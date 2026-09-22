using CPCREDO.Application.Accounting;
using CPCREDO.Application.Common;
using CPCREDO.Application.Loans;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Loans;
using CPCREDO.Domain.Teller;
using CPCREDO.Domain.Treasury;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CPCREDO.Infrastructure.Loans;

public sealed class CreditPoolService : ICreditPoolService
{
    private readonly CpcredoDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IJournalService _journals;
    private readonly IAuditLogger _audit;

    public CreditPoolService(
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

    public async Task<Result<CreditPoolDto>> GetAsync(string? currencyCode, CancellationToken cancellationToken = default)
    {
        var auth = RequireManagerOrOfficer();
        if (!auth.IsSuccess)
            return Result<CreditPoolDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);
        var currency = Normalize(currencyCode);
        var pool = await EnsurePoolAsync(currency, cancellationToken);
        return Result<CreditPoolDto>.Ok(await MapAsync(pool, cancellationToken));
    }

    public async Task<Result<CreditPoolDto>> FundAsync(
        FundCreditPoolRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var gate = RequireManager();
        if (!gate.IsSuccess)
            return Result<CreditPoolDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);

        if (_currentUser.Roles.Contains(RoleNames.Caissier) && !IsManager())
            return Result<CreditPoolDto>.Fail("auth.forbidden", "Le caissier ne peut pas alimenter le fonds de crédit.");

        var amount = MoneyAmount.Normalize(request.Amount);
        if (amount <= 0m)
            return Result<CreditPoolDto>.Fail("pool.amount", "Le montant doit être supérieur à zéro.");

        var currency = Normalize(request.CurrencyCode);
        var source = (request.SourceKind ?? "Vault").Trim();
        var isVault = source.Equals("Vault", StringComparison.OrdinalIgnoreCase);
        var isBank = source.Equals("Bank", StringComparison.OrdinalIgnoreCase)
                     || source.Equals("Correspondent", StringComparison.OrdinalIgnoreCase);
        if (!isVault && !isBank)
            return Result<CreditPoolDto>.Fail("pool.source", "Alimentez le fonds depuis le coffre ou une banque correspondante.");

        var poolGl = await RequireGlAsync(LoanGl.CreditPoolHtg, currency, cancellationToken);
        if (poolGl is null)
            return Result<CreditPoolDto>.Fail("pool.gl", "Compte 1040 (fonds de crédit) introuvable.");

        Guid sourceGlId;
        string sourceLabel;
        if (isVault)
        {
            var vault = await RequireGlAsync(TreasuryGl.Vault(currency), currency, cancellationToken);
            if (vault is null)
                return Result<CreditPoolDto>.Fail("internal.gl", "Compte de coffre introuvable.");
            var vaultBal = await GlBalanceAsync(vault.Id, cancellationToken);
            if (vaultBal < amount)
                return Result<CreditPoolDto>.Fail("internal.insufficient_vault", "Solde du coffre insuffisant.");
            sourceGlId = vault.Id;
            sourceLabel = "Coffre";
        }
        else
        {
            if (request.BankAccountId is null)
                return Result<CreditPoolDto>.Fail("pool.bank", "Choisissez la banque correspondante.");
            var bank = await _db.BankAccounts.AsNoTracking()
                .FirstOrDefaultAsync(
                    b => b.Id == request.BankAccountId && b.TenantId == _currentUser.TenantId && b.IsActive,
                    cancellationToken);
            if (bank is null)
                return Result<CreditPoolDto>.Fail("treasury.bank.not_found", "Compte correspondant introuvable.");
            var bankGl = await _db.GlAccounts.AsNoTracking()
                .FirstOrDefaultAsync(
                    a => a.TenantId == _currentUser.TenantId && a.Code == bank.GlCode && a.CurrencyCode == currency && a.IsPostable,
                    cancellationToken);
            if (bankGl is null)
                return Result<CreditPoolDto>.Fail("internal.gl", "Compte GL banque introuvable.");
            var bankBal = await GlBalanceAsync(bankGl.Id, cancellationToken);
            if (bankBal < amount)
                return Result<CreditPoolDto>.Fail("internal.insufficient", "Solde de la banque correspondante insuffisant.");
            sourceGlId = bankGl.Id;
            sourceLabel = bank.Name;
        }

