using CPCREDO.Application.Accounting;
using CPCREDO.Application.Common;
using CPCREDO.Domain.Accounting;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CPCREDO.Infrastructure.Accounting;

public sealed class JournalService : IJournalService
{
    private readonly CpcredoDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditLogger _audit;

    public JournalService(
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

    public async Task<Result<JournalDto>> PostAsync(
        CreateJournalRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<JournalDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        if (!_currentUser.Roles.Any(r => RoleNames.WriteRoles.Contains(r)))
            return Result<JournalDto>.Fail("auth.forbidden", "Vous n’avez pas le droit de saisir une écriture.");

        var tenantId = _currentUser.TenantId!.Value;
        var userId = _currentUser.UserId!.Value;
        var branchId = request.BranchId ?? _currentUser.BranchId!.Value;

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var replay = await LoadByIdempotencyAsync(tenantId, idempotencyKey, cancellationToken);
            if (replay is not null)
                return Result<JournalDto>.Ok(Map(replay));
        }

        var currency = string.IsNullOrWhiteSpace(request.CurrencyCode)
            ? Currencies.Htg
            : request.CurrencyCode.Trim().ToUpperInvariant();

        if (!Currencies.IsSupported(currency))
            return Result<JournalDto>.Fail("journal.currency", "Devise non supportée. Utilisez HTG ou USD.");

        if (request.Lines is null || request.Lines.Count == 0)
            return Result<JournalDto>.Fail("journal.empty", "Une écriture doit contenir au moins une ligne.");

        var branchOk = await _db.Branches.AnyAsync(b => b.Id == branchId && b.TenantId == tenantId && b.IsActive, cancellationToken);
        if (!branchOk)
            return Result<JournalDto>.Fail("journal.branch", "Agence introuvable.");

        var accountIds = request.Lines.Select(l => l.GlAccountId).Distinct().ToList();
        var accounts = await _db.GlAccounts
            .Where(a => a.TenantId == tenantId && accountIds.Contains(a.Id))
            .ToListAsync(cancellationToken);

        foreach (var line in request.Lines)
        {
            var account = accounts.FirstOrDefault(a => a.Id == line.GlAccountId);
            if (account is null)
                return Result<JournalDto>.Fail("journal.account", "Compte général introuvable.");
            if (!account.IsActive || !account.IsPostable)
                return Result<JournalDto>.Fail("journal.account_not_postable", $"Le compte {account.Code} n’accepte pas d’écritures.");
            if (!string.Equals(account.CurrencyCode, currency, StringComparison.OrdinalIgnoreCase))
                return Result<JournalDto>.Fail("journal.currency_mismatch", $"Le compte {account.Code} est en {account.CurrencyCode}, pas en {currency}.");
        }

        var now = _clock.UtcNow;
        var journal = new JournalEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            JournalNo = await NextJournalNoAsync(tenantId, cancellationToken),
            ValueDate = request.ValueDate ?? _clock.TodayInPortAuPrince(),
            PostedAtUtc = now,
            PostedByUserId = userId,
            Description = string.IsNullOrWhiteSpace(request.Description) ? "Écriture" : request.Description.Trim(),
            CurrencyCode = currency,
            Status = JournalStatus.Posted,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim(),
            CreatedAtUtc = now
        };

        var lineNo = 1;
        foreach (var line in request.Lines)
        {
            journal.Lines.Add(new JournalLine
            {
                Id = Guid.NewGuid(),
                JournalEntryId = journal.Id,
                LineNo = lineNo++,
                GlAccountId = line.GlAccountId,
                Debit = line.Debit,
                Credit = line.Credit,
                Description = string.IsNullOrWhiteSpace(line.Description) ? journal.Description : line.Description.Trim()
            });
        }

        try
        {
            journal.EnsureBalanced();
        }
        catch (DomainException ex)
        {
            return Result<JournalDto>.Fail(ex.Code, ex.Message);
        }

        _db.JournalEntries.Add(journal);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (PostedJournalImmutableException ex)
        {
            return Result<JournalDto>.Fail(ex.Code, ex.Message);
        }

