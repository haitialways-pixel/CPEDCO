using CPCREDO.Domain.Identity;

namespace CPCREDO.Domain.Members;

public class KycDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid MemberId { get; set; }
    public KycDocumentType Type { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public DateTime UploadedAtUtc { get; set; }
    public Guid UploadedBy { get; set; }

    public Member? Member { get; set; }
    public User? UploadedByUser { get; set; }
}
