using System.Security.Claims;
using CPCREDO.Application.Common;
using Microsoft.AspNetCore.Http;

namespace CPCREDO.Infrastructure.Identity;

public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _http;

    public HttpCurrentUser(IHttpContextAccessor http)
    {
        _http = http;
    }

    private ClaimsPrincipal? Principal => _http.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId => ParseGuid(Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? Principal?.FindFirstValue("sub"));

    public Guid? TenantId => ParseGuid(Principal?.FindFirstValue("tenant_id"));

    public Guid? BranchId => ParseGuid(Principal?.FindFirstValue("branch_id"));

    public string? Username => Principal?.Identity?.Name;

    public IReadOnlyList<string> Roles =>
        Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? [];

    public string? IpAddress =>
        _http.HttpContext?.Connection.RemoteIpAddress?.ToString();

    private static Guid? ParseGuid(string? value) =>
        Guid.TryParse(value, out var id) ? id : null;
}
