namespace CPCREDO.Application.Common;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    Guid? TenantId { get; }
    Guid? BranchId { get; }
    string? Username { get; }
    IReadOnlyList<string> Roles { get; }
    string? IpAddress { get; }
}
