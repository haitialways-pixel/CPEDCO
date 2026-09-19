using CPCREDO.Application.Common;
using CPCREDO.Application.Savings;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Members;
using CPCREDO.Domain.Savings;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CPCREDO.Infrastructure.Savings;

public sealed class SavingsService : ISavingsService
{
    private readonly CpcredoDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditLogger _audit;

    public SavingsService(
        CpcredoDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        IAuditLogger audit)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
    }

    public async Task<Result<IReadOnlyList<SavingsProductDto>>> ListProductsAsync(CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<IReadOnlyList<SavingsProductDto>>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var products = await _db.SavingsProducts
            .AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && x.IsActive)
            .OrderBy(x => x.LegalName)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<SavingsProductDto>>.Ok(products.Select(SavingsProductService.Map).ToList());
    }

    public async Task<Result<OpenedAccountDto>> OpenMemberAccountAsync(
        OpenMemberAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var kind = (request.Kind ?? "Epargne").Trim();
        if (kind.Equals("Epargne", StringComparison.OrdinalIgnoreCase))
        {
            if (request.ProductId is null || request.ProductId == Guid.Empty)
                return Result<OpenedAccountDto>.Fail("savings.product.required", "Choisissez un produit d’épargne.");
            var opened = await OpenAccountAsync(request.MemberId, request.ProductId.Value, cancellationToken);
            if (!opened.IsSuccess)
                return Result<OpenedAccountDto>.Fail(opened.ErrorCode!, opened.ErrorMessage!);
            var a = opened.Value!;
            return Result<OpenedAccountDto>.Ok(new OpenedAccountDto(
                "Epargne", a.Id, a.AccountNo, a.ProductName, a.CurrencyCode, a.LedgerBalance, a.IsActive));
        }

        if (kind.Equals("Qualification", StringComparison.OrdinalIgnoreCase)
            || kind.Equals("Permanent", StringComparison.OrdinalIgnoreCase)
            || kind.Equals("Parts qualification", StringComparison.OrdinalIgnoreCase)
            || kind.Equals("Parts permanentes", StringComparison.OrdinalIgnoreCase))
        {
            var shareType = kind.StartsWith("Perm", StringComparison.OrdinalIgnoreCase)
                || kind.Contains("perman", StringComparison.OrdinalIgnoreCase)
                ? ShareType.Permanent
                : ShareType.Qualification;
            return await OpenShareAccountAsync(request.MemberId, shareType, cancellationToken);
        }

        return Result<OpenedAccountDto>.Fail("savings.kind", "Type de compte invalide (Epargne, Parts qualification, Parts permanentes).");
    }

    public async Task<Result<SavingsAccountDto>> OpenAccountAsync(
        Guid memberId,
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var gate = RequireWriter();
        if (!gate.IsSuccess)
            return Result<SavingsAccountDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var tenantId = _currentUser.TenantId!.Value;

        var member = await _db.Members.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == memberId && x.TenantId == tenantId, cancellationToken);
        if (member is null)
            return Result<SavingsAccountDto>.Fail("savings.member.not_found", "Membre introuvable.");
        if (member.Status != MemberStatus.Active)
            return Result<SavingsAccountDto>.Fail("savings.member.inactive", "Seuls les membres actifs peuvent ouvrir un compte d’épargne.");
        if (MembershipRules.ServicesBlocked(member, _clock.UtcNow))
            return Result<SavingsAccountDto>.Fail("member.usager_expired", "Période d’usage échue : conversion en sociétaire requise avant tout service.");

        var product = await _db.SavingsProducts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == productId && x.TenantId == tenantId && x.IsActive, cancellationToken);
        if (product is null)
            return Result<SavingsAccountDto>.Fail("savings.product.not_found", "Produit d’épargne introuvable.");
        if (member.LegalStatus == LegalStatus.Usager && product.IsTermLike)
            return Result<SavingsAccountDto>.Fail(
                "savings.usager_terme",
                "Un usager ne peut ouvrir qu’une épargne à vue.");

        var exists = await _db.SavingsAccounts.AnyAsync(
            x => x.TenantId == tenantId && x.MemberId == memberId && x.ProductId == productId && x.IsActive,
            cancellationToken);
        if (exists)
            return Result<SavingsAccountDto>.Fail("savings.account.exists", "Ce membre a déjà un compte pour ce produit.");

        var account = new SavingsAccount
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            MemberId = memberId,
            BranchId = member.BranchId,
            ProductId = productId,
            AccountNo = await NextAccountNoAsync(tenantId, cancellationToken),
            CurrencyCode = product.CurrencyCode,
            MinimumBalance = product.MinimumBalance,
            MaturesOn = product.IsTermLike && product.TermDays is > 0
                ? _clock.TodayInPortAuPrince().AddDays(product.TermDays.Value)
                : null,
            AllowWithdrawBeforeTerm = product.AllowWithdrawBeforeTerm,
            IsActive = true,
            OpenedAtUtc = _clock.UtcNow
        };

        _db.SavingsAccounts.Add(account);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            "Savings.AccountOpened",
            nameof(SavingsAccount),
            account.Id,
            new { memberId, productId, account.AccountNo },
            tenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);

        return Result<SavingsAccountDto>.Ok(MapAccount(account, product.DisplayName, product.ProductKind.ToString(), 0m, 0m, []));
    }

    private async Task<Result<OpenedAccountDto>> OpenShareAccountAsync(
        Guid memberId,
        ShareType shareType,
        CancellationToken cancellationToken)
    {
        var gate = RequireWriter();
        if (!gate.IsSuccess)
            return Result<OpenedAccountDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var tenantId = _currentUser.TenantId!.Value;
        var member = await _db.Members
            .Include(m => m.ShareAccounts)
            .FirstOrDefaultAsync(x => x.Id == memberId && x.TenantId == tenantId, cancellationToken);
        if (member is null)
            return Result<OpenedAccountDto>.Fail("savings.member.not_found", "Membre introuvable.");
        if (member.Status != MemberStatus.Active)
            return Result<OpenedAccountDto>.Fail("savings.member.inactive", "Seuls les membres actifs peuvent ouvrir un compte.");
        if (MembershipRules.ServicesBlocked(member, _clock.UtcNow))
            return Result<OpenedAccountDto>.Fail("member.usager_expired", "Période d’usage échue : conversion en sociétaire requise avant tout service.");

        if (shareType == ShareType.Qualification)
        {
            if (member.LegalStatus == LegalStatus.Usager)
                return Result<OpenedAccountDto>.Fail(
                    "savings.usager_parts",
                    "Un usager n’ouvre pas de parts de qualification hors conversion.");
            if (member.LegalStatus == LegalStatus.Auxiliaire)
                return Result<OpenedAccountDto>.Fail(
                    "savings.auxiliaire_parts",
                    "Un auxiliaire n’ouvre pas de parts de qualification.");
        }

        if (shareType == ShareType.Permanent)
        {
            if (member.LegalStatus == LegalStatus.Usager)
                return Result<OpenedAccountDto>.Fail(
                    "savings.usager_permanent",
                    "Un usager ne peut pas ouvrir de parts permanentes.");
            if (member.LegalStatus != LegalStatus.Societaire && member.LegalStatus != LegalStatus.Auxiliaire)
                return Result<OpenedAccountDto>.Fail(
                    "savings.permanent_status",
                    "Seuls les sociétaires et les auxiliaires ouvrent des parts permanentes.");
        }

        var existing = member.ShareAccounts.FirstOrDefault(s => s.ShareType == shareType);
        if (existing is not null)
        {
            return Result<OpenedAccountDto>.Ok(new OpenedAccountDto(
                shareType.ToString(),
                existing.Id,
                existing.AccountNo,
                shareType == ShareType.Qualification ? "Parts de qualification" : "Parts permanentes",
                existing.CurrencyCode,
                existing.BookValue,
                existing.IsActive));
        }

        var par = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.ShareParValue)
            .FirstOrDefaultAsync(cancellationToken);
        par = MembershipRules.NormalizeParValue(par);
        var prefix = shareType == ShareType.Permanent
            ? MembershipRules.PermanentShareAccountNoPrefix
            : MembershipRules.ShareAccountNoPrefix;
        var key = shareType == ShareType.Permanent ? "PermanentShareAccountNo" : "ShareAccountNo";
        var sequence = await _db.NumberSequences
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Key == key, cancellationToken);
        if (sequence is null)
        {
            sequence = new NumberSequence { Id = Guid.NewGuid(), TenantId = tenantId, Key = key, LastValue = 0 };
            _db.NumberSequences.Add(sequence);
        }

        sequence.LastValue += 1;
        var account = new ShareAccount
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            MemberId = member.Id,
            BranchId = member.BranchId,
            AccountNo = $"{prefix}{sequence.LastValue:000000}",
            ShareType = shareType,
            ShareCount = 0,
            ParValue = par,
            CurrencyCode = Currencies.Htg,
            IsActive = true,
            OpenedAtUtc = _clock.UtcNow
        };
        _db.ShareAccounts.Add(account);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(
            "Member.ShareAccountOpened",
            nameof(ShareAccount),
            account.Id,
            new { memberId, shareType = shareType.ToString(), account.AccountNo },
            tenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);

        return Result<OpenedAccountDto>.Ok(new OpenedAccountDto(
            shareType.ToString(),
            account.Id,
            account.AccountNo,
            shareType == ShareType.Qualification ? "Parts de qualification" : "Parts permanentes",
            account.CurrencyCode,
            0m,
            true));
    }

    public async Task<Result<SavingsAccountDto>> GetAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<SavingsAccountDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var account = await _db.SavingsAccounts
            .AsNoTracking()
            .Include(x => x.Product)
            .FirstOrDefaultAsync(x => x.Id == accountId && x.TenantId == _currentUser.TenantId, cancellationToken);
        if (account is null)
            return Result<SavingsAccountDto>.Fail("savings.account.not_found", "Compte d’épargne introuvable.");

        var (ledger, available) = await ComputeBalancesAsync(account.Id, cancellationToken);
        var holds = await ListActiveHoldsAsync(account.Id, cancellationToken);
        return Result<SavingsAccountDto>.Ok(MapAccount(
            account,
            account.Product?.DisplayName ?? string.Empty,
            account.Product?.ProductKind.ToString() ?? "AVue",
            ledger,
            available,
            holds));
    }

    public async Task<Result<IReadOnlyList<SavingsAccountDto>>> ListMemberAccountsAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<IReadOnlyList<SavingsAccountDto>>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var accounts = await _db.SavingsAccounts
            .AsNoTracking()
            .Include(x => x.Product)
            .Where(x => x.TenantId == _currentUser.TenantId && x.MemberId == memberId)
            .OrderBy(x => x.AccountNo)
            .ToListAsync(cancellationToken);

        var result = new List<SavingsAccountDto>();
        foreach (var account in accounts)
        {
            var (ledger, available) = await ComputeBalancesAsync(account.Id, cancellationToken);
            var holds = await ListActiveHoldsAsync(account.Id, cancellationToken);
            result.Add(MapAccount(
                account,
                account.Product?.DisplayName ?? string.Empty,
                account.Product?.ProductKind.ToString() ?? "AVue",
                ledger,
                available,
                holds));
        }

        return Result<IReadOnlyList<SavingsAccountDto>>.Ok(result);
    }

    public async Task<Result<SavingsStatementDto>> GetStatementAsync(
        Guid accountId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<SavingsStatementDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        if (to < from)
            return Result<SavingsStatementDto>.Fail("savings.statement.range", "La date de fin doit être postérieure ou égale à la date de début.");

        var account = await _db.SavingsAccounts
            .AsNoTracking()
            .Include(x => x.Product)
            .FirstOrDefaultAsync(x => x.Id == accountId && x.TenantId == _currentUser.TenantId, cancellationToken);
        if (account is null)
            return Result<SavingsStatementDto>.Fail("savings.account.not_found", "Compte d’épargne introuvable.");

        var fromUtc = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(to.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);

        var ledgerEntries = await _db.SavingsLedgerEntries
            .AsNoTracking()
            .Where(x => x.SavingsAccountId == accountId)
            .OrderBy(x => x.ValueDateUtc)
            .ThenBy(x => x.PostedAtUtc)
            .ToListAsync(cancellationToken);

        var opening = MoneyAmount.Normalize(
            ledgerEntries.Where(x => x.ValueDateUtc < fromUtc).Sum(x => x.SignedAmount));

        var running = opening;
        var lines = new List<SavingsStatementEntryDto>();
        foreach (var entry in ledgerEntries.Where(x => x.ValueDateUtc >= fromUtc && x.ValueDateUtc <= toUtc))
        {
            running = MoneyAmount.Normalize(running + entry.SignedAmount);
            lines.Add(new SavingsStatementEntryDto(
                entry.ValueDateUtc,
                entry.PostedAtUtc,
                entry.EntryType,
                MoneyAmount.Normalize(entry.Amount),
                entry.Description,
                running));
        }

        var (ledger, available) = await ComputeBalancesAsync(account.Id, cancellationToken);

        return Result<SavingsStatementDto>.Ok(new SavingsStatementDto(
            account.Id,
            account.AccountNo,
            account.Product?.Name ?? string.Empty,
            account.CurrencyCode,
            ledger,
            available,
            from,
            to,
            lines));
    }

    public async Task<Result<StatementPdfDto>> GetStatementPdfAsync(
        Guid accountId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var statement = await GetStatementAsync(accountId, from, to, cancellationToken);
        if (!statement.IsSuccess)
            return Result<StatementPdfDto>.Fail(statement.ErrorCode!, statement.ErrorMessage!);

        var account = await _db.SavingsAccounts.AsNoTracking()
            .Include(x => x.Member)
            .FirstAsync(x => x.Id == accountId, cancellationToken);

        var bytes = StatementPdf.Render(
            statement.Value!,
            account.Member?.MemberNo ?? string.Empty,
            account.Member?.FullName ?? string.Empty);

        var fileName = $"releve-{statement.Value!.AccountNo}-{from:yyyyMMdd}-{to:yyyyMMdd}.pdf";
        return Result<StatementPdfDto>.Ok(new StatementPdfDto(bytes, fileName));
    }

    public async Task<Result<StatementPdfDto>> PrintLivretPdfAsync(
        Guid accountId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<StatementPdfDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var account = await _db.SavingsAccounts
            .Include(x => x.Product)
            .Include(x => x.Member)
            .FirstOrDefaultAsync(x => x.Id == accountId && x.TenantId == _currentUser.TenantId, cancellationToken);
        if (account is null)
            return Result<StatementPdfDto>.Fail("savings.account.not_found", "Compte d’épargne introuvable.");

        var today = _clock.TodayInPortAuPrince();
        var fromDate = from ?? DateOnly.FromDateTime(
            (account.LastPassbookPrintAtUtc ?? account.OpenedAtUtc).Kind == DateTimeKind.Utc
                ? _clock.ToPortAuPrince(account.LastPassbookPrintAtUtc ?? account.OpenedAtUtc)
                : (account.LastPassbookPrintAtUtc ?? account.OpenedAtUtc));
        var toDate = to ?? today;
        if (toDate < fromDate)
            return Result<StatementPdfDto>.Fail("savings.statement.range", "La date de fin doit être postérieure ou égale à la date de début.");

        var fromUtc = DateTime.SpecifyKind(fromDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(toDate.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);
        var stamp = from is null ? account.LastPassbookPrintAtUtc : null;

        var ledgerEntries = await _db.SavingsLedgerEntries
            .AsNoTracking()
            .Where(x => x.SavingsAccountId == accountId)
            .OrderBy(x => x.ValueDateUtc)
            .ThenBy(x => x.PostedAtUtc)
            .ToListAsync(cancellationToken);

        var tillIds = ledgerEntries.Where(x => x.TillSessionId is not null).Select(x => x.TillSessionId!.Value).Distinct().ToList();
        var cashiers = tillIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await (
                from t in _db.TillSessions.AsNoTracking()
                join u in _db.Users.AsNoTracking() on t.UserId equals u.Id
                where tillIds.Contains(t.Id)
                select new { t.Id, u.FullName }
            ).ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);

        var openingSource = stamp is { } s
            ? ledgerEntries.Where(x => x.PostedAtUtc <= s)
            : ledgerEntries.Where(x => x.ValueDateUtc < fromUtc);
        var running = MoneyAmount.Normalize(openingSource.Sum(x => x.SignedAmount));

        var printed = stamp is { } printedAfter
            ? ledgerEntries.Where(x => x.PostedAtUtc > printedAfter && x.PostedAtUtc <= toUtc).ToList()
            : ledgerEntries.Where(x => x.ValueDateUtc >= fromUtc && x.ValueDateUtc <= toUtc).ToList();

        var lines = new List<LivretLineDto>();
        foreach (var entry in printed)
        {
            running = MoneyAmount.Normalize(running + entry.SignedAmount);
            var debit = string.Equals(entry.EntryType, "Debit", StringComparison.OrdinalIgnoreCase) ? entry.Amount : 0m;
            var credit = string.Equals(entry.EntryType, "Credit", StringComparison.OrdinalIgnoreCase) ? entry.Amount : 0m;
            var cashier = entry.TillSessionId is { } tid && cashiers.TryGetValue(tid, out var name) ? name : "—";
            lines.Add(new LivretLineDto(
                entry.ValueDateUtc,
                entry.Description,
                MoneyAmount.Normalize(debit),
                MoneyAmount.Normalize(credit),
                running,
                cashier));
        }

        var (_, available) = await ComputeBalancesAsync(account.Id, cancellationToken);
        var last = ledgerEntries.LastOrDefault();
        var livret = new LivretDto(
            account.Id,
            account.AccountNo,
            account.Product?.Name ?? string.Empty,
            account.CurrencyCode,
            account.Member?.MemberNo ?? string.Empty,
            account.Member?.FullName ?? string.Empty,
            fromDate,
            toDate,
            available,
            last?.ValueDateUtc,
            last is null ? null : MoneyAmount.Normalize(last.Amount),
            last?.EntryType,
            lines);

        var bytes = LivretPdf.Render(livret);
        account.LastPassbookPrintAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(
            "Savings.LivretPrinted",
            nameof(SavingsAccount),
            account.Id,
            new { account.AccountNo, from = fromDate, to = toDate, lines = lines.Count },
            account.TenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);

        var fileName = $"livret-{account.AccountNo}-{fromDate:yyyyMMdd}-{toDate:yyyyMMdd}.pdf";
        return Result<StatementPdfDto>.Ok(new StatementPdfDto(bytes, fileName));
    }

    public async Task<Result<SavingsAccountDto>> PlaceHoldAsync(
        Guid accountId,
        PlaceHoldRequest request,
        CancellationToken cancellationToken = default)
    {
        var gate = RequireWriter();
        if (!gate.IsSuccess)
            return Result<SavingsAccountDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var amount = MoneyAmount.Normalize(request.Amount);
        if (amount <= 0m)
            return Result<SavingsAccountDto>.Fail("savings.hold.amount", "Le montant du gel doit être supérieur à zéro.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result<SavingsAccountDto>.Fail("savings.hold.reason", "Indiquez le motif du gel.");

        var account = await LoadAccountAsync(accountId, cancellationToken);
        if (account is null)
            return Result<SavingsAccountDto>.Fail("savings.account.not_found", "Compte d’épargne introuvable.");

        var (ledger, available) = await ComputeBalancesAsync(account.Id, cancellationToken);
        if (amount > available)
            return Result<SavingsAccountDto>.Fail(
                "savings.hold.insufficient",
                $"Disponible insuffisant pour ce gel ({MoneyDisplay.Format(available, account.CurrencyCode)}).");

        account.Liens.Add(new SavingsLien
        {
            TenantId = account.TenantId,
            SavingsAccountId = account.Id,
            CurrencyCode = account.CurrencyCode,
            Amount = amount,
            Reason = request.Reason.Trim(),
            CreatedAtUtc = _clock.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            "Savings.HoldPlaced",
            nameof(SavingsAccount),
            account.Id,
            new { amount, request.Reason },
            account.TenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);

        return await GetAccountAsync(account.Id, cancellationToken);
    }

    public async Task<Result<SavingsAccountDto>> ReleaseHoldAsync(
        Guid accountId,
        Guid holdId,
        CancellationToken cancellationToken = default)
    {
        var gate = RequireWriter();
        if (!gate.IsSuccess)
            return Result<SavingsAccountDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var hold = await _db.SavingsLiens
            .FirstOrDefaultAsync(
                x => x.Id == holdId && x.SavingsAccountId == accountId && x.TenantId == _currentUser.TenantId,
                cancellationToken);
        if (hold is null)
            return Result<SavingsAccountDto>.Fail("savings.hold.not_found", "Gel introuvable.");
        if (hold.ReleasedAtUtc is not null)
            return Result<SavingsAccountDto>.Fail("savings.hold.released", "Ce gel est déjà levé.");

        hold.ReleasedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            "Savings.HoldReleased",
            nameof(SavingsLien),
            hold.Id,
            new { hold.Amount, hold.Reason },
            hold.TenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);

        return await GetAccountAsync(accountId, cancellationToken);
    }

    public async Task<Result<SavingsAccountDto>> BlockAccountAsync(
        Guid accountId,
        BlockAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var gate = RequireWriter();
        if (!gate.IsSuccess)
            return Result<SavingsAccountDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result<SavingsAccountDto>.Fail("savings.block.reason", "Indiquez le motif du blocage.");

        var account = await LoadAccountAsync(accountId, cancellationToken);
        if (account is null)
            return Result<SavingsAccountDto>.Fail("savings.account.not_found", "Compte d’épargne introuvable.");
        if (account.IsBlocked)
            return Result<SavingsAccountDto>.Fail("savings.block.already", "Ce compte est déjà bloqué.");

        account.IsBlocked = true;
        account.BlockedReason = request.Reason.Trim();
        account.BlockedAtUtc = _clock.UtcNow;
        account.BlockedByUserId = _currentUser.UserId;
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            "Savings.Blocked",
            nameof(SavingsAccount),
            account.Id,
            new { account.BlockedReason },
            account.TenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);

        return await GetAccountAsync(account.Id, cancellationToken);
    }

    public async Task<Result<SavingsAccountDto>> UnblockAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var gate = RequireWriter();
        if (!gate.IsSuccess)
            return Result<SavingsAccountDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var account = await LoadAccountAsync(accountId, cancellationToken);
        if (account is null)
            return Result<SavingsAccountDto>.Fail("savings.account.not_found", "Compte d’épargne introuvable.");
        if (!account.IsBlocked)
            return Result<SavingsAccountDto>.Fail("savings.block.not_blocked", "Ce compte n’est pas bloqué.");

        account.IsBlocked = false;
        account.BlockedReason = null;
        account.BlockedAtUtc = null;
        account.BlockedByUserId = null;
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            "Savings.Unblocked",
            nameof(SavingsAccount),
            account.Id,
            null,
            account.TenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);

        return await GetAccountAsync(account.Id, cancellationToken);
    }

    private Task<SavingsAccount?> LoadAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        _db.SavingsAccounts
            .Include(x => x.Product)
            .Include(x => x.Liens)
            .FirstOrDefaultAsync(x => x.Id == accountId && x.TenantId == _currentUser.TenantId, cancellationToken);

    private async Task<(decimal Ledger, decimal Available)> ComputeBalancesAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var entries = await _db.SavingsLedgerEntries
            .AsNoTracking()
            .Where(x => x.SavingsAccountId == accountId)
            .ToListAsync(cancellationToken);
        var ledger = MoneyAmount.Normalize(entries.Sum(x => x.SignedAmount));

        var liens = await _db.SavingsLiens
            .AsNoTracking()
            .Where(x => x.SavingsAccountId == accountId && x.ReleasedAtUtc == null)
            .Select(x => x.Amount)
            .ToListAsync(cancellationToken);
        var lienTotal = MoneyAmount.Normalize(liens.Sum());

        return (ledger, MoneyAmount.Normalize(ledger - lienTotal));
    }

    private async Task<string> NextAccountNoAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var sequence = await _db.NumberSequences
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Key == "SavingsAccountNo", cancellationToken);
        if (sequence is null)
        {
            sequence = new NumberSequence
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Key = "SavingsAccountNo",
                LastValue = 0
            };
            _db.NumberSequences.Add(sequence);
        }

        sequence.LastValue += 1;
        await _db.SaveChangesAsync(cancellationToken);
        return $"A-{sequence.LastValue:000000}";
    }

    private Result<bool> RequireUser()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.TenantId is null || _currentUser.UserId is null)
            return Result<bool>.Fail("auth.unauthorized", "Session invalide.");
        return Result<bool>.Ok(true);
    }

    private Result<bool> RequireWriter()
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return auth;
        if (!_currentUser.Roles.Any(r => RoleNames.WriteRoles.Contains(r)))
            return Result<bool>.Fail("auth.forbidden", "Le commissaire a un accès en lecture seule.");
        return Result<bool>.Ok(true);
    }

    private async Task<IReadOnlyList<SavingsHoldDto>> ListActiveHoldsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return await _db.SavingsLiens.AsNoTracking()
            .Where(x => x.SavingsAccountId == accountId && x.ReleasedAtUtc == null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new SavingsHoldDto(x.Id, x.Amount, x.Reason, x.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    private static SavingsAccountDto MapAccount(
        SavingsAccount account,
        string productName,
        string productKind,
        decimal ledger,
        decimal available,
        IReadOnlyList<SavingsHoldDto> holds) =>
        new(
            account.Id,
            account.MemberId,
            account.ProductId,
            account.AccountNo,
            productName,
            account.CurrencyCode,
            ledger,
            available,
            account.IsActive,
            account.IsBlocked,
            account.BlockedReason,
            account.OpenedAtUtc,
            account.LastPassbookPrintAtUtc,
            account.MaturesOn,
            account.AllowWithdrawBeforeTerm,
            productKind,
            holds);
}
