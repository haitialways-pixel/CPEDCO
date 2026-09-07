using CPCREDO.Domain.Common;

namespace CPCREDO.Domain.Members;

/// <summary>
/// Qualification shares confer membership (one sociétaire = one vote).
/// Permanent shares are capital only and never confer a vote.
/// </summary>
public class ShareAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid MemberId { get; set; }
    public Guid BranchId { get; set; }
    public string AccountNo { get; set; } = string.Empty;
    public ShareType ShareType { get; set; } = ShareType.Qualification;
    public int ShareCount { get; set; }
    public decimal ParValue { get; set; } = MembershipRules.DefaultShareParValue;
    public string CurrencyCode { get; set; } = Currencies.Htg;
    public bool IsActive { get; set; } = true;
    public DateTime OpenedAtUtc { get; set; }

    public Member? Member { get; set; }

    public decimal BookValue => MembershipRules.BookValue(ShareCount, ParValue);
}
