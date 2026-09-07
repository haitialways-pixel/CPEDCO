using CPCREDO.Application.Accounting;
using CPCREDO.Application.Common;
using CPCREDO.Application.Reports;
using CPCREDO.Domain.Accounting;
using CPCREDO.Domain.Common;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CPCREDO.Infrastructure.Reports;

public sealed class ReportService : IReportService
{
    private static readonly string[] LiquidCodes = ["1010", "1020", "1030", "1031", "1110", "1111", "1120"];
    private static readonly string[] DepositCodes = ["2010", "2020", "2110", "2120"];

    private readonly CpcredoDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IJournalService _journals;

    public ReportService(
        CpcredoDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        IJournalService journals)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _journals = journals;
    }

    public async Task<Result<TellerCashProofDto>> GetTellerCashProofAsync(
        DateOnly? date,
        string? currencyCode,
        CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<TellerCashProofDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var day = date ?? _clock.TodayInPortAuPrince();
        var currency = NormalizeCurrency(currencyCode);
        if (currency is null)
            return Result<TellerCashProofDto>.Fail("journal.currency", "Devise non supportée. Utilisez HTG ou USD.");

        var sessions = await _db.TillSessions
            .AsNoTracking()
            .Include(t => t.User)
            .Include(t => t.Branch)
            .Include(t => t.CountLines)
            .Where(t => t.TenantId == _currentUser.TenantId && t.CurrencyCode == currency)
            .ToListAsync(cancellationToken);

        var ofDay = sessions
            .Where(t => OnDay(t.OpenedAtUtc, day) || (t.ClosedAtUtc is { } closed && OnDay(closed, day)))
            .OrderBy(t => t.OpenedAtUtc)
            .ToList();

        var tillIds = ofDay.Select(t => t.Id).ToList();
        var movements = tillIds.Count == 0
            ? []
            : await _db.SavingsLedgerEntries.AsNoTracking()
                .Where(e => e.TenantId == _currentUser.TenantId && e.TillSessionId != null && tillIds.Contains(e.TillSessionId.Value))
                .ToListAsync(cancellationToken);

        var rows = ofDay.Select(t =>
        {
            var related = movements.Where(m => m.TillSessionId == t.Id).ToList();
            return new TellerCashProofSessionDto(
                t.Id,
                t.User?.FullName ?? string.Empty,
                t.Branch?.Name ?? string.Empty,
                t.Status.ToString(),
                t.OpenedAtUtc,
                t.ClosedAtUtc,
                t.OpeningFloat,
                t.ExpectedCash,
                t.CountedCash,
                t.OverShortAmount,
                t.CountLines
                    .OrderByDescending(c => c.FaceValue)
                    .Select(c => new TellerCashProofCountDto(c.FaceValue, c.Quantity, c.Subtotal))
                    .ToList(),
                MoneyAmount.Normalize(related.Where(m => m.EntryType == "Credit").Sum(m => m.Amount)),
                MoneyAmount.Normalize(related.Where(m => m.EntryType == "Debit").Sum(m => m.Amount)));
        }).ToList();

        return Result<TellerCashProofDto>.Ok(new TellerCashProofDto(day, currency, rows));
    }

    public Task<Result<TrialBalanceDto>> GetTrialBalanceAsync(
        DateOnly? asOf,
        string? currencyCode,
        CancellationToken cancellationToken = default) =>
        _journals.GetTrialBalanceAsync(asOf, currencyCode, cancellationToken);

    public async Task<Result<FinancialStatementDto>> GetFinancialsAsync(
        DateOnly? from,
        DateOnly? asOf,
        string? currencyCode,
        CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<FinancialStatementDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var end = asOf ?? _clock.TodayInPortAuPrince();
        var start = from ?? new DateOnly(end.Year, 1, 1);
        var currency = NormalizeCurrency(currencyCode);
        if (currency is null)
            return Result<FinancialStatementDto>.Fail("journal.currency", "Devise non supportée. Utilisez HTG ou USD.");

        var asOfBalances = await LoadBalancesAsync(end, currency, cancellationToken);
        var period = await LoadBalancesAsync(end, currency, cancellationToken, start);

        var assets = Lines(asOfBalances, GlAccountType.Asset);
        var liabilities = Lines(asOfBalances, GlAccountType.Liability);
        var equityPosted = Lines(asOfBalances, GlAccountType.Equity);
        var income = Lines(period, GlAccountType.Income);
        var expenses = Lines(period, GlAccountType.Expense);

        var totalAssets = Sum(assets);
        var totalLiabilities = Sum(liabilities);
        var totalIncome = Sum(income);
        var totalExpenses = Sum(expenses);
        var netIncome = MoneyAmount.Normalize(totalIncome - totalExpenses);
        var equityLines = equityPosted
            .Concat([new ReportLineDto("", "Résultat net de la période", netIncome)])
            .ToList();
        var totalEquity = MoneyAmount.Normalize(Sum(equityPosted) + netIncome);

        return Result<FinancialStatementDto>.Ok(new FinancialStatementDto(
            start,
            end,
            currency,
            assets,
            totalAssets,
            liabilities,
            totalLiabilities,
            equityLines,
            netIncome,
            totalEquity,
            MoneyAmount.Normalize(totalLiabilities + totalEquity),
            income,
            totalIncome,
            expenses,
            totalExpenses));
    }

    public async Task<Result<DepositListingDto>> GetDepositsAsync(
        DateOnly? from,
        DateOnly? to,
        string? currencyCode,
        CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<DepositListingDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var end = to ?? _clock.TodayInPortAuPrince();
        var start = from ?? end;
        var currency = NormalizeCurrency(currencyCode);
        if (currency is null)
            return Result<DepositListingDto>.Fail("journal.currency", "Devise non supportée. Utilisez HTG ou USD.");

        var startUtc = DateTime.SpecifyKind(start.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(end.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);

        var entries = await (
            from e in _db.SavingsLedgerEntries.AsNoTracking()
            join a in _db.SavingsAccounts.AsNoTracking() on e.SavingsAccountId equals a.Id
            join m in _db.Members.AsNoTracking() on a.MemberId equals m.Id
            join p in _db.SavingsProducts.AsNoTracking() on a.ProductId equals p.Id
            where e.TenantId == _currentUser.TenantId
                  && e.EntryType == "Credit"
                  && e.CurrencyCode == currency
                  && e.ValueDateUtc >= startUtc
                  && e.ValueDateUtc <= endUtc
            orderby e.PostedAtUtc
            select new
            {
                e,
                a.AccountNo,
                ProductName = p.Name,
                m.MemberNo,
                m.FirstName,
                m.LastName
            }).ToListAsync(cancellationToken);

        var tillIds = entries.Select(x => x.e.TillSessionId).Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var cashiers = tillIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.TillSessions.AsNoTracking()
                .Include(t => t.User)
                .Where(t => tillIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.User != null ? t.User.FullName : string.Empty, cancellationToken);

        var rows = entries.Select(x => new DepositListingRowDto(
            x.e.PostedAtUtc,
            _clock.ToPortAuPrince(x.e.PostedAtUtc),
            x.MemberNo,
            $"{x.FirstName} {x.LastName}".Trim(),
            x.AccountNo,
            x.ProductName,
            x.e.Amount,
            x.e.CurrencyCode,
            x.e.Description,
            x.e.TillSessionId is { } tillId && cashiers.TryGetValue(tillId, out var name) ? name : null)).ToList();

        return Result<DepositListingDto>.Ok(new DepositListingDto(
            start,
            end,
            currency,
            rows,
            MoneyAmount.Normalize(rows.Sum(r => r.Amount))));
    }

    public async Task<Result<LiquidityRatioDto>> GetLiquidityAsync(
        DateOnly? asOf,
        string? currencyCode,
        CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<LiquidityRatioDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var date = asOf ?? _clock.TodayInPortAuPrince();
        var currency = NormalizeCurrency(currencyCode);
        if (currency is null)
            return Result<LiquidityRatioDto>.Fail("journal.currency", "Devise non supportée. Utilisez HTG ou USD.");

        var balances = await LoadBalancesAsync(date, currency, cancellationToken);
        var liquid = balances
            .Where(b => LiquidCodes.Contains(b.Code))
            .Select(b => new ReportLineDto(b.Code, b.Name, Signed(b.Type, b.Debit, b.Credit)))
            .Where(l => l.Amount != 0m)
            .OrderBy(l => l.Code)
            .ToList();
        var deposits = balances
            .Where(b => DepositCodes.Contains(b.Code))
            .Select(b => new ReportLineDto(b.Code, b.Name, Signed(b.Type, b.Debit, b.Credit)))
            .Where(l => l.Amount != 0m)
            .OrderBy(l => l.Code)
            .ToList();

        var totalLiquid = Sum(liquid);
        var totalDeposits = Sum(deposits);
        decimal? ratio = totalDeposits == 0m ? null : MoneyAmount.Normalize(totalLiquid / totalDeposits);

        return Result<LiquidityRatioDto>.Ok(new LiquidityRatioDto(
            date,
            currency,
            liquid,
            totalLiquid,
            deposits,
            totalDeposits,
            ratio));
    }

    public async Task<Result<ReportFileDto>> ExportTellerCashProofAsync(
        DateOnly? date, string? currencyCode, string format, CancellationToken cancellationToken = default)
    {
        var data = await GetTellerCashProofAsync(date, currencyCode, cancellationToken);
        if (!data.IsSuccess)
            return Result<ReportFileDto>.Fail(data.ErrorCode!, data.ErrorMessage!);
        var report = data.Value!;
        var headers = new[] { "Caissier", "Agence", "Statut", "Fond", "Attendu", "Compté", "Écart", "Dépôts", "Retraits" };
        var rows = report.Sessions.Select(s => (IReadOnlyList<string>)
        [
            s.CashierName,
            s.BranchName,
            s.Status,
            Money(s.OpeningFloat, report.CurrencyCode),
            Money(s.ExpectedCash, report.CurrencyCode),
            s.CountedCash is { } counted ? Money(counted, report.CurrencyCode) : "",
            s.OverShortAmount is { } over ? Money(over, report.CurrencyCode) : "",
            Money(s.Deposits, report.CurrencyCode),
            Money(s.Withdrawals, report.CurrencyCode)
        ]).ToList();
        return FileResult(
            format,
            "Preuve de caisse journalière",
            $"Date {report.Date:yyyy-MM-dd} · {report.CurrencyCode}",
            headers,
            rows,
            $"preuve-caisse-{report.Date:yyyy-MM-dd}");
    }

    public async Task<Result<ReportFileDto>> ExportTrialBalanceAsync(
        DateOnly? asOf, string? currencyCode, string format, CancellationToken cancellationToken = default)
    {
        var data = await GetTrialBalanceAsync(asOf, currencyCode, cancellationToken);
        if (!data.IsSuccess)
            return Result<ReportFileDto>.Fail(data.ErrorCode!, data.ErrorMessage!);
        var report = data.Value!;
        var headers = new[] { "Compte", "Libellé", "Type", "Débit", "Crédit" };
        var rows = report.Rows.Select(r => (IReadOnlyList<string>)
        [
            r.AccountCode,
            r.AccountName,
            r.AccountType,
            Money(r.Debit, report.CurrencyCode),
            Money(r.Credit, report.CurrencyCode)
        ]).Concat([(IReadOnlyList<string>)["", "Totaux", "", Money(report.TotalDebit, report.CurrencyCode), Money(report.TotalCredit, report.CurrencyCode)]]).ToList();
        return FileResult(
            format,
            "Balance de vérification",
            $"Au {report.AsOf:yyyy-MM-dd} · {report.CurrencyCode}",
            headers,
            rows,
            $"balance-verification-{report.AsOf:yyyy-MM-dd}");
    }

    public async Task<Result<ReportFileDto>> ExportFinancialsAsync(
        DateOnly? from, DateOnly? asOf, string? currencyCode, string format, CancellationToken cancellationToken = default)
    {
        var data = await GetFinancialsAsync(from, asOf, currencyCode, cancellationToken);
        if (!data.IsSuccess)
            return Result<ReportFileDto>.Fail(data.ErrorCode!, data.ErrorMessage!);
        var r = data.Value!;
        var headers = new[] { "Section", "Compte", "Libellé", "Montant" };
        var rows = new List<IReadOnlyList<string>>();
        void Add(string section, IReadOnlyList<ReportLineDto> lines, string totalLabel, decimal total)
        {
            foreach (var line in lines)
                rows.Add([section, line.Code, line.Label, Money(line.Amount, r.CurrencyCode)]);
            rows.Add([section, "", totalLabel, Money(total, r.CurrencyCode)]);
        }

        Add("Actif", r.Assets, "Total actif", r.TotalAssets);
        Add("Passif", r.Liabilities, "Total passif", r.TotalLiabilities);
        Add("Capitaux propres", r.Equity, "Total capitaux propres", r.TotalEquity);
        rows.Add(["Capitaux propres", "", "Passif + capitaux propres", Money(r.TotalLiabilitiesAndEquity, r.CurrencyCode)]);
        Add("Produits", r.Income, "Total produits", r.TotalIncome);
        Add("Charges", r.Expenses, "Total charges", r.TotalExpenses);
        rows.Add(["Résultat", "", "Résultat net", Money(r.NetIncome, r.CurrencyCode)]);

        return FileResult(
            format,
            "Bilan et compte de résultat",
            $"Du {r.From:yyyy-MM-dd} au {r.AsOf:yyyy-MM-dd} · {r.CurrencyCode}",
            headers,
            rows,
            $"etats-financiers-{r.AsOf:yyyy-MM-dd}");
    }

    public async Task<Result<ReportFileDto>> ExportDepositsAsync(
        DateOnly? from, DateOnly? to, string? currencyCode, string format, CancellationToken cancellationToken = default)
    {
        var data = await GetDepositsAsync(from, to, currencyCode, cancellationToken);
        if (!data.IsSuccess)
            return Result<ReportFileDto>.Fail(data.ErrorCode!, data.ErrorMessage!);
        var r = data.Value!;
        var headers = new[] { "Date", "Membre", "Nom", "Compte", "Produit", "Montant", "Caissier", "Libellé" };
        var rows = r.Rows.Select(x => (IReadOnlyList<string>)
        [
            x.PostedAtPortAuPrince.ToString("yyyy-MM-dd HH:mm"),
            x.MemberNo,
            x.MemberName,
            x.AccountNo,
            x.ProductName,
            Money(x.Amount, r.CurrencyCode),
            x.CashierName ?? "",
            x.Description
        ]).Concat([(IReadOnlyList<string>)["", "", "", "", "Total", Money(r.Total, r.CurrencyCode), "", ""]]).ToList();
        return FileResult(
            format,
            "Listing des dépôts",
            $"Du {r.From:yyyy-MM-dd} au {r.To:yyyy-MM-dd} · {r.CurrencyCode}",
            headers,
            rows,
            $"depots-{r.From:yyyy-MM-dd}-{r.To:yyyy-MM-dd}");
    }

    public async Task<Result<ReportFileDto>> ExportLiquidityAsync(
        DateOnly? asOf, string? currencyCode, string format, CancellationToken cancellationToken = default)
    {
        var data = await GetLiquidityAsync(asOf, currencyCode, cancellationToken);
        if (!data.IsSuccess)
            return Result<ReportFileDto>.Fail(data.ErrorCode!, data.ErrorMessage!);
        var r = data.Value!;
        var headers = new[] { "Poste", "Compte", "Libellé", "Montant" };
        var rows = new List<IReadOnlyList<string>>();
        foreach (var line in r.LiquidAssets)
            rows.Add(["Actifs liquides", line.Code, line.Label, Money(line.Amount, r.CurrencyCode)]);
        rows.Add(["Actifs liquides", "", "Total", Money(r.TotalLiquidAssets, r.CurrencyCode)]);
        foreach (var line in r.MemberDeposits)
            rows.Add(["Dépôts des membres", line.Code, line.Label, Money(line.Amount, r.CurrencyCode)]);
        rows.Add(["Dépôts des membres", "", "Total", Money(r.TotalMemberDeposits, r.CurrencyCode)]);
        rows.Add(["Ratio", "", "Actifs liquides / dépôts membres", r.Ratio is { } ratio ? ratio.ToString("0.00") : "n/d"]);
        return FileResult(
            format,
            "Ratio de liquidité",
            $"Au {r.AsOf:yyyy-MM-dd} · {r.CurrencyCode} · liquidités / dépôts membres",
            headers,
            rows,
            $"liquidite-{r.AsOf:yyyy-MM-dd}");
    }

    private async Task<List<BalanceRow>> LoadBalancesAsync(
        DateOnly asOf,
        string currency,
        CancellationToken cancellationToken,
        DateOnly? from = null)
    {
        var query =
            from line in _db.JournalLines
            join journal in _db.JournalEntries on line.JournalEntryId equals journal.Id
            join account in _db.GlAccounts on line.GlAccountId equals account.Id
            where journal.TenantId == _currentUser.TenantId
                  && journal.ValueDate <= asOf
                  && journal.Status == JournalStatus.Posted
                  && account.CurrencyCode == currency
                  && account.IsPostable
            select new { line, journal, account };

        if (from is { } start)
            query = query.Where(x => x.journal.ValueDate >= start);

        var grouped = await query
            .GroupBy(x => new { x.account.Code, x.account.NameFr, x.account.AccountType })
            .Select(g => new
            {
                g.Key.Code,
                g.Key.NameFr,
                g.Key.AccountType,
                Debit = g.Sum(x => x.line.Debit),
                Credit = g.Sum(x => x.line.Credit)
            })
            .ToListAsync(cancellationToken);

        return grouped
            .Select(g => new BalanceRow(g.Code, g.NameFr, g.AccountType, g.Debit, g.Credit))
            .ToList();
    }

    private Result<bool> RequireUser()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.TenantId is null || _currentUser.UserId is null)
            return Result<bool>.Fail("auth.unauthorized", "Session invalide.");
        return Result<bool>.Ok(true);
    }

    private bool OnDay(DateTime utc, DateOnly day) =>
        DateOnly.FromDateTime(_clock.ToPortAuPrince(utc)) == day;

    private static string? NormalizeCurrency(string? currencyCode)
    {
        var currency = string.IsNullOrWhiteSpace(currencyCode) ? Currencies.Htg : currencyCode.Trim().ToUpperInvariant();
        return Currencies.IsSupported(currency) ? currency : null;
    }

    private static decimal Signed(GlAccountType type, decimal debit, decimal credit) =>
        type is GlAccountType.Asset or GlAccountType.Expense
            ? MoneyAmount.Normalize(debit - credit)
            : MoneyAmount.Normalize(credit - debit);

    private static List<ReportLineDto> Lines(IEnumerable<BalanceRow> rows, GlAccountType type) =>
        rows.Where(r => r.Type == type)
            .Select(r => new ReportLineDto(r.Code, r.Name, Signed(r.Type, r.Debit, r.Credit)))
            .Where(l => l.Amount != 0m)
            .OrderBy(l => l.Code)
            .ToList();

    private static decimal Sum(IEnumerable<ReportLineDto> lines) =>
        MoneyAmount.Normalize(lines.Sum(l => l.Amount));

    private static string Money(decimal amount, string currency) =>
        MoneyDisplay.Format(amount, currency);

    private static Result<ReportFileDto> FileResult(
        string format,
        string title,
        string subtitle,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows,
        string fileStub)
    {
        var kind = (format ?? "pdf").Trim().ToLowerInvariant();
        if (kind is "csv" or "text/csv")
        {
            return Result<ReportFileDto>.Ok(new ReportFileDto(
                ReportCsv.Render(headers, rows),
                "text/csv; charset=utf-8",
                $"{fileStub}.csv"));
        }

        if (kind is not "pdf" and not "application/pdf")
            return Result<ReportFileDto>.Fail("report.format", "Format non supporté. Utilisez pdf ou csv.");

        return Result<ReportFileDto>.Ok(new ReportFileDto(
            ReportPdf.Render(title, subtitle, headers, rows),
            "application/pdf",
            $"{fileStub}.pdf"));
    }

    private sealed record BalanceRow(string Code, string Name, GlAccountType Type, decimal Debit, decimal Credit);
}
