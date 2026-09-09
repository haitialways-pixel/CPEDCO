using CPCREDO.Application.Accounting;
using CPCREDO.Application.Common;
using CPCREDO.Application.Reports;
using CPCREDO.Domain.Accounting;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Loans;
using CPCREDO.Domain.Members;
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

        var internals = tillIds.Count == 0
            ? []
            : await _db.InternalCashMovements.AsNoTracking()
                .Include(m => m.SourceTillSession)!.ThenInclude(s => s!.User)
                .Include(m => m.DestinationTillSession)!.ThenInclude(s => s!.User)
                .Where(m => m.TenantId == _currentUser.TenantId && m.CurrencyCode == currency
                            && ((m.SourceTillSessionId != null && tillIds.Contains(m.SourceTillSessionId.Value))
                                || (m.DestinationTillSessionId != null && tillIds.Contains(m.DestinationTillSessionId.Value))))
                .ToListAsync(cancellationToken);

        var rows = ofDay.Select(t =>
        {
            var related = movements.Where(m => m.TillSessionId == t.Id).ToList();
            var relatedInternal = internals
                .Where(m => (m.SourceTillSessionId == t.Id || m.DestinationTillSessionId == t.Id)
                            && (OnDay(m.CreatedAtUtc, day) || (m.AcceptedAtUtc is { } acc && OnDay(acc, day))))
                .Select(m => new TellerCashProofInternalDto(
                    m.Direction.ToString(),
                    m.Status.ToString(),
                    m.Amount,
                    m.SourceTillSessionId == t.Id
                        ? (m.DestinationTillSession?.User?.FullName ?? "Coffre")
                        : (m.SourceTillSession?.User?.FullName ?? "Coffre")))
                .ToList();
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
                MoneyAmount.Normalize(related.Where(m => m.EntryType == "Debit").Sum(m => m.Amount)),
                relatedInternal);
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
        var headers = new[] { "Caissier", "Agence", "Statut", "Fond", "Attendu", "Compté", "Écart", "Dépôts", "Retraits", "Mouvements internes" };
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
            Money(s.Withdrawals, report.CurrencyCode),
            string.Join(" ; ", s.InternalMovements.Select(m =>
                $"{LabelDirection(m.Direction)} {Money(m.Amount, report.CurrencyCode)} ({m.Status})"))
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

    public async Task<Result<ParCt90Dto>> GetParCt90Async(DateOnly? asOf, CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<ParCt90Dto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var day = asOf ?? _clock.TodayInPortAuPrince();
        var loans = await _db.Loans.AsNoTracking()
            .Include(l => l.Product)
            .Include(l => l.Installments)
            .Where(l => l.TenantId == _currentUser.TenantId && l.Status == LoanStatus.Active)
            .ToListAsync(cancellationToken);
        var ct90 = loans.Where(l => LoanEvergreen.IsCt90(l.Product)).ToList();
        var outstanding = ct90.Select(l =>
        {
            var principal = MoneyAmount.Normalize(l.Installments.Sum(LoanRepaymentAllocator.RemainingPrincipal));
            var dpd = LoanDelinquency.DaysPastDue(l.Installments, day);
            return (principal, dpd);
        }).ToList();
        var portfolio = MoneyAmount.Normalize(outstanding.Sum(x => x.principal));
        return Result<ParCt90Dto>.Ok(new ParCt90Dto(
            day,
            Currencies.Htg,
            portfolio,
            ct90.Count,
            Bucket(1, outstanding, portfolio),
            Bucket(7, outstanding, portfolio),
            Bucket(30, outstanding, portfolio)));
    }

    public async Task<Result<ReportFileDto>> ExportParCt90Async(
        DateOnly? asOf, string format, CancellationToken cancellationToken = default)
    {
        var data = await GetParCt90Async(asOf, cancellationToken);
        if (!data.IsSuccess)
            return Result<ReportFileDto>.Fail(data.ErrorCode!, data.ErrorMessage!);
        var r = data.Value!;
        var headers = new[] { "Indicateur", "Jours", "Encours à risque", "Ratio" };
        IReadOnlyList<string> Line(string name, ParBucketDto b) =>
        [
            name,
            b.Days.ToString(),
            Money(b.Outstanding, r.CurrencyCode),
            b.Ratio is { } ratio ? $"{MoneyDisplay.FormatNumber(ratio * 100m)} %" : "n/d"
        ];
        var rows = new List<IReadOnlyList<string>>();
        rows.Add(["Portefeuille CT90", "—", Money(r.PortfolioOutstanding, r.CurrencyCode), $"{r.LoanCount} crédits"]);
        rows.Add(Line("PAR 1", r.Par1));
        rows.Add(Line("PAR 7", r.Par7));
        rows.Add(Line("PAR 30", r.Par30));
        return FileResult(
            format,
            "PAR 1 / 7 / 30 — CT90",
            $"Au {r.AsOf:yyyy-MM-dd} · {r.CurrencyCode}",
            headers,
            rows,
            $"par-ct90-{r.AsOf:yyyy-MM-dd}");
    }

    public async Task<Result<RenewalRegisterDto>> GetRenewalRegisterAsync(
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<RenewalRegisterDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var end = to ?? _clock.TodayInPortAuPrince();
        var start = from ?? new DateOnly(end.Year, 1, 1);
        var startUtc = DateTime.SpecifyKind(start.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc).AddHours(-6);
        var endUtc = DateTime.SpecifyKind(end.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc).AddHours(18);

        var news = await _db.Loans.AsNoTracking()
            .Where(l => l.TenantId == _currentUser.TenantId
                        && l.RenewedFromLoanId != null
                        && l.CreatedAtUtc >= startUtc && l.CreatedAtUtc < endUtc)
            .OrderBy(l => l.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var oldIds = news.Select(l => l.RenewedFromLoanId!.Value).Distinct().ToList();
        var previous = oldIds.Count == 0
            ? new Dictionary<Guid, Loan>()
            : await _db.Loans.AsNoTracking()
                .Where(l => oldIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, cancellationToken);
        var memberIds = news.Select(l => l.MemberId).Distinct().ToList();
        var members = memberIds.Count == 0
            ? new Dictionary<Guid, Member>()
            : await _db.Members.AsNoTracking()
                .Where(m => memberIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, cancellationToken);

        var rows = news.Select(l =>
        {
            previous.TryGetValue(l.RenewedFromLoanId!.Value, out var old);
            members.TryGetValue(l.MemberId, out var member);
            var prevPrincipal = old?.Principal ?? 0m;
            return new RenewalRegisterRowDto(
                l.CreatedAtUtc,
                member?.MemberNo ?? string.Empty,
                member is null ? string.Empty : $"{member.FirstName} {member.LastName}".Trim(),
                old?.LoanNo ?? string.Empty,
                l.LoanNo,
                l.CycleNumber,
                prevPrincipal,
                l.Principal,
                LoanEvergreen.Matches(l.CycleNumber, l.Principal, prevPrincipal),
                l.CurrencyCode);
        }).ToList();

        return Result<RenewalRegisterDto>.Ok(new RenewalRegisterDto(start, end, rows));
    }

    public async Task<Result<ReportFileDto>> ExportRenewalRegisterAsync(
        DateOnly? from, DateOnly? to, string format, CancellationToken cancellationToken = default)
    {
        var data = await GetRenewalRegisterAsync(from, to, cancellationToken);
        if (!data.IsSuccess)
            return Result<ReportFileDto>.Fail(data.ErrorCode!, data.ErrorMessage!);
        var r = data.Value!;
        var headers = new[] { "Date", "Membre", "Nom", "Ancien n°", "Nouveau n°", "Cycle", "Capital précédent", "Nouveau capital", "Evergreen" };
        var rows = r.Rows.Select(x => (IReadOnlyList<string>)
        [
            x.RenewedAtUtc.ToString("yyyy-MM-dd"),
            x.MemberNo,
            x.MemberName,
            x.OldLoanNo,
            x.NewLoanNo,
            x.NewCycle.ToString(),
            Money(x.PreviousPrincipal, x.CurrencyCode),
            Money(x.NewPrincipal, x.CurrencyCode),
            x.IsEvergreen ? "Oui" : "Non"
        ]).ToList();
        return FileResult(
            format,
            "Registre des renouvellements",
            $"Du {r.From:yyyy-MM-dd} au {r.To:yyyy-MM-dd}",
            headers,
            rows,
            $"registre-renouvellements-{r.From:yyyy-MM-dd}-{r.To:yyyy-MM-dd}");
    }

    private static ParBucketDto Bucket(int days, IReadOnlyList<(decimal principal, int dpd)> loans, decimal portfolio)
    {
        var amount = MoneyAmount.Normalize(loans.Where(x => x.dpd >= days).Sum(x => x.principal));
        decimal? ratio = portfolio <= 0m ? null : MoneyAmount.Normalize(amount / portfolio);
        return new ParBucketDto(days, amount, ratio);
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

    private static string LabelDirection(string direction) => direction switch
    {
        "VaultToTill" => "Coffre → Caisse",
        "TillToVault" => "Caisse → Coffre",
        "TillToTill" => "Caisse → Caisse",
        _ => direction
    };

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
