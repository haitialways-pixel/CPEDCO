namespace CPCREDO.Application.Reports;

public sealed record ReportFileDto(byte[] Content, string ContentType, string FileName);

public sealed record ReportLineDto(
    string Code,
    string Label,
    decimal Amount);

public sealed record TellerCashProofSessionDto(
    Guid TillId,
    string CashierName,
    string BranchName,
    string Status,
    DateTime OpenedAtUtc,
    DateTime? ClosedAtUtc,
    decimal OpeningFloat,
    decimal ExpectedCash,
    decimal? CountedCash,
    decimal? OverShortAmount,
    IReadOnlyList<TellerCashProofCountDto> Counts,
    decimal Deposits,
    decimal Withdrawals);

public sealed record TellerCashProofCountDto(
    decimal FaceValue,
    int Quantity,
    decimal Subtotal);

public sealed record TellerCashProofDto(
    DateOnly Date,
    string CurrencyCode,
    IReadOnlyList<TellerCashProofSessionDto> Sessions);

public sealed record FinancialStatementDto(
    DateOnly From,
    DateOnly AsOf,
    string CurrencyCode,
    IReadOnlyList<ReportLineDto> Assets,
    decimal TotalAssets,
    IReadOnlyList<ReportLineDto> Liabilities,
    decimal TotalLiabilities,
    IReadOnlyList<ReportLineDto> Equity,
    decimal NetIncome,
    decimal TotalEquity,
    decimal TotalLiabilitiesAndEquity,
    IReadOnlyList<ReportLineDto> Income,
    decimal TotalIncome,
    IReadOnlyList<ReportLineDto> Expenses,
    decimal TotalExpenses);

public sealed record DepositListingRowDto(
    DateTime PostedAtUtc,
    DateTime PostedAtPortAuPrince,
    string MemberNo,
    string MemberName,
    string AccountNo,
    string ProductName,
    decimal Amount,
    string CurrencyCode,
    string Description,
    string? CashierName);

public sealed record DepositListingDto(
    DateOnly From,
    DateOnly To,
    string CurrencyCode,
    IReadOnlyList<DepositListingRowDto> Rows,
    decimal Total);

public sealed record LiquidityRatioDto(
    DateOnly AsOf,
    string CurrencyCode,
    IReadOnlyList<ReportLineDto> LiquidAssets,
    decimal TotalLiquidAssets,
    IReadOnlyList<ReportLineDto> MemberDeposits,
    decimal TotalMemberDeposits,
    decimal? Ratio);
