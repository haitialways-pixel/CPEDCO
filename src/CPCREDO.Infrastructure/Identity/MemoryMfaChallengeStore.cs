using System.Collections.Concurrent;
using CPCREDO.Application.Identity;

namespace CPCREDO.Infrastructure.Identity;

public sealed class MemoryMfaChallengeStore : IMfaChallengeStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<Guid, MfaChallenge> _items = new();

    public Guid Start(Guid userId, DateTime utcNow, byte[]? pendingSecret)
    {
        var ticket = Guid.NewGuid();
        _items[ticket] = new MfaChallenge(ticket, userId, utcNow.Add(Lifetime), pendingSecret, 0);
        return ticket;
    }

    public MfaChallenge? Get(Guid ticket, DateTime utcNow)
    {
        if (!_items.TryGetValue(ticket, out var item))
            return null;
        if (utcNow > item.ExpiresUtc || item.Failures >= 5)
        {
            _items.TryRemove(ticket, out _);
            return null;
        }
        return item;
    }

    public void RegisterFailure(Guid ticket)
    {
        if (!_items.TryGetValue(ticket, out var item))
            return;
        var next = item with { Failures = item.Failures + 1 };
        if (next.Failures >= 5)
            _items.TryRemove(ticket, out _);
        else
            _items.TryUpdate(ticket, next, item);
    }

    public void Consume(Guid ticket) => _items.TryRemove(ticket, out _);
}
