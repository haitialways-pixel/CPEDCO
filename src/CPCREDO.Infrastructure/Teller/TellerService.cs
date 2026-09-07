using CPCREDO.Application.Accounting;
using CPCREDO.Application.Common;
using CPCREDO.Application.Teller;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Members;
using CPCREDO.Domain.Savings;
using CPCREDO.Domain.Teller;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CPCREDO.Infrastructure.Teller;

public sealed class TellerService : ITellerService
{
    private readonly CpcredoDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IJournalService _journals;
    private readonly IAuditLogger _audit;

    public TellerService(
        CpcredoDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        IJournalService journals,
        IAuditLogger audit)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _journals = journals;
        _audit = audit;
    }

    public async Task<Result<TillSessionDto>> OpenAsync(OpenTillRequest request, CancellationToken cancellationToken = default)
    {
        var gate = RequireWriter();
        if (!gate.IsSuccess)
            return Result<TillSessionDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var currency = NormalizeCurrency(request.CurrencyCode);
        if (currency is null)
            return Result<TillSessionDto>.Fail("till.currency", "Devise non supportée.");

        var floatAmount = MoneyAmount.Normalize(request.OpeningFloat);
        if (floatAmount < 0m)
            return Result<TillSessionDto>.Fail("till.float", "Le fond de caisse ne peut pas être négatif.");

        var tenantId = _currentUser.TenantId!.Value;
        var userId = _currentUser.UserId!.Value;
        var branchId = _currentUser.BranchId!.Value;

        var openExists = await _db.TillSessions.AnyAsync(
            t => t.TenantId == tenantId && t.UserId == userId && t.BranchId == branchId
                 && t.CurrencyCode == currency && t.Status == TillSessionStatus.Open,
            cancellationToken);
        if (openExists)
            return Result<TillSessionDto>.Fail("till.already_open", "Une caisse est déjà ouverte pour cet utilisateur, cette agence et cette devise.");

        var till = new TillSession
        {
            TenantId = tenantId,
            BranchId = branchId,
            UserId = userId,
            CurrencyCode = currency,
            Status = TillSessionStatus.Open,
            OpeningFloat = floatAmount,
            ExpectedCash = floatAmount,
            OpenedAtUtc = _clock.UtcNow
        };
        _db.TillSessions.Add(till);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            "Till.Opened",
            nameof(TillSession),
            till.Id,
            new { till.CurrencyCode, till.OpeningFloat },
            tenantId,
            userId,
            cancellationToken: cancellationToken);

        return Result<TillSessionDto>.Ok(MapTill(till));
    }

    public async Task<Result<TillSessionDto>> GetCurrentAsync(string? currencyCode, CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<TillSessionDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var currency = NormalizeCurrency(currencyCode) ?? Currencies.Htg;
        var till = await FindOpenTillAsync(currency, cancellationToken);
        if (till is null)
            return Result<TillSessionDto>.Fail("till.not_open", "Aucune caisse ouverte pour cet utilisateur et cette agence.");

        return Result<TillSessionDto>.Ok(MapTill(till));
    }

    public async Task<Result<TillSessionDto>> CloseAsync(
        Guid tillId,
        CloseTillRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var gate = RequireWriter();
        if (!gate.IsSuccess)
            return Result<TillSessionDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var till = await _db.TillSessions
            .Include(t => t.CountLines)
            .FirstOrDefaultAsync(
                t => t.Id == tillId && t.TenantId == _currentUser.TenantId && t.UserId == _currentUser.UserId,
                cancellationToken);
        if (till is null)
            return Result<TillSessionDto>.Fail("till.not_found", "Caisse introuvable.");
        if (till.Status != TillSessionStatus.Open)
            return Result<TillSessionDto>.Fail("till.already_closed", "Cette caisse est déjà fermée.");

        var counted = 0m;
        till.CountLines.Clear();
        foreach (var line in request.Denominations ?? [])
        {
            if (line.Quantity < 0 || line.FaceValue <= 0m)
                return Result<TillSessionDto>.Fail("till.count", "Dénominations invalides.");
            var qty = line.Quantity;
            var face = MoneyAmount.Normalize(line.FaceValue);
            counted += face * qty;
            till.CountLines.Add(new TillCountLine
            {
                TillSessionId = till.Id,
                FaceValue = face,
                Quantity = qty
            });
        }

        counted = MoneyAmount.Normalize(counted);
        var expected = MoneyAmount.Normalize(till.ExpectedCash);
        var overShort = MoneyAmount.Normalize(counted - expected);

        till.CountedCash = counted;
        till.OverShortAmount = overShort;
        till.Status = TillSessionStatus.Closed;
        till.ClosedAtUtc = _clock.UtcNow;

        if (overShort != 0m)
        {
            var cashGl = CashGl(till.CurrencyCode);
            var pnlGl = overShort > 0m
                ? SeedGuids.Gl("4040")
                : SeedGuids.Gl("5050");

            var abs = overShort > 0m ? overShort : -overShort;
            var journal = await _journals.PostAsync(new CreateJournalRequest
            {
                Description = overShort > 0m
                    ? $"Excédent de caisse {till.CurrencyCode}"
                    : $"Manquant de caisse {till.CurrencyCode}",
                CurrencyCode = till.CurrencyCode,
                BranchId = till.BranchId,
                Lines = overShort > 0m
                    ?
                    [
                        new CreateJournalLineRequest { GlAccountId = cashGl, Debit = abs, Credit = 0m, Description = "Caisse" },
                        new CreateJournalLineRequest { GlAccountId = pnlGl, Debit = 0m, Credit = abs, Description = "Écart de caisse" }
                    ]
                    :
                    [
                        new CreateJournalLineRequest { GlAccountId = pnlGl, Debit = abs, Credit = 0m, Description = "Écart de caisse" },
                        new CreateJournalLineRequest { GlAccountId = cashGl, Debit = 0m, Credit = abs, Description = "Caisse" }
                    ]
            }, idempotencyKey, cancellationToken);

            if (!journal.IsSuccess)
                return Result<TillSessionDto>.Fail(journal.ErrorCode!, journal.ErrorMessage!);

            till.OverShortJournalId = journal.Value!.Id;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(
            "Till.Closed",
            nameof(TillSession),
            till.Id,
            new { till.ExpectedCash, till.CountedCash, till.OverShortAmount },
            till.TenantId,
            till.UserId,
            cancellationToken: cancellationToken);

        return Result<TillSessionDto>.Ok(MapTill(till));
    }

    public Task<Result<CashPostResultDto>> DepositAsync(
        CashPostRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default) =>
        PostCashAsync(request, isDeposit: true, idempotencyKey, cancellationToken);

    public Task<Result<CashPostResultDto>> WithdrawAsync(
        CashPostRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default) =>
        PostCashAsync(request, isDeposit: false, idempotencyKey, cancellationToken);

    private async Task<Result<CashPostResultDto>> PostCashAsync(
        CashPostRequest request,
        bool isDeposit,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var gate = RequireWriter();
        if (!gate.IsSuccess)
            return Result<CashPostResultDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        var amount = MoneyAmount.Normalize(request.Amount);
        if (amount <= 0m)
            return Result<CashPostResultDto>.Fail("teller.amount", "Le montant doit être supérieur à zéro.");

        var tenantId = _currentUser.TenantId!.Value;

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var replay = await ReplayAsync(tenantId, idempotencyKey, cancellationToken);
            if (replay is not null)
                return Result<CashPostResultDto>.Ok(replay);
        }

        var account = await _db.SavingsAccounts
            .Include(a => a.Product)
            .Include(a => a.Member)
            .FirstOrDefaultAsync(
                a => a.Id == request.SavingsAccountId && a.TenantId == tenantId && a.IsActive,
                cancellationToken);
        if (account is null)
            return Result<CashPostResultDto>.Fail("savings.account.not_found", "Compte d’épargne introuvable.");
        if (account.Product is null)
            return Result<CashPostResultDto>.Fail("savings.product.not_found", "Produit d’épargne introuvable.");
        if (account.IsBlocked)
            return Result<CashPostResultDto>.Fail(
                "savings.blocked",
                $"Compte bloqué : {account.BlockedReason ?? "gel administratif"}.");
        if (account.Member is not null && MembershipRules.ServicesBlocked(account.Member, _clock.UtcNow))
            return Result<CashPostResultDto>.Fail(
                "member.usager_expired",
                "Période d’usage échue : conversion en sociétaire requise avant tout mouvement de caisse.");

        var till = await FindOpenTillAsync(account.CurrencyCode, cancellationToken);
        if (till is null)
            return Result<CashPostResultDto>.Fail(
                "till.not_open",
                "Impossible de poster en caisse : aucune caisse ouverte pour cet utilisateur et cette agence.");

        var (ledger, available) = await ComputeBalancesAsync(account.Id, cancellationToken);
        if (!isDeposit && amount > available)
            return Result<CashPostResultDto>.Fail(
                "teller.insufficient",
                $"Solde disponible insuffisant ({MoneyDisplay.Format(available, account.CurrencyCode)}).");

        var cashGl = account.Product.CashGlAccountId;
        var liabilityGl = account.Product.LiabilityGlAccountId;
        var type = isDeposit ? "deposit" : "withdrawal";
        var title = isDeposit ? "Dépôt en espèces" : "Retrait en espèces";

        var journal = await _journals.PostAsync(new CreateJournalRequest
        {
            Description = $"{title} {account.AccountNo}",
            CurrencyCode = account.CurrencyCode,
            BranchId = till.BranchId,
            Lines = isDeposit
                ?
                [
                    new CreateJournalLineRequest { GlAccountId = cashGl, Debit = amount, Credit = 0m, Description = "Caisse" },
                    new CreateJournalLineRequest { GlAccountId = liabilityGl, Debit = 0m, Credit = amount, Description = "Épargne membre" }
                ]
                :
                [
                    new CreateJournalLineRequest { GlAccountId = liabilityGl, Debit = amount, Credit = 0m, Description = "Épargne membre" },
                    new CreateJournalLineRequest { GlAccountId = cashGl, Debit = 0m, Credit = amount, Description = "Caisse" }
                ]
        }, idempotencyKey, cancellationToken);

        if (!journal.IsSuccess)
            return Result<CashPostResultDto>.Fail(journal.ErrorCode!, journal.ErrorMessage!);

        _db.SavingsLedgerEntries.Add(new SavingsLedgerEntry
        {
            TenantId = tenantId,
            SavingsAccountId = account.Id,
            ValueDateUtc = DateTime.SpecifyKind(
                _clock.TodayInPortAuPrince().ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
            PostedAtUtc = _clock.UtcNow,
            EntryType = isDeposit ? "Credit" : "Debit",
            Amount = amount,
            CurrencyCode = account.CurrencyCode,
            Description = title,
            JournalEntryId = journal.Value!.Id,
            TillSessionId = till.Id,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim()
        });

        till.ExpectedCash = MoneyAmount.Normalize(till.ExpectedCash + (isDeposit ? amount : -amount));
        await _db.SaveChangesAsync(cancellationToken);

        var (newLedger, newAvailable) = await ComputeBalancesAsync(account.Id, cancellationToken);
        var receipt = BuildReceipt(
            type,
            title,
            journal.Value,
            account,
            amount,
            newLedger,
            newAvailable);

        return Result<CashPostResultDto>.Ok(new CashPostResultDto(
            receipt,
            journal.Value.Id,
            journal.Value.Lines.Count,
            newLedger,
            newAvailable));
    }

    private async Task<CashPostResultDto?> ReplayAsync(Guid tenantId, string key, CancellationToken cancellationToken)
    {
        var entry = await _db.SavingsLedgerEntries.AsNoTracking()
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.IdempotencyKey == key, cancellationToken);
        if (entry is null)
            return null;

        var account = await _db.SavingsAccounts.AsNoTracking()
            .Include(a => a.Product)
            .Include(a => a.Member)
            .FirstAsync(a => a.Id == entry.SavingsAccountId, cancellationToken);
        var journal = await _journals.GetAsync(entry.JournalEntryId!.Value, cancellationToken);
        if (!journal.IsSuccess)
            return null;

        var (ledger, available) = await ComputeBalancesAsync(account.Id, cancellationToken);
        var isDeposit = entry.EntryType == "Credit";
        var receipt = BuildReceipt(
            isDeposit ? "deposit" : "withdrawal",
            isDeposit ? "Dépôt en espèces" : "Retrait en espèces",
            journal.Value!,
            account,
            entry.Amount,
            ledger,
            available);
        return new CashPostResultDto(receipt, journal.Value!.Id, journal.Value.Lines.Count, ledger, available);
    }

    private async Task<(decimal Ledger, decimal Available)> ComputeBalancesAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var entries = await _db.SavingsLedgerEntries.AsNoTracking()
            .Where(x => x.SavingsAccountId == accountId)
            .ToListAsync(cancellationToken);
        var ledger = MoneyAmount.Normalize(entries.Sum(x => x.SignedAmount));
        var liens = await _db.SavingsLiens.AsNoTracking()
            .Where(x => x.SavingsAccountId == accountId && x.ReleasedAtUtc == null)
            .Select(x => x.Amount)
            .ToListAsync(cancellationToken);
        return (ledger, MoneyAmount.Normalize(ledger - liens.Sum()));
    }

    private Task<TillSession?> FindOpenTillAsync(string currency, CancellationToken cancellationToken) =>
        _db.TillSessions.FirstOrDefaultAsync(
            t => t.TenantId == _currentUser.TenantId
                 && t.UserId == _currentUser.UserId
                 && t.BranchId == _currentUser.BranchId
                 && t.CurrencyCode == currency
                 && t.Status == TillSessionStatus.Open,
            cancellationToken);

    private CashReceiptDto BuildReceipt(
        string type,
        string title,
        JournalDto journal,
        SavingsAccount account,
        decimal amount,
        decimal ledger,
        decimal available) =>
        new(
            new ReceiptLetterheadDto(Letterhead.Sigle, Letterhead.Line2, Letterhead.Line3, Letterhead.Line4),
            type,
            title,
            journal.JournalNo,
            journal.JournalNo,
            account.Member?.MemberNo ?? string.Empty,
            account.Member?.FullName ?? string.Empty,
            account.AccountNo,
            account.Product?.Name ?? string.Empty,
            amount,
            account.CurrencyCode,
            ledger,
            available,
            _currentUser.Username ?? string.Empty,
            account.Member?.Branch?.Name ?? Letterhead.DefaultBranchName,
            journal.PostedAtUtc,
            journal.PostedAtPortAuPrince);

    private Result<bool> RequireUser()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.TenantId is null || _currentUser.UserId is null || _currentUser.BranchId is null)
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

    private static string? NormalizeCurrency(string? code) =>
        Currencies.IsSupported(code ?? string.Empty) ? code!.Trim().ToUpperInvariant() : null;

    private static Guid CashGl(string currency) =>
        string.Equals(currency, Currencies.Usd, StringComparison.OrdinalIgnoreCase)
            ? SeedGuids.Gl("1020")
            : SeedGuids.Gl("1010");

    private static TillSessionDto MapTill(TillSession till) =>
        new(
            till.Id,
            till.BranchId,
            till.UserId,
            till.CurrencyCode,
            till.Status.ToString(),
            till.OpeningFloat,
            till.ExpectedCash,
            till.CountedCash,
            till.OverShortAmount,
            till.OverShortJournalId,
            till.OpenedAtUtc,
            till.ClosedAtUtc);
}
