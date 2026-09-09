using System.Collections.Concurrent;
using CPCREDO.Application.Members;

namespace CPCREDO.Infrastructure.Members;

public sealed class KycOverrideStore : IKycOverrideStore
{
    private readonly ConcurrentDictionary<Guid, KycOverrideGrant> _grants = new();

    public KycOverrideGrant Issue(KycOverrideGrant grant)
    {
        _grants[grant.GrantId] = grant;
        return grant;
    }

    public bool TryConsume(
        Guid grantId,
        Guid userId,
        Guid documentId,
        string action,
        DateTime utcNow,
        out KycOverrideGrant grant)
    {
        grant = default!;
        if (!_grants.TryRemove(grantId, out var found))
            return false;

        var actionOk = string.Equals(found.Action, action, StringComparison.OrdinalIgnoreCase);
        if (found.UserId != userId
            || found.DocumentId != documentId
            || !actionOk
            || found.ExpiresAtUtc <= utcNow)
        {
            _grants.TryAdd(found.GrantId, found);
            return false;
        }

        grant = found;
        return true;
    }
}
