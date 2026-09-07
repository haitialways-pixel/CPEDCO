using System.Security.Cryptography;
using System.Text;
using CPCREDO.Application.Common;
using CPCREDO.Domain.Audit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CPCREDO.WebApi.Filters;

public sealed class IdempotencyActionFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var required = context.ActionDescriptor.EndpointMetadata
            .OfType<RequiresIdempotencyKeyAttribute>()
            .Any();

        if (!required)
        {
            await next();
            return;
        }

        var http = context.HttpContext;
        var key = http.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(key))
        {
            context.Result = new BadRequestObjectResult(new
            {
                code = "idempotency.missing",
                error = "L’en-tête Idempotency-Key est obligatoire pour cette opération monétaire."
            });
            return;
        }

        var currentUser = http.RequestServices.GetRequiredService<ICurrentUser>();
        if (currentUser.TenantId is null || currentUser.UserId is null)
        {
            context.Result = new UnauthorizedObjectResult(new
            {
                code = "auth.unauthorized",
                error = "Session invalide."
            });
            return;
        }

        http.Request.EnableBuffering();
        string body;
        using (var reader = new StreamReader(http.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
        {
            body = await reader.ReadToEndAsync();
            http.Request.Body.Position = 0;
        }

        var requestHash = Sha256($"{http.Request.Method}|{http.Request.Path}|{body}");
        var store = http.RequestServices.GetRequiredService<IIdempotencyStore>();
        var existing = await store.FindAsync(currentUser.TenantId.Value, key, http.RequestAborted);

        if (existing is not null)
        {
            if (existing.RequestHash != requestHash)
            {
                context.Result = new ObjectResult(new
                {
                    code = "idempotency.conflict",
                    error = "Cette clé d’idempotence a déjà été utilisée avec une requête différente."
                })
                {
                    StatusCode = StatusCodes.Status409Conflict
                };
                return;
            }

            context.Result = new ContentResult
            {
                StatusCode = existing.ResponseStatusCode,
                Content = existing.ResponseBody,
                ContentType = existing.ContentType
            };
            return;
        }

        var originalBody = http.Response.Body;
        await using var buffer = new MemoryStream();
        http.Response.Body = buffer;

        var executed = await next();

        buffer.Position = 0;
        var responseBody = await new StreamReader(buffer).ReadToEndAsync();
        buffer.Position = 0;
        await buffer.CopyToAsync(originalBody);
        http.Response.Body = originalBody;

        if (executed.Exception is not null || http.Response.StatusCode is < 200 or >= 300)
            return;

        var clock = http.RequestServices.GetRequiredService<IClock>();
        var now = clock.UtcNow;
        await store.SaveAsync(new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            TenantId = currentUser.TenantId.Value,
            UserId = currentUser.UserId.Value,
            Key = key,
            HttpMethod = http.Request.Method,
            Path = http.Request.Path.ToString(),
            RequestHash = requestHash,
            ResponseStatusCode = http.Response.StatusCode,
            ResponseBody = responseBody,
            ContentType = http.Response.ContentType ?? "application/json",
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(24)
        }, http.RequestAborted);
    }

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }
}
