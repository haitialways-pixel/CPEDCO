namespace CPCREDO.Application.Identity;

public interface IStaffSessionStore
{
    Guid Start(Guid userId, DateTime utcNow);
    bool TryValidate(Guid sessionId, Guid userId, DateTime utcNow, TimeSpan idle, out bool idleExpired);
    void Touch(Guid sessionId, DateTime utcNow);
    void Revoke(Guid sessionId);
    void RevokeUser(Guid userId);
}
