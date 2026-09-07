using System.Text.Json.Serialization;
using CPCREDO.Domain.Accounting;
using CPCREDO.Domain.Common;
using CPCREDO.Infrastructure;
using CPCREDO.Infrastructure.Persistence;
using CPCREDO.Infrastructure.Seed;
using CPCREDO.WebApi.Filters;
using CPCREDO.WebApi.Swagger;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers(options =>
    {
        options.Filters.Add<IdempotencyActionFilter>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

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

        context.Response.ContentType = "application/json";

        if (ex is PostedJournalImmutableException posted)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new { code = posted.Code, error = posted.Message });
            return;
        }

        if (ex is DomainException domain)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { code = domain.Code, error = domain.Message });
            return;
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new
        {
            code = "server.error",
            error = "Une erreur interne s’est produite.",
            detail = app.Environment.IsDevelopment() ? ex?.ToString() : null
        });
    });
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CpcredoDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("CPCREDO.Startup");
    var retries = 10;
    while (true)
    {
        try
        {
            await db.Database.MigrateAsync();
            break;
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
else
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "CPCREDO API v1");
        options.DocumentTitle = "CPCREDO API";
    });
}

app.UseCors("StaffUi");
app.UseAuthentication();
app.Use(async (context, next) =>
{
    var mustChangePassword = context.User.Identity?.IsAuthenticated == true
        && bool.TryParse(context.User.FindFirst("must_change_password")?.Value, out var required)
        && required;
    var allowedDuringPasswordChange = context.Request.Path.StartsWithSegments("/api/auth/change-password")
        || context.Request.Path.StartsWithSegments("/api/auth/me");

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

app.Run();