        await _audit.LogAsync(
            "Journal.Posted",
            nameof(JournalEntry),
            journal.Id,
            new { journal.JournalNo, journal.TotalDebit, journal.TotalCredit, journal.CurrencyCode },
            tenantId,
            userId,
            cancellationToken: cancellationToken);

        await LoadAccountCodesAsync(journal, cancellationToken);
        return Result<JournalDto>.Ok(Map(journal));
    }

    public async Task<Result<JournalDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<JournalDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var journal = await _db.JournalEntries
            .Include(j => j.Lines)
            .ThenInclude(l => l.GlAccount)
            .FirstOrDefaultAsync(
                j => j.Id == id && j.TenantId == _currentUser.TenantId,
                cancellationToken);

        if (journal is null)
            return Result<JournalDto>.Fail("journal.not_found", "Écriture introuvable.");

        return Result<JournalDto>.Ok(Map(journal));
    }

    public async Task<Result<JournalDto>> ReverseAsync(
        Guid id,
        ReverseJournalRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<JournalDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        if (!_currentUser.Roles.Any(r => RoleNames.ReverseRoles.Contains(r)))
            return Result<JournalDto>.Fail("auth.forbidden", "Seuls l’administrateur et le gérant peuvent contre-passer une écriture.");

        var tenantId = _currentUser.TenantId!.Value;
        var userId = _currentUser.UserId!.Value;

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var replay = await LoadByIdempotencyAsync(tenantId, idempotencyKey, cancellationToken);
            if (replay is not null)
                return Result<JournalDto>.Ok(Map(replay));
        }

        var original = await _db.JournalEntries
            .Include(j => j.Lines)
            .ThenInclude(l => l.GlAccount)
            .FirstOrDefaultAsync(j => j.Id == id && j.TenantId == tenantId, cancellationToken);

        if (original is null)
            return Result<JournalDto>.Fail("journal.not_found", "Écriture introuvable.");

        var already = await _db.JournalEntries.AnyAsync(
            j => j.TenantId == tenantId && j.ReversalOfJournalId == original.Id,
            cancellationToken);
        if (already)
            return Result<JournalDto>.Fail("journal.already_reversed", "Cette écriture a déjà une contre-passation.");

        var now = _clock.UtcNow;
        JournalEntry reversal;
        try
        {
            reversal = original.CreateReversal(
                Guid.NewGuid(),
                await NextJournalNoAsync(tenantId, cancellationToken),
                userId,
                now,
                request.ValueDate ?? _clock.TodayInPortAuPrince());
        }
        catch (DomainException ex)
        {
            return Result<JournalDto>.Fail(ex.Code, ex.Message);
        }

        if (!string.IsNullOrWhiteSpace(request.Description))
            reversal.Description = request.Description.Trim();

        reversal.IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();

        _db.JournalEntries.Add(reversal);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (PostedJournalImmutableException ex)
        {
            return Result<JournalDto>.Fail(ex.Code, ex.Message);
        }

        await _audit.LogAsync(
            "Journal.Reversed",
            nameof(JournalEntry),
            reversal.Id,
            new { original = original.JournalNo, reversal = reversal.JournalNo },
            tenantId,
            userId,
            cancellationToken: cancellationToken);

        await LoadAccountCodesAsync(reversal, cancellationToken);
        return Result<JournalDto>.Ok(Map(reversal));
    }

    public async Task<Result<TrialBalanceDto>> GetTrialBalanceAsync(
        DateOnly? asOf,
        string? currencyCode,
        CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<TrialBalanceDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var tenantId = _currentUser.TenantId!.Value;
        var date = asOf ?? _clock.TodayInPortAuPrince();
        var currency = string.IsNullOrWhiteSpace(currencyCode)
            ? Currencies.Htg
            : currencyCode.Trim().ToUpperInvariant();

        if (!Currencies.IsSupported(currency))
            return Result<TrialBalanceDto>.Fail("journal.currency", "Devise non supportée. Utilisez HTG ou USD.");

        var grouped = await (
            from line in _db.JournalLines
            join journal in _db.JournalEntries on line.JournalEntryId equals journal.Id
            join account in _db.GlAccounts on line.GlAccountId equals account.Id
            where journal.TenantId == tenantId
                  && journal.ValueDate <= date
                  && journal.Status == JournalStatus.Posted
                  && account.CurrencyCode == currency
            group line by new { account.Id, account.Code, account.NameFr, account.AccountType } into g
            select new
            {
                g.Key.Id,
                g.Key.Code,
                g.Key.NameFr,
                g.Key.AccountType,
                Debit = g.Sum(x => x.Debit),
                Credit = g.Sum(x => x.Credit)
            }).ToListAsync(cancellationToken);

        var rows = grouped
            .Select(g =>
            {
                var debit = MoneyAmount.Normalize(g.Debit);
                var credit = MoneyAmount.Normalize(g.Credit);
                var net = debit - credit;
                return new TrialBalanceRowDto(
                    g.Id,
                    g.Code,
                    g.NameFr,
                    g.AccountType.ToString(),
                    net > 0m ? net : 0m,
                    net < 0m ? MoneyAmount.Normalize(-net) : 0m);
            })
            .Where(r => r.Debit != 0m || r.Credit != 0m)
            .OrderBy(r => r.AccountCode)
            .ToList();

        var totalDebit = MoneyAmount.Normalize(rows.Sum(r => r.Debit));
        var totalCredit = MoneyAmount.Normalize(rows.Sum(r => r.Credit));

        return Result<TrialBalanceDto>.Ok(new TrialBalanceDto(
            date,
            currency,
            rows,
            totalDebit,
            totalCredit,
            MoneyAmount.Normalize(totalDebit - totalCredit)));
    }

    private Result<bool> RequireUser()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.TenantId is null || _currentUser.UserId is null || _currentUser.BranchId is null)
            return Result<bool>.Fail("auth.unauthorized", "Session invalide.");

        return Result<bool>.Ok(true);
    }

    private async Task<string> NextJournalNoAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var numbers = await _db.JournalEntries
            .Where(j => j.TenantId == tenantId && j.JournalNo.StartsWith("J-"))
            .Select(j => j.JournalNo)
            .ToListAsync(cancellationToken);

        var max = 0;
        foreach (var number in numbers)
        {
            var tail = number[2..];
            if (int.TryParse(tail, out var n) && n > max)
                max = n;
        }

        return $"J-{max + 1:000000}";
    }

    private Task<JournalEntry?> LoadByIdempotencyAsync(Guid tenantId, string key, CancellationToken cancellationToken) =>
        _db.JournalEntries
            .Include(j => j.Lines)
            .ThenInclude(l => l.GlAccount)
            .FirstOrDefaultAsync(j => j.TenantId == tenantId && j.IdempotencyKey == key, cancellationToken);

    private async Task LoadAccountCodesAsync(JournalEntry journal, CancellationToken cancellationToken)
    {
        var ids = journal.Lines.Select(l => l.GlAccountId).Distinct().ToList();
        var accounts = await _db.GlAccounts.Where(a => ids.Contains(a.Id)).ToListAsync(cancellationToken);
        foreach (var line in journal.Lines)
            line.GlAccount = accounts.FirstOrDefault(a => a.Id == line.GlAccountId);
    }

    private JournalDto Map(JournalEntry journal) =>
        new(
            journal.Id,
            journal.JournalNo,
            journal.ValueDate,
            journal.PostedAtUtc,
            _clock.ToPortAuPrince(journal.PostedAtUtc),
            journal.PostedByUserId,
            journal.Description,
            journal.CurrencyCode,
            journal.Status.ToString(),
            journal.IsReversal,
            journal.ReversalOfJournalId,
            MoneyAmount.Normalize(journal.TotalDebit),
            MoneyAmount.Normalize(journal.TotalCredit),
            journal.Lines
                .OrderBy(l => l.LineNo)
                .Select(l => new JournalLineDto(
                    l.Id,
                    l.LineNo,
                    l.GlAccountId,
                    l.GlAccount?.Code,
                    MoneyAmount.Normalize(l.Debit),
                    MoneyAmount.Normalize(l.Credit),
                    l.Description))
                .ToList());
}
