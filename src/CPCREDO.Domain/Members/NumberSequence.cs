namespace CPCREDO.Domain.Members;

public class NumberSequence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Key { get; set; } = string.Empty;
    public long LastValue { get; set; }
}
