using CPCREDO.Domain.Audit;

namespace CPCREDO.Application.Common;

public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> FindAsync(Guid tenantId, string key, CancellationToken cancellationToken = default);

    Task SaveAsync(IdempotencyRecord record, CancellationToken cancellationToken = default);
}
