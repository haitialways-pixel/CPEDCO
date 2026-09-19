using CPCREDO.Domain.Common;

namespace CPCREDO.Domain.Savings;

public class SavingsProduct
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    public string? CommercialName { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = Currencies.Htg;
    public SavingsProductKind ProductKind { get; set; } = SavingsProductKind.AVue;
    public int? TermDays { get; set; }
    public decimal InterestRatePercent { get; set; }
    public SavingsInterestMethod InterestMethod { get; set; } = SavingsInterestMethod.None;
    public decimal MinOpeningAmount { get; set; }
    public decimal MinimumBalance { get; set; }
    public bool AllowWithdrawBeforeTerm { get; set; }
    public Guid LiabilityGlAccountId { get; set; }
    public Guid CashGlAccountId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(CommercialName) ? CommercialName.Trim()
        : !string.IsNullOrWhiteSpace(LegalName) ? LegalName
        : Name;

    public bool IsTermLike =>
        ProductKind == SavingsProductKind.Terme
        || (ProductKind == SavingsProductKind.Autre && TermDays is > 0);
}
