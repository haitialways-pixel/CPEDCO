namespace CPCREDO.Application.Identity;

public interface IStaffSessionStore
{
    Guid Start(Guid userId, DateTime utcNow);
    bool HasActive(Guid userId, DateTime utcNow, TimeSpan idle);
    bool TryValidate(Guid sessionId, Guid userId, DateTime utcNow, TimeSpan idle, out bool idleExpired);
    void Touch(Guid sessionId, DateTime utcNow);
    void BindTill(Guid sessionId, Guid tillSessionId);
    Guid? GetBoundTill(Guid sessionId);
    void Revoke(Guid sessionId);
    void RevokeUser(Guid userId);
}
