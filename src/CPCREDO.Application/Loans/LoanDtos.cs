namespace CPCREDO.Application.Loans;

public sealed record LoanProductDto(
    Guid Id,
    string Code,
    string LegalName,
    string? CommercialName,
    string DisplayName,
    string? SmsName,
    int TermDays,
    int InstallmentCount,
    string RepaymentFrequency,
    string InterestMethod,
    decimal DefaultRatePercent,
    int? MaxRenewals,
    decimal CompulsorySavingsPercent,
    decimal? MinPrincipal,
    decimal? MaxPrincipal,
    decimal? OfficerMaxApproval,
    bool EarlyPayoffChargesFullFlatInterest,
    decimal LatePenaltyPercentPerDay,
    bool IsActive);

public sealed record SaveLoanProductRequest
{
    public string Code { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    public string? CommercialName { get; set; }
    public string? SmsName { get; set; }
    public int TermDays { get; set; } = 90;
    public int InstallmentCount { get; set; } = 12;
    public string RepaymentFrequency { get; set; } = "Weekly";
    public decimal DefaultRatePercent { get; set; } = 20m;
    public int? MaxRenewals { get; set; }
    public decimal CompulsorySavingsPercent { get; set; } = 10m;
    public decimal? MinPrincipal { get; set; }
    public decimal? MaxPrincipal { get; set; }
    public decimal? OfficerMaxApproval { get; set; }
    public bool EarlyPayoffChargesFullFlatInterest { get; set; }
    public decimal LatePenaltyPercentPerDay { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed record LoanInstallmentDto(
    int LineNo,
    DateOnly DueDate,
    decimal PrincipalDue,
    decimal InterestDue,
    decimal TotalDue,
    decimal PrincipalPaid = 0,
    decimal InterestPaid = 0,
    decimal PenaltyDue = 0,
    decimal PenaltyPaid = 0,
    decimal Remaining = 0);

public sealed record LoanScheduleDto(
    decimal Principal,
    decimal AgreedRatePercent,
    int RateAppliesToTermDays,
    decimal TotalInterest,
    decimal TotalDue,
    int InstallmentCount,
    IReadOnlyList<LoanInstallmentDto> Installments);

public sealed record PreviewLoanRequest
{
    public Guid ProductId { get; set; }
    public decimal Principal { get; set; }
    public decimal AgreedRatePercent { get; set; }
    public DateOnly? StartDate { get; set; }
}

public sealed record PayoffQuoteDto(
    decimal Principal,
    decimal InterestDue,
    decimal TotalDue,
    int DaysElapsed,
    int TermDays,
    bool ChargesFullFlatInterest);

public sealed record CreateLoanRequest
{
    public Guid MemberId { get; set; }
    public Guid ProductId { get; set; }
    public decimal Principal { get; set; }
    public decimal AgreedRatePercent { get; set; }
}

public sealed record RejectLoanRequest
{
    public string? Reason { get; set; }
}

public sealed record DisburseLoanRequest
{
    public Guid? SavingsAccountId { get; set; }
}

public sealed record LoanDto(
    Guid Id,
    string LoanNo,
    Guid MemberId,
    string MemberNo,
    string MemberName,
    Guid ProductId,
    string ProductDisplayName,
    decimal Principal,
    decimal AgreedRatePercent,
    int RateAppliesToTermDays,
    decimal TotalInterest,
    decimal TotalDue,
    string CurrencyCode,
    string Status,
    int CycleNumber,
    Guid? RenewedFromLoanId,
    Guid? RenewedToLoanId,
    DateOnly OriginationDate,
    decimal CompulsorySavingsPercent,
    decimal CompulsorySavingsAmount,
    decimal CashDisbursedAmount,
    decimal SavingsDisbursedAmount,
    decimal? OfficerMaxApproval,
    bool RequiresSecondApproval,
    Guid? SubmittedByUserId,
    DateTime? SubmittedAtUtc,
    Guid? Approver1Id,
    string? Approver1Name,
    DateTime? Approved1AtUtc,
    Guid? Approver2Id,
    string? Approver2Name,
    DateTime? Approved2AtUtc,
    Guid? DisbursedByUserId,
    string? DisbursedByName,
    DateTime? DisbursedAtUtc,
    Guid? PostedJournalId,
    Guid? SavingsAccountId,
    Guid? LienId,
    string? RejectReason,
    int DaysPastDue,
    LoanScheduleDto Schedule);

public sealed record RepayLoanRequest
{
    public decimal Amount { get; set; }
    public string? CurrencyCode { get; set; }
}

public sealed record LoanReceiptDto(
    string LetterheadSigle,
    string LetterheadLine2,
    string LetterheadLine3,
    string LetterheadLine4,
    string Type,
    string Title,
    string ReceiptNo,
    string JournalNo,
    string LoanNo,
    string MemberNo,
    string MemberName,
    string ProductName,
    decimal Amount,
    decimal Penalty,
    decimal Interest,
    decimal Principal,
    string CurrencyCode,
    decimal RemainingBalance,
    string CashierName,
    string BranchName,
    DateTime PostedAtUtc,
    DateTime PostedAtPortAuPrince);

public sealed record RepaymentResultDto(LoanDto Loan, LoanReceiptDto Receipt);

public sealed record AccrualResultDto(int LoansUpdated, DateOnly AsOf);

public sealed record CollectionSheetRowDto(
    Guid LoanId,
    string LoanNo,
    string MemberNo,
    string MemberName,
    string ProductName,
    int LineNo,
    DateOnly DueDate,
    decimal PrincipalRemaining,
    decimal InterestRemaining,
    decimal PenaltyRemaining,
    decimal TotalRemaining,
    int DaysPastDue,
    bool IsArrears);

public sealed record CollectionSheetDto(
    string Period,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<CollectionSheetRowDto> Rows);
