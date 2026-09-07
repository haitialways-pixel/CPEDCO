using CPCREDO.Domain.Common;

namespace CPCREDO.Domain.Savings;

public class SavingsLien
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid SavingsAccountId { get; set; }
    public string CurrencyCode { get; set; } = Currencies.Htg;
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReleasedAtUtc { get; set; }

    public SavingsAccount? SavingsAccount { get; set; }
}
