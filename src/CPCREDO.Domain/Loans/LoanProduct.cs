using CPCREDO.Domain.Common;

namespace CPCREDO.Domain.Loans;

public class LoanProduct
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    public string? CommercialName { get; set; }
    public string? SmsName { get; set; }
    public int TermDays { get; set; } = 90;
    public int InstallmentCount { get; set; } = 12;
    public RepaymentFrequency RepaymentFrequency { get; set; } = RepaymentFrequency.Weekly;
    public InterestMethod InterestMethod { get; set; } = InterestMethod.FlatOnOriginalPrincipalForTerm;
    public decimal DefaultRatePercent { get; set; } = 20m;
    public int? MaxRenewals { get; set; }
    public decimal CompulsorySavingsPercent { get; set; } = 10m;
    public decimal? MinPrincipal { get; set; }
    public decimal? MaxPrincipal { get; set; }
    public decimal? OfficerMaxApproval { get; set; }
    public bool EarlyPayoffChargesFullFlatInterest { get; set; }
    public decimal LatePenaltyPercentPerDay { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(CommercialName) ? LegalName : CommercialName.Trim();
}