        var journal = await _journals.PostAsync(new CreateJournalRequest
        {
            Description = "Alimentation fonds de crédit",
            CurrencyCode = currency,
            BranchId = _currentUser.BranchId,
            Lines =
            [
                new CreateJournalLineRequest { GlAccountId = poolGl.Id, Debit = amount, Credit = 0m, Description = "Fonds de crédit" },
                new CreateJournalLineRequest { GlAccountId = sourceGlId, Debit = 0m, Credit = amount, Description = sourceLabel }
            ]
        }, idempotencyKey, cancellationToken);
        if (!journal.IsSuccess)
            return Result<CreditPoolDto>.Fail(journal.ErrorCode!, journal.ErrorMessage!);

        var pool = await EnsurePoolAsync(currency, cancellationToken);
        pool.FundedTotal = MoneyAmount.Normalize(pool.FundedTotal + amount);
        pool.UpdatedAtUtc = _clock.UtcNow;
        pool.Movements.Add(new CreditPoolMovement
        {
            TenantId = pool.TenantId,
            PoolId = pool.Id,
            Kind = "Funding",
            SourceKind = isVault ? "Vault" : "Bank",
            BankAccountId = isBank ? request.BankAccountId : null,
            Amount = amount,
            CurrencyCode = currency,
            Note = string.IsNullOrWhiteSpace(request.Note) ? "CreditPoolFunding" : request.Note.Trim(),
            JournalEntryId = journal.Value!.Id,
            CreatedByUserId = _currentUser.UserId!.Value,
            CreatedAtUtc = _clock.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(
            "CreditPool.Funded",
            nameof(CreditPool),
            pool.Id,
            new { amount, source, journal.Value.Id },
            pool.TenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);
        return Result<CreditPoolDto>.Ok(await MapAsync(pool, cancellationToken));
    }

    public async Task<Result<bool>> ConsumeDisbursementAsync(
        Guid loanId,
        decimal principal,
        string currencyCode,
        CancellationToken cancellationToken = default)
    {
        var currency = Normalize(currencyCode);
        var pool = await _db.CreditPools
            .Include(p => p.Movements)
            .FirstOrDefaultAsync(
                p => p.TenantId == _currentUser.TenantId && p.CurrencyCode == currency,
                cancellationToken);
        if (pool is null || pool.FundedTotal <= 0m)
            return Result<bool>.Ok(true);

        var reserved = await ReservedApprovalsAsync(currency, cancellationToken);
        var thisLoan = await _db.Loans.AsNoTracking()
            .Where(l => l.Id == loanId && l.Status == LoanStatus.Approved)
            .Select(l => l.Principal)
            .FirstOrDefaultAsync(cancellationToken);
        var reservedOthers = MoneyAmount.Normalize(reserved - thisLoan);
        if (reservedOthers < 0m)
            reservedOthers = 0m;
        var available = MoneyAmount.Normalize(pool.FundedTotal - pool.DisbursedTotal - reservedOthers);
        if (available < principal)
            return Result<bool>.Fail(
                "pool.insufficient",
                $"Fonds de crédit insuffisant (disponible {MoneyDisplay.Format(available, currency)}).");

        pool.DisbursedTotal = MoneyAmount.Normalize(pool.DisbursedTotal + principal);
        pool.UpdatedAtUtc = _clock.UtcNow;
        pool.Movements.Add(new CreditPoolMovement
        {
            TenantId = pool.TenantId,
            PoolId = pool.Id,
            Kind = "Disbursement",
            SourceKind = "Pool",
            LoanId = loanId,
            Amount = principal,
            CurrencyCode = currency,
            Note = "Décaissement prêt",
            CreatedByUserId = _currentUser.UserId!.Value,
            CreatedAtUtc = _clock.UtcNow
        });
        return Result<bool>.Ok(true);
    }

    private async Task<CreditPool> EnsurePoolAsync(string currency, CancellationToken cancellationToken)
    {
        var pool = await _db.CreditPools
            .Include(p => p.Movements)
            .FirstOrDefaultAsync(
                p => p.TenantId == _currentUser.TenantId && p.CurrencyCode == currency,
                cancellationToken);
        if (pool is not null)
            return pool;
        pool = new CreditPool
        {
            TenantId = _currentUser.TenantId!.Value,
            CurrencyCode = currency,
            UpdatedAtUtc = _clock.UtcNow
        };
        _db.CreditPools.Add(pool);
        await _db.SaveChangesAsync(cancellationToken);
        return pool;
    }

    private async Task<CreditPoolDto> MapAsync(CreditPool pool, CancellationToken cancellationToken)
    {
        var reserved = await ReservedApprovalsAsync(pool.CurrencyCode, cancellationToken);
        var available = MoneyAmount.Normalize(pool.FundedTotal - pool.DisbursedTotal - reserved);
        if (available < 0m)
            available = 0m;
        var movements = pool.Movements
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(50)
            .Select(m => new CreditPoolMovementDto(
                m.Id, m.Kind, m.SourceKind, m.Amount, m.CurrencyCode, m.Note, m.LoanId, m.CreatedAtUtc))
            .ToList();
        return new CreditPoolDto(
            pool.Id,
            pool.CurrencyCode,
            pool.FundedTotal,
            pool.DisbursedTotal,
            reserved,
            available,
            movements);
    }

    private async Task<decimal> ReservedApprovalsAsync(string currency, CancellationToken cancellationToken)
    {
        var sum = await _db.Loans.AsNoTracking()
            .Where(l => l.TenantId == _currentUser.TenantId
                        && l.CurrencyCode == currency
                        && l.Status == LoanStatus.Approved)
            .SumAsync(l => (decimal?)l.Principal, cancellationToken);
        return MoneyAmount.Normalize(sum ?? 0m);
    }

    private bool IsManager() =>
        _currentUser.Roles.Any(r => r is RoleNames.Admin or RoleNames.Gerant);

    private Result<bool> RequireManager()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.TenantId is null || _currentUser.UserId is null)
            return Result<bool>.Fail("auth.unauthorized", "Session invalide.");
        if (!IsManager())
            return Result<bool>.Fail("auth.forbidden", "Seul le gérant peut alimenter le fonds de crédit.");
        return Result<bool>.Ok(true);
    }

    private Result<bool> RequireManagerOrOfficer()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.TenantId is null || _currentUser.UserId is null)
            return Result<bool>.Fail("auth.unauthorized", "Session invalide.");
        if (!_currentUser.Roles.Any(r => r is RoleNames.Admin or RoleNames.Gerant or RoleNames.OfficierCredit or RoleNames.Commissaire))
            return Result<bool>.Fail("auth.forbidden", "Accès au fonds de crédit refusé.");
        return Result<bool>.Ok(true);
    }

    private static string Normalize(string? code) =>
        Currencies.IsSupported(code ?? string.Empty) ? code!.Trim().ToUpperInvariant() : Currencies.Htg;

    private Task<Domain.Accounting.GlAccount?> RequireGlAsync(string code, string currency, CancellationToken cancellationToken) =>
        _db.GlAccounts.FirstOrDefaultAsync(
            a => a.TenantId == _currentUser.TenantId && a.Code == code && a.CurrencyCode == currency && a.IsPostable,
            cancellationToken);

    private async Task<decimal> GlBalanceAsync(Guid glAccountId, CancellationToken cancellationToken)
    {
        var nets = await _db.JournalLines.AsNoTracking()
            .Where(l => l.GlAccountId == glAccountId)
            .Select(l => l.Debit - l.Credit)
            .ToListAsync(cancellationToken);
        return MoneyAmount.Normalize(nets.Sum());
    }
}
