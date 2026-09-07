using CPCREDO.Domain.Common;
using CPCREDO.Domain.Tenancy;

namespace CPCREDO.Domain.Accounting;

public class GlAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid? ParentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameFr { get; set; } = string.Empty;
    public string NameHt { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public GlAccountType AccountType { get; set; }
    public NormalBalance NormalBalance { get; set; }
    public string CurrencyCode { get; set; } = Currencies.Htg;
    public bool IsPostable { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }

    public Tenant? Tenant { get; set; }
    public GlAccount? Parent { get; set; }
    public ICollection<GlAccount> Children { get; set; } = new List<GlAccount>();
}
