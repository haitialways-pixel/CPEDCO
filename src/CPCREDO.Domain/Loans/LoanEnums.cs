namespace CPCREDO.Domain.Loans;

public enum InterestMethod
{
    FlatOnOriginalPrincipalForTerm = 0
}

public enum RepaymentFrequency
{
    Weekly = 0
}

public enum LoanStatus
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    Active = 3,
    Renewed = 4,
    PaidOff = 5,
    WrittenOff = 6,
    Rejected = 7
}
