namespace CPCREDO.Application.Common;

public interface IAuditLogger
{
    Task LogAsync(
        string action,
        string entityType,
        Guid? entityId = null,
        object? details = null,
        Guid? tenantId = null,
        Guid? userId = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);
}
