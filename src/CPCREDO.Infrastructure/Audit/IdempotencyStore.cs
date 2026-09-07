using CPCREDO.Application.Common;
using CPCREDO.Domain.Audit;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CPCREDO.Infrastructure.Audit;

public sealed class IdempotencyStore : IIdempotencyStore
{
    private readonly CpcredoDbContext _db;

    public IdempotencyStore(CpcredoDbContext db)
    {
        _db = db;
    }

    public Task<IdempotencyRecord?> FindAsync(Guid tenantId, string key, CancellationToken cancellationToken = default) =>
        _db.IdempotencyRecords.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Key == key, cancellationToken);

    public async Task SaveAsync(IdempotencyRecord record, CancellationToken cancellationToken = default)
    {
        _db.IdempotencyRecords.Add(record);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
