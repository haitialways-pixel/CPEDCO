using CPCREDO.Domain.Tenancy;

namespace CPCREDO.Domain.Members;

public class Member
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public string MemberNo { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Cin { get; set; }
    public string? Nif { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? AlternatePhone { get; set; }
    public string AddressLine { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Commune { get; set; }
    public MemberStatus Status { get; set; } = MemberStatus.Pending;
    public KycStatus KycStatus { get; set; } = KycStatus.Incomplete;
    public LegalStatus LegalStatus { get; set; } = LegalStatus.Usager;
    public bool IsFounder { get; set; }
    public FounderGroup? FounderGroup { get; set; }
    public DateTime? UsagerSinceUtc { get; set; }
    public int ProbationDays { get; set; } = MembershipRules.DefaultProbationDays;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public Tenant? Tenant { get; set; }
    public Branch? Branch { get; set; }
    public ICollection<ShareAccount> ShareAccounts { get; set; } = new List<ShareAccount>();

    public string FullName => $"{FirstName} {LastName}".Trim();

    public int QualificationShareCount =>
        ShareAccounts.Where(s => s.ShareType == ShareType.Qualification && s.IsActive).Sum(s => s.ShareCount);

    public int PermanentShareCount =>
        ShareAccounts.Where(s => s.ShareType == ShareType.Permanent && s.IsActive).Sum(s => s.ShareCount);

    public bool HasVotingRights => MembershipRules.HasVotingRights(this);
}
