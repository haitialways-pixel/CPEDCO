using CPCREDO.Domain.Common;

namespace CPCREDO.Domain.Loans;

public class LoanRepayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid LoanId { get; set; }
    public string ReceiptNo { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal PenaltyAllocated { get; set; }
    public decimal InterestAllocated { get; set; }
    public decimal PrincipalAllocated { get; set; }
    public string CurrencyCode { get; set; } = Currencies.Htg;
    public Guid PostedJournalId { get; set; }
    public Guid? TillSessionId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string? IdempotencyKey { get; set; }

    public Loan? Loan { get; set; }
}
