using CPCREDO.Domain.Common;

namespace CPCREDO.Domain.Loans;

public class CreditPool
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string CurrencyCode { get; set; } = Currencies.Htg;
    public decimal FundedTotal { get; set; }
    public decimal DisbursedTotal { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<CreditPoolMovement> Movements { get; set; } = new List<CreditPoolMovement>();
}

public class CreditPoolMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid PoolId { get; set; }
    public string Kind { get; set; } = "Funding";
    public string SourceKind { get; set; } = "Vault";
    public Guid? BankAccountId { get; set; }
    public Guid? LoanId { get; set; }
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = Currencies.Htg;
    public string? Note { get; set; }
    public Guid? JournalEntryId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public CreditPool? Pool { get; set; }
}
