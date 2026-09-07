using CPCREDO.Domain.Common;

namespace CPCREDO.Domain.Loans;

public class Loan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public Guid MemberId { get; set; }
    public Guid ProductId { get; set; }
    public string LoanNo { get; set; } = string.Empty;
    public decimal Principal { get; set; }
    public decimal AgreedRatePercent { get; set; }
    public int RateAppliesToTermDays { get; set; } = 90;
    public int TermDays { get; set; } = 90;
    public int InstallmentCount { get; set; } = 12;
    public RepaymentFrequency RepaymentFrequency { get; set; } = RepaymentFrequency.Weekly;
    public InterestMethod InterestMethod { get; set; } = InterestMethod.FlatOnOriginalPrincipalForTerm;
    public decimal TotalInterest { get; set; }
    public decimal TotalDue { get; set; }
    public string CurrencyCode { get; set; } = Currencies.Htg;
    public LoanStatus Status { get; set; } = LoanStatus.Draft;
    public int CycleNumber { get; set; } = 1;
    public Guid? RenewedFromLoanId { get; set; }
    public Guid? RenewedToLoanId { get; set; }
    public DateOnly OriginationDate { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public decimal CompulsorySavingsPercent { get; set; }
    public decimal CompulsorySavingsAmount { get; set; }
    public decimal CashDisbursedAmount { get; set; }
    public decimal SavingsDisbursedAmount { get; set; }
    public Guid? SavingsAccountId { get; set; }
    public Guid? LienId { get; set; }
    public Guid? PostedJournalId { get; set; }
    public Guid? TillSessionId { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public Guid? Approver1Id { get; set; }
    public DateTime? Approved1AtUtc { get; set; }
    public Guid? Approver2Id { get; set; }
    public DateTime? Approved2AtUtc { get; set; }
    public Guid? RejectedByUserId { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public string? RejectReason { get; set; }
    public Guid? DisbursedByUserId { get; set; }
    public DateTime? DisbursedAtUtc { get; set; }
    public int DaysPastDue { get; set; }
    public DateOnly? LastAccruedOn { get; set; }

    public LoanProduct? Product { get; set; }
    public ICollection<LoanInstallment> Installments { get; set; } = new List<LoanInstallment>();
    public ICollection<LoanRepayment> Repayments { get; set; } = new List<LoanRepayment>();
}
