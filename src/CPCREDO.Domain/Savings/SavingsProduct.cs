using CPCREDO.Domain.Common;

namespace CPCREDO.Domain.Savings;

public class SavingsProduct
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = Currencies.Htg;
    public decimal MinimumBalance { get; set; }
    public Guid LiabilityGlAccountId { get; set; }
    public Guid CashGlAccountId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
}
