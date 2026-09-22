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
    decimal Withdrawals,
    IReadOnlyList<TellerCashProofInternalDto> InternalMovements);

public sealed record TellerCashProofInternalDto(
    string Direction,
    string Status,
    decimal Amount,
    string? Counterparty);

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

public sealed record ParBucketDto(
    int Days,
    decimal Outstanding,
    decimal? Ratio);

public sealed record ParCt90Dto(
    DateOnly AsOf,
    string CurrencyCode,
    decimal PortfolioOutstanding,
    int LoanCount,
    ParBucketDto Par1,
    ParBucketDto Par7,
    ParBucketDto Par30);

public sealed record RenewalRegisterRowDto(
    DateTime RenewedAtUtc,
    string MemberNo,
    string MemberName,
    string OldLoanNo,
    string NewLoanNo,
    int NewCycle,
    decimal PreviousPrincipal,
    decimal NewPrincipal,
    bool IsEvergreen,
    string CurrencyCode);

public sealed record RenewalRegisterDto(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<RenewalRegisterRowDto> Rows);

public sealed record LiquidityRatioDto(
    DateOnly AsOf,
    string CurrencyCode,
    IReadOnlyList<ReportLineDto> LiquidAssets,
    decimal TotalLiquidAssets,
    IReadOnlyList<ReportLineDto> MemberDeposits,
    decimal TotalMemberDeposits,
    decimal? Ratio);

public sealed record CreditReportCountDto(string Key, int Count, string Href);

public sealed record CreditReportAmountDto(string Key, decimal Amount, string Href);

public sealed record CreditRejectReasonDto(string Reason, int Count);

public sealed record CreditBreakdownDto(
    string Id,
    string Label,
    int Count,
    decimal Requested,
    decimal Disbursed,
    string Href);

public sealed record CreditReportDto(
    DateOnly From,
    DateOnly To,
    string CurrencyCode,
    int Received,
    int Pending,
    int Approved,
    int Rejected,
    int Cancelled,
    int Disbursed,
    decimal RequestedAmount,
    decimal ApprovedAmount,
    decimal DisbursedAmount,
    decimal OutstandingPrincipal,
    decimal PoolOpening,
    decimal PoolFunded,
    decimal PoolDisbursed,
    decimal PoolAvailable,
    decimal InterestReceived,
    decimal InterestAccrued,
    decimal FeesPenaltiesReceived,
    decimal? ApprovalRate,
    double? AvgDaysApplyToDecision,
    double? AvgDaysDecisionToDisburse,
    decimal Par30,
    decimal Par90,
    IReadOnlyList<CreditRejectReasonDto> RejectReasons,
    IReadOnlyList<CreditBreakdownDto> ByProduct,
    IReadOnlyList<CreditBreakdownDto> ByOfficer);
