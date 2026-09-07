using System.Text.Json;
using CPCREDO.Application.Common;
using CPCREDO.Domain.Audit;
using CPCREDO.Infrastructure.Persistence;

namespace CPCREDO.Infrastructure.Audit;

public sealed class AuditLogger : IAuditLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly CpcredoDbContext _db;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;

    public AuditLogger(CpcredoDbContext db, IClock clock, ICurrentUser currentUser)
    {
        _db = db;
        _clock = clock;
        _currentUser = currentUser;
    }

    public async Task LogAsync(
        string action,
        string entityType,
        Guid? entityId = null,
        object? details = null,
        Guid? tenantId = null,
        Guid? userId = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId ?? _currentUser.TenantId,
            UserId = userId ?? _currentUser.UserId,
            OccurredAtUtc = _clock.UtcNow,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details, JsonOptions),
            IpAddress = ipAddress ?? _currentUser.IpAddress
        });

        await _db.SaveChangesAsync(cancellationToken);
    }
}
