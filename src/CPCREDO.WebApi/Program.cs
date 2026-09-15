using System.Text.Json.Serialization;
using CPCREDO.Domain.Accounting;
using CPCREDO.Domain.Common;
using CPCREDO.Infrastructure;
using CPCREDO.Infrastructure.Persistence;
using CPCREDO.Infrastructure.Seed;
using CPCREDO.WebApi.Filters;
using CPCREDO.WebApi.Json;
using CPCREDO.WebApi.Swagger;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using CPCREDO.WebApi.Logging;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsProduction())
{
    builder.Logging.AddProvider(new SimpleFileLoggerProvider(
        Path.Combine(builder.Environment.ContentRootPath, "app.log")));
}

builder.Services.AddInfrastructure(builder.Configuration);
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDataProtection()
        .SetApplicationName("CPCREDO")
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "data", "dp-keys")));
}
else
{
    builder.Services.AddDataProtection().SetApplicationName("CPCREDO");
}
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 6_291_456;
});
builder.Services.Configure<CPCREDO.Application.Members.KycStorageOptions>(options =>
{
    options.RootPath = Path.Combine(builder.Environment.ContentRootPath, "data", "kyc");
});
builder.Services.AddControllers(options =>
    {
        options.Filters.Add<IdempotencyActionFilter>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.Converters.Add(new DecimalJsonConverter());
        options.JsonSerializerOptions.Converters.Add(new NullableDecimalJsonConverter());
        options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "CPCREDO API",
            Version = "v1",
            Description = $"{Letterhead.LegalName} — API du personnel."
        });
        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "JWT. Exemple : Bearer {token}",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        });
        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                },
                Array.Empty<string>()
            }
        });
        options.OperationFilter<IdempotencyHeaderOperationFilter>();
    });
}

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
    ?? ["http://localhost:5173", "http://localhost:3000"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("StaffUi", policy =>
        policy.WithOrigins(corsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("CPCREDO API"))
    .AddNpgSql(connectionString, name: "postgres");

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var ex = feature?.Error;
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("CPCREDO.Errors");
        if (ex is not null)
            logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);

        context.Response.ContentType = "application/problem+json";
        var isDev = app.Environment.IsDevelopment();

        if (ex is PostedJournalImmutableException posted)
        {
            await WriteProductionProblem(context, StatusCodes.Status409Conflict, posted.Code, posted.Message, isDev ? posted.ToString() : null);
            return;
        }

        if (ex is DomainException domain)
        {
            await WriteProductionProblem(context, StatusCodes.Status400BadRequest, domain.Code, domain.Message, isDev ? domain.ToString() : null);
            return;
        }

        await WriteProductionProblem(
            context,
            StatusCodes.Status500InternalServerError,
            "server.error",
            "Une erreur interne s’est produite.",
            isDev ? ex?.ToString() : null);
    });
});

if (!app.Environment.IsEnvironment("Testing"))
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CpcredoDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("CPCREDO.Startup");
    var retries = 10;
    while (true)
    {
        try
        {
            var creator = db.GetService<IRelationalDatabaseCreator>();
            if (!await creator.ExistsAsync())
            {
                logger.LogError("La base cpcredo n'existe pas. Relancez INSTALLER-SERVEUR.bat.");
                throw new InvalidOperationException(
                    "La base de données cpcredo n'existe pas. Exécutez INSTALLER-SERVEUR.bat en tant qu'administrateur.");
            }

            await db.GetService<IMigrator>().MigrateAsync();
            break;
        }
        catch (InvalidOperationException ex) when (ex.Message.IndexOf("n'existe pas", StringComparison.Ordinal) >= 0)
        {
            throw;
        }
        catch (Exception ex) when (retries-- > 0)
        {
            logger.LogWarning(ex, "PostgreSQL pas encore prêt. Nouvelle tentative ({Retries} restantes).", retries);
            await Task.Delay(2000);
        }
    }

    await DataSeeder.SeedAsync(scope.ServiceProvider);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "CPCREDO API v1");
        options.DocumentTitle = "CPCREDO API";
    });
}

app.UseCors("StaffUi");
var spaStatic = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var name = ctx.File.Name;
        if (name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
            ctx.Context.Response.Headers.Pragma = "no-cache";
            ctx.Context.Response.Headers.Expires = "0";
        }
        else if (ctx.Context.Request.Path.StartsWithSegments("/assets"))
        {
            ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
    }
};
app.UseDefaultFiles();
app.UseStaticFiles(spaStatic);
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var sessions = context.RequestServices.GetRequiredService<CPCREDO.Application.Identity.IStaffSessionStore>();
        var clock = context.RequestServices.GetRequiredService<CPCREDO.Application.Common.IClock>();
        var jwtOptions = context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<CPCREDO.Infrastructure.Identity.JwtOptions>>().Value;
        var userIdRaw = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
        var jti = context.User.FindFirst("jti")?.Value
            ?? context.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.SerialNumber)?.Value;
        var idleExpired = false;
        if (!Guid.TryParse(userIdRaw, out var userId) || !Guid.TryParse(jti, out var sessionId)
            || !sessions.TryValidate(sessionId, userId, clock.UtcNow, TimeSpan.FromMinutes(Math.Max(1, jwtOptions.IdleMinutes)), out idleExpired))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "auth.session_expired",
                error = idleExpired
                    ? "Session expirée (inactivité de 12 minutes). Connectez-vous à nouveau."
                    : "Session invalide. Connectez-vous à nouveau."
            });
            return;
        }

        sessions.Touch(sessionId, clock.UtcNow);

        var mustChangePassword = bool.TryParse(context.User.FindFirst("must_change_password")?.Value, out var required)
            && required;
        var path = context.Request.Path;
        var allowedDuringPasswordChange = path.StartsWithSegments("/api/auth/change-password")
            || path.StartsWithSegments("/api/auth/me")
            || path.StartsWithSegments("/api/auth/logout");

        if (mustChangePassword && !allowedDuringPasswordChange)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "auth.password_change_required",
                error = "Le changement de mot de passe est obligatoire avant de continuer."
            });
            return;
        }
    }

    await next();
});
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            institution = Letterhead.Sigle,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description
            })
        });
    }
});
app.MapFallbackToFile("index.html", spaStatic);

app.Run();

static bool LooksLikeSqlOrStack(string? text)
{
    if (string.IsNullOrEmpty(text)) return false;
    return text.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
        || text.Contains("SqlException", StringComparison.OrdinalIgnoreCase)
        || text.Contains("SELECT ", StringComparison.OrdinalIgnoreCase)
        || text.Contains("INSERT ", StringComparison.OrdinalIgnoreCase)
        || text.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase)
        || text.Contains("DELETE FROM", StringComparison.OrdinalIgnoreCase)
        || text.Contains(" at ", StringComparison.Ordinal)
        || text.Contains("\n   at ");
}

static async Task WriteProductionProblem(HttpContext context, int status, string code, string title, string? detail)
{
    context.Response.StatusCode = status;
    var safeTitle = LooksLikeSqlOrStack(title) ? "Une erreur interne s’est produite." : title;
    var problem = new ProblemDetails
    {
        Status = status,
        Title = safeTitle,
        Type = code
    };
    if (!string.IsNullOrEmpty(detail) && !LooksLikeSqlOrStack(detail))
        problem.Detail = detail;
    problem.Extensions["code"] = code;
    problem.Extensions["error"] = safeTitle;
    await context.Response.WriteAsJsonAsync(problem);
}
