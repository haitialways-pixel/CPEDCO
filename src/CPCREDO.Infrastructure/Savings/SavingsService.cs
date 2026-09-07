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
            .OrderBy(x => x.Name)
            .Select(x => new SavingsProductDto(
                x.Id,
                x.Name,
                x.CurrencyCode,
                x.MinimumBalance,
                x.LiabilityGlAccountId,
                x.CashGlAccountId,
                x.IsActive))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<SavingsProductDto>>.Ok(products);
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

        return Result<SavingsAccountDto>.Ok(MapAccount(account, product.Name, 0m, 0m, []));
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
        return Result<SavingsAccountDto>.Ok(MapAccount(account, account.Product?.Name ?? string.Empty, ledger, available, holds));
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
            result.Add(MapAccount(account, account.Product?.Name ?? string.Empty, ledger, available, holds));
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
            holds);
}
