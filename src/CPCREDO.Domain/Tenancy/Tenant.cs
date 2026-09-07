using CPCREDO.Domain.Common;
using CPCREDO.Domain.Members;

namespace CPCREDO.Domain.Tenancy;

public class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Sigle { get; set; } = Letterhead.Sigle;
    public string LegalName { get; set; } = Letterhead.LegalName;
    public string City { get; set; } = Letterhead.City;
    public string Country { get; set; } = Letterhead.Country;
    public string PrimaryCurrency { get; set; } = Currencies.Htg;
    public string SecondaryCurrency { get; set; } = Currencies.Usd;
    public string DisplayTimeZone { get; set; } = CpcredoTimeZone.DisplayId;
    public decimal ShareParValue { get; set; } = MembershipRules.DefaultShareParValue;
    public Guid? DefaultBranchId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }

    public Branch? DefaultBranch { get; set; }
    public ICollection<Branch> Branches { get; set; } = new List<Branch>();
}
