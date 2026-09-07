namespace CPCREDO.Domain.Identity;

public class Role
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string DisplayNameFr { get; set; } = string.Empty;
    public string DisplayNameHt { get; set; } = string.Empty;
    public string DisplayNameEn { get; set; } = string.Empty;
    public string DescriptionFr { get; set; } = string.Empty;
    public bool IsReadOnly { get; set; }
    public bool IsSystem { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
