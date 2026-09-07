using CPCREDO.Domain.Tenancy;

namespace CPCREDO.Domain.Identity;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool MustChangePassword { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }

    public Tenant? Tenant { get; set; }
    public Branch? Branch { get; set; }
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
