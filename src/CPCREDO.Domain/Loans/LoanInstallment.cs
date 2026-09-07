namespace CPCREDO.Domain.Loans;

public class LoanInstallment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LoanId { get; set; }
    public int LineNo { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal PrincipalDue { get; set; }
    public decimal InterestDue { get; set; }
    public decimal PenaltyDue { get; set; }
    public decimal TotalDue { get; set; }
    public decimal PrincipalPaid { get; set; }
    public decimal InterestPaid { get; set; }
    public decimal PenaltyPaid { get; set; }
    public DateOnly? LastPenaltyAccruedOn { get; set; }

    public Loan? Loan { get; set; }
}
