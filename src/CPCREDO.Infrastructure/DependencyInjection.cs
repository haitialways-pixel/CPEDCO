using System.Text;
using CPCREDO.Application.Accounting;
using CPCREDO.Application.Common;
using CPCREDO.Application.Identity;
using CPCREDO.Application.Members;
using CPCREDO.Application.Reports;
using CPCREDO.Application.Savings;
using CPCREDO.Application.Teller;
using CPCREDO.Application.Tenancy;
using CPCREDO.Application.Treasury;
using CPCREDO.Application.Loans;
using CPCREDO.Domain.Identity;
using CPCREDO.Infrastructure.Accounting;
using CPCREDO.Infrastructure.Members;
using CPCREDO.Infrastructure.Teller;
using CPCREDO.Infrastructure.Treasury;
using CPCREDO.Infrastructure.Loans;
using CPCREDO.Infrastructure.Audit;
using CPCREDO.Infrastructure.Reports;
using CPCREDO.Infrastructure.Savings;
using CPCREDO.Infrastructure.Identity;
using CPCREDO.Infrastructure.Persistence;
using CPCREDO.Infrastructure.Tenancy;
using CPCREDO.Infrastructure.Time;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace CPCREDO.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<CpcredoDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npg =>
            {
                npg.MigrationsHistoryTable("__ef_migrations_history");
                npg.MigrationsAssembly(typeof(CpcredoDbContext).Assembly.FullName);
                npg.EnableRetryOnFailure(5);
            });
            options.UseSnakeCaseNamingConvention();
        });

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("Jwt configuration is missing.");

        if (string.IsNullOrWhiteSpace(jwt.Secret) || jwt.Secret.Length < 32)
            throw new InvalidOperationException("Jwt:Secret must be at least 32 characters.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                    ClockSkew = TimeSpan.FromMinutes(1),
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role,
                    NameClaimType = System.Security.Claims.ClaimTypes.Name
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("AdminOnly", policy => policy.RequireRole(RoleNames.Admin));
            options.AddPolicy("CanWrite", policy => policy.RequireRole(RoleNames.WriteRoles.ToArray()));
            options.AddPolicy("CanReverse", policy => policy.RequireRole(RoleNames.ReverseRoles.ToArray()));
        });

        services.AddHttpContextAccessor();
        services.AddScoped<IClock, SystemClock>();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IStaffService, StaffService>();
        services.AddScoped<IInstitutionPublicService, InstitutionPublicService>();
        services.AddScoped<IJournalService, JournalService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IMemberService, MemberService>();
        services.AddScoped<ISavingsService, SavingsService>();
        services.AddScoped<ITellerService, TellerService>();
        services.AddScoped<ITreasuryService, TreasuryService>();
        services.AddScoped<ILoanProductService, LoanProductService>();
        services.AddScoped<ILoanService, LoanService>();

        return services;
    }
}
