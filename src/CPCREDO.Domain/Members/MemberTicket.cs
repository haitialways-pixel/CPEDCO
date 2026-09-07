using CPCREDO.Domain.Identity;

namespace CPCREDO.Domain.Members;

public class MemberTicket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid MemberId { get; set; }
    public string TicketNo { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public Guid CreatedByUserId { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? AssignedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }

    public Member? Member { get; set; }
    public User? CreatedBy { get; set; }
    public User? AssignedTo { get; set; }
}
