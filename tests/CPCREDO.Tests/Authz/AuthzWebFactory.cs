using System.Net.Http.Json;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Tenancy;
using CPCREDO.Application.Identity;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CPCREDO.Tests.Authz;

public sealed class AuthzWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Secret", "CPCREDO-dev-change-me-32chars-minimum-key!");
        builder.UseSetting("Jwt:Issuer", "CPCREDO");
        builder.UseSetting("Jwt:Audience", "CPCREDO.Staff");
        builder.UseSetting("Seed:Enabled", "false");
        builder.UseSetting("ConnectionStrings:Default", "Host=127.0.0.1;Port=5432;Database=cpcredo_test;Username=t;Password=t");
        builder.ConfigureTestServices(services =>
        {
            var remove = services.Where(d =>
                    d.ServiceType == typeof(CpcredoDbContext)
                    || d.ServiceType == typeof(DbContextOptions<CpcredoDbContext>)
                    || (d.ServiceType.IsGenericType
                        && d.ServiceType.GetGenericArguments().Any(a => a == typeof(CpcredoDbContext))))
                .ToList();
            foreach (var d in remove)
                services.Remove(d);

            services.AddDbContext<CpcredoDbContext>(o =>
                o.UseInMemoryDatabase("role-matrix").UseSnakeCaseNamingConvention());
        });
    }

    public async Task<string> LoginCaissierAsync(HttpClient client)
    {
        Seed();
        Services.GetRequiredService<IStaffSessionStore>().RevokeUser(SeedGuids.CaissierUserId);
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "caissier", password = "Caissier!1234" });
        response.EnsureSuccessStatusCode();
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("accessToken").GetString()!;
    }

    public async Task<string> LoginServiceClientAsync(HttpClient client)
    {
        Seed();
        Services.GetRequiredService<IStaffSessionStore>().RevokeUser(Guid.Parse("0c0ec0de-0001-4000-a000-000000000013"));
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "service", password = "Service!1234" });
        response.EnsureSuccessStatusCode();
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("accessToken").GetString()!;
    }

    private void Seed()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CpcredoDbContext>();
        db.Database.EnsureCreated();
        if (db.Users.Any())
            return;

        var now = DateTime.UtcNow;
        var tenant = new Tenant
        {
            Id = SeedGuids.TenantId,
            Sigle = Letterhead.Sigle,
            LegalName = Letterhead.LegalName,
            City = Letterhead.City,
            Country = Letterhead.Country,
            CreatedAtUtc = now
        };
        var branch = new Branch
        {
            Id = SeedGuids.BranchId,
            TenantId = tenant.Id,
            Code = Letterhead.DefaultBranchCode,
            Name = Letterhead.DefaultBranchName,
            City = Letterhead.City,
            Country = Letterhead.Country,
            IsHeadquarters = true,
            CreatedAtUtc = now
        };
        db.Tenants.Add(tenant);
        db.Branches.Add(branch);
        db.Roles.AddRange(
            Role(SeedGuids.RoleAdmin, RoleNames.Admin, "Administrateur"),
            Role(SeedGuids.RoleGerant, RoleNames.Gerant, "Gérant"),
            Role(SeedGuids.RoleCaissier, RoleNames.Caissier, "Caissier"),
            Role(SeedGuids.RoleOfficierCredit, RoleNames.OfficierCredit, "Officier"),
            Role(SeedGuids.RoleServiceClient, RoleNames.ServiceClient, "Service client"),
            Role(SeedGuids.RoleCommissaire, RoleNames.Commissaire, "Commissaire"));
        db.SaveChanges();
        tenant.DefaultBranchId = branch.Id;

        var hasher = new PasswordHasher<User>();
        var caissier = User(SeedGuids.CaissierUserId, "caissier", "Caissier Test", "caissier@cpcredo.ht", now);
        caissier.PasswordHash = hasher.HashPassword(caissier, "Caissier!1234");
        caissier.MustChangePassword = false;
        caissier.UserRoles.Add(new UserRole { UserId = caissier.Id, RoleId = SeedGuids.RoleCaissier });
        var service = User(Guid.Parse("0c0ec0de-0001-4000-a000-000000000013"), "service", "Service Test", "service@cpcredo.ht", now);
        service.PasswordHash = hasher.HashPassword(service, "Service!1234");
        service.MustChangePassword = false;
        service.UserRoles.Add(new UserRole { UserId = service.Id, RoleId = SeedGuids.RoleServiceClient });
        db.Users.AddRange(caissier, service);
        db.SaveChanges();
    }

    private static Role Role(Guid id, string name, string label) => new()
    {
        Id = id,
        Name = name,
        DisplayNameFr = label,
        DisplayNameHt = label,
        DisplayNameEn = label,
        IsSystem = true,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static User User(Guid id, string username, string fullName, string email, DateTime now) => new()
    {
        Id = id,
        TenantId = SeedGuids.TenantId,
        BranchId = SeedGuids.BranchId,
        Username = username,
        FullName = fullName,
        Email = email,
        IsActive = true,
        CreatedAtUtc = now
    };
}
