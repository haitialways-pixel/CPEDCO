using System.Text;
using CPCREDO.Application.Accounting;
using CPCREDO.Application.Admin;
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
using CPCREDO.Infrastructure.Admin;
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
                options.MapInboundClaims = false;
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
            options.AddPolicy("CanBackup", policy => policy.RequireRole(RoleNames.BackupRoles.ToArray()));
            options.AddPolicy("CanWrite", policy => policy.RequireRole(RoleNames.WriteRoles.ToArray()));
            options.AddPolicy("CanReverse", policy => policy.RequireRole(RoleNames.ReverseRoles.ToArray()));
            options.AddPolicy("CanKycUpload", policy => policy.RequireRole(RoleNames.KycUploadRoles.ToArray()));
            options.AddPolicy("CanKycManage", policy => policy.RequireRole(RoleNames.KycManageRoles.ToArray()));
            options.AddPolicy("CanTill", policy => policy.RequireRole(RoleNames.TillRoles.ToArray()));
            options.AddPolicy("CanDisburse", policy => policy.RequireRole(RoleNames.DisburseRoles.ToArray()));
            options.AddPolicy("CanCollect", policy => policy.RequireRole(RoleNames.CollectRoles.ToArray()));
            options.AddPolicy("CanLoanDraft", policy => policy.RequireRole(RoleNames.LoanDraftRoles.ToArray()));
            options.AddPolicy("CanLoanApprove", policy => policy.RequireRole(RoleNames.LoanApproveRoles.ToArray()));
            options.AddPolicy("CanCreditReport", policy => policy.RequireRole(RoleNames.CreditReportRoles.ToArray()));
            options.AddPolicy("CanFundCreditPool", policy => policy.RequireRole(RoleNames.TreasuryRoles.ToArray()));
            options.AddPolicy("CanCreateMember", policy => policy.RequireRole(RoleNames.MemberCreateRoles.ToArray()));
            options.AddPolicy("CanEditMember", policy => policy.RequireRole(RoleNames.MemberEditRoles.ToArray()));
            options.AddPolicy("CanTickets", policy => policy.RequireRole(RoleNames.TicketRoles.ToArray()));
            options.AddPolicy("CanTreasury", policy => policy.RequireRole(RoleNames.TreasuryRoles.ToArray()));
            options.AddPolicy("CanTreasuryDraft", policy => policy.RequireRole(RoleNames.TreasuryDraftRoles.ToArray()));
            options.AddPolicy("CanManageProducts", policy => policy.RequireRole(RoleNames.ProductRoles.ToArray()));
            options.AddPolicy("CanLivret", policy => policy.RequireRole(RoleNames.LivretRoles.ToArray()));
            options.AddPolicy("CanManageSavings", policy => policy.RequireRole(RoleNames.SavingsManageRoles.ToArray()));
            options.AddPolicy("CanJournalPost", policy => policy.RequireRole(RoleNames.JournalPostRoles.ToArray()));
        });

        services.AddHttpContextAccessor();
        services.AddScoped<IClock, SystemClock>();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddSingleton<IStaffSessionStore, MemoryStaffSessionStore>();
        services.AddSingleton<IMfaChallengeStore, MemoryMfaChallengeStore>();
        services.AddScoped<TotpProtector>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IStaffService, StaffService>();
        services.Configure<BackupOptions>(configuration.GetSection(BackupOptions.SectionName));
        services.AddSingleton<IBackupProcess, ProcessBackupRunner>();
        services.AddSingleton<IBackupTaskScheduler, WindowsBackupTaskScheduler>();
        services.AddScoped<IBackupService, BackupService>();
        services.AddScoped<IInstitutionPublicService, InstitutionPublicService>();
        services.AddScoped<IJournalService, JournalService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IMemberService, MemberService>();
        services.AddSingleton<IKycOverrideStore, KycOverrideStore>();
        services.AddScoped<IKycDocumentService, KycDocumentService>();
        services.AddScoped<ISavingsService, SavingsService>();
        services.AddScoped<ISavingsProductService, SavingsProductService>();
        services.AddScoped<ITellerService, TellerService>();
        services.AddScoped<ITreasuryService, TreasuryService>();
        services.AddScoped<ILoanProductService, LoanProductService>();
        services.AddScoped<ICreditPoolService, CreditPoolService>();
        services.AddScoped<ILoanService, LoanService>();

        return services;
    }
}
