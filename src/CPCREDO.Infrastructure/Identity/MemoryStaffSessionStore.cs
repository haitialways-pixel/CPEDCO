using System.Collections.Concurrent;
using CPCREDO.Application.Identity;

namespace CPCREDO.Infrastructure.Identity;

public sealed class MemoryStaffSessionStore : IStaffSessionStore
{
    private readonly ConcurrentDictionary<Guid, Entry> _sessions = new();

    public Guid Start(Guid userId, DateTime utcNow)
    {
        var id = Guid.NewGuid();
        _sessions[id] = new Entry(userId, utcNow, false, null);
        return id;
    }

    public bool HasActive(Guid userId, DateTime utcNow, TimeSpan idle)
    {
        foreach (var pair in _sessions)
        {
            var e = pair.Value;
            if (e.UserId != userId || e.Revoked)
                continue;
            if (utcNow - e.LastActivityUtc <= idle)
                return true;
        }
        return false;
    }

    public bool TryValidate(Guid sessionId, Guid userId, DateTime utcNow, TimeSpan idle, out bool idleExpired)
    {
        idleExpired = false;
        if (!_sessions.TryGetValue(sessionId, out var entry) || entry.Revoked || entry.UserId != userId)
            return false;

        if (utcNow - entry.LastActivityUtc > idle)
        {
            idleExpired = true;
            _sessions.TryUpdate(sessionId, entry with { Revoked = true }, entry);
            return false;
        }

        return true;
    }

    public void BindTill(Guid sessionId, Guid tillSessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry) || entry.Revoked)
            return;
        _sessions.TryUpdate(sessionId, entry with { TillSessionId = tillSessionId }, entry);
    }

    public Guid? GetBoundTill(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry) || entry.Revoked)
            return null;
        return entry.TillSessionId;
    }

    public void Touch(Guid sessionId, DateTime utcNow)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry) || entry.Revoked)
            return;
        _sessions.TryUpdate(sessionId, entry with { LastActivityUtc = utcNow }, entry);
    }

    public void Revoke(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry))
            return;
        _sessions.TryUpdate(sessionId, entry with { Revoked = true }, entry);
    }

    public void RevokeUser(Guid userId)
    {
        foreach (var pair in _sessions)
        {
            if (pair.Value.UserId == userId && !pair.Value.Revoked)
                _sessions.TryUpdate(pair.Key, pair.Value with { Revoked = true }, pair.Value);
        }
    }

    private sealed record Entry(Guid UserId, DateTime LastActivityUtc, bool Revoked, Guid? TillSessionId);
}
