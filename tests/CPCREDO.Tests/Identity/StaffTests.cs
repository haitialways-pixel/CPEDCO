using CPCREDO.Application.Identity;
using CPCREDO.Application.Common;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Tenancy;
using CPCREDO.Infrastructure.Audit;
using CPCREDO.Infrastructure.Identity;
using CPCREDO.Infrastructure.Persistence;
using CPCREDO.Infrastructure.Tenancy;
using CPCREDO.Tests.Accounting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace CPCREDO.Tests.Identity;

public sealed class StaffTests
{
    [Fact]
    public async Task Non_admin_cannot_manage_staff()
    {
        using var harness = new StaffHarness();
        harness.User.Roles = [RoleNames.Gerant];

        var result = await harness.Staff.ListAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.forbidden", result.ErrorCode);
    }

    [Fact]
    public async Task Creating_staff_assigns_one_role_and_requires_password_change()
    {
        using var harness = new StaffHarness();

        var result = await harness.Staff.CreateAsync(new CreateStaffRequest(
            "cashier",
            "Cashier Test",
            "cashier@cpcredo.ht",
            "Password!123",
            RoleNames.Caissier));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.Value!.MustChangePassword);
        Assert.Single(result.Value.Roles);
        Assert.Equal(RoleNames.Caissier, result.Value.Roles[0].Name);
        Assert.DoesNotContain("Password!123", result.Value.ToString());
    }

    [Fact]
    public async Task Reset_password_forces_change()
    {
        using var harness = new StaffHarness();
        var created = await harness.Staff.CreateAsync(new CreateStaffRequest(
            "cashier",
            "Cashier Test",
            "cashier@cpcredo.ht",
            "Password!123",
            RoleNames.Caissier));

        var result = await harness.Staff.ResetPasswordAsync(created.Value!.Id, new ResetStaffPasswordRequest("NewPassword!123"));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.Value!.MustChangePassword);
    }

    [Fact]
    public async Task Last_admin_cannot_be_disabled_or_lose_admin_role()
    {
        using var harness = new StaffHarness();

        var disabled = await harness.Staff.DisableAsync(SeedGuids.AdminUserId);
        var reassigned = await harness.Staff.AssignRoleAsync(SeedGuids.AdminUserId, new AssignStaffRoleRequest(RoleNames.Gerant));

        Assert.Equal("staff.last_admin", disabled.ErrorCode);
        Assert.Equal("staff.last_admin", reassigned.ErrorCode);
    }

    [Fact]
    public async Task Creating_staff_does_not_create_a_member()
    {
        using var harness = new StaffHarness();
        var membersBefore = harness.Db.Members.Count();

        var result = await harness.Staff.CreateAsync(new CreateStaffRequest(
            "cashier",
            "Cashier Test",
            "cashier@cpcredo.ht",
            "Password!123",
            RoleNames.Caissier));

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(membersBefore, harness.Db.Members.Count());
        Assert.True(await harness.Db.Users.AnyAsync(u => u.Username == "cashier"));
    }

    [Fact]
    public async Task Directory_is_visible_to_non_admin()
    {
        using var harness = new StaffHarness();
        harness.User.Roles = [RoleNames.ServiceClient];

        var result = await harness.Staff.ListDirectoryAsync();

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains(result.Value!, u => u.Username == "admin");
    }

    [Fact]
    public async Task Change_password_clears_must_change_flag()
    {
        using var harness = new StaffHarness();
        var created = await harness.Staff.CreateAsync(new CreateStaffRequest(
            "cashier",
            "Cashier Test",
            "cashier@cpcredo.ht",
            "Password!123",
            RoleNames.Caissier));

        var changed = await harness.Auth.ChangePasswordAsync(
            created.Value!.Id,
            new ChangePasswordRequest("Password!123", "Changed!456"));

        Assert.True(changed.IsSuccess, changed.ErrorMessage);
        var listed = await harness.Staff.ListAsync();
        Assert.False(listed.Value!.Single(u => u.Id == created.Value.Id).MustChangePassword);
    }
}

internal sealed class StaffHarness : IDisposable
{
    public CpcredoDbContext Db { get; }
    public StaffService Staff { get; }
    public AuthService Auth { get; }
    public TestCurrentUser User { get; }

    public StaffHarness()
    {
        var options = new DbContextOptionsBuilder<CpcredoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        Db = new CpcredoDbContext(options);
        Db.Database.EnsureCreated();

        var now = new DateTime(2026, 1, 2, 13, 0, 0, DateTimeKind.Utc);
        Db.Tenants.Add(new Tenant
        {
            Id = SeedGuids.TenantId,
            Sigle = Letterhead.Sigle,
            LegalName = Letterhead.LegalName,
            City = Letterhead.City,
            Country = Letterhead.Country,
            CreatedAtUtc = now
        });
        Db.Branches.Add(new Branch
        {
            Id = SeedGuids.BranchId,
            TenantId = SeedGuids.TenantId,
            Code = Letterhead.DefaultBranchCode,
            Name = Letterhead.DefaultBranchName,
            City = Letterhead.City,
            Country = Letterhead.Country,
            IsHeadquarters = true,
            CreatedAtUtc = now
        });
        var adminRole = Role(RoleNames.Admin, "Administrateur");
        var managerRole = Role(RoleNames.Gerant, "Gérant");
        var cashierRole = Role(RoleNames.Caissier, "Caissier");
        Db.Roles.AddRange(adminRole, managerRole, cashierRole);
        Db.Users.Add(new User
        {
            Id = SeedGuids.AdminUserId,
            TenantId = SeedGuids.TenantId,
            BranchId = SeedGuids.BranchId,
            Username = "admin",
            Email = "admin@cpcredo.ht",
            FullName = "Administrateur CPCREDO",
            PasswordHash = "hash",
            IsActive = true,
            CreatedAtUtc = now,
            UserRoles = [new UserRole { UserId = SeedGuids.AdminUserId, RoleId = adminRole.Id, Role = adminRole }]
        });
        Db.SaveChanges();

        User = new TestCurrentUser();
        var clock = new FixedClock();
        var audit = new AuditLogger(Db, clock, User);
        Staff = new StaffService(Db, User, clock, audit);
        var jwt = Options.Create(new JwtOptions
        {
            Issuer = "CPCREDO",
            Audience = "CPCREDO.Staff",
            Secret = "unit-test-secret-key-32-chars-min!",
            ExpiryMinutes = 60
        });
        Auth = new AuthService(Db, new JwtTokenService(jwt, clock), audit, clock, new InstitutionPublicService(Db));
    }

    public void Dispose() => Db.Dispose();

    private static Role Role(string name, string label) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        DisplayNameFr = label,
        DisplayNameHt = label,
        DisplayNameEn = label,
        DescriptionFr = label,
        CreatedAtUtc = new DateTime(2026, 1, 2, 13, 0, 0, DateTimeKind.Utc)
    };
}
