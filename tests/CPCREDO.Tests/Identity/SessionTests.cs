using System.IdentityModel.Tokens.Jwt;
using CPCREDO.Application.Identity;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Infrastructure.Identity;
using CPCREDO.Tests.Accounting;
using Xunit;

namespace CPCREDO.Tests.Identity;

public sealed class SessionTests
{
    [Fact]
    public void Idle_timeout_after_12_minutes_invalidates_session()
    {
        var store = new MemoryStaffSessionStore();
        var clock = new FixedClock();
        var userId = SeedGuids.AdminUserId;
        var sessionId = store.Start(userId, clock.UtcNow);
        var idle = TimeSpan.FromMinutes(12);

        Assert.True(store.TryValidate(sessionId, userId, clock.UtcNow, idle, out var expired));
        Assert.False(expired);

        store.Touch(sessionId, clock.UtcNow);
        clock.UtcNow = clock.UtcNow.AddMinutes(11);
        Assert.True(store.TryValidate(sessionId, userId, clock.UtcNow, idle, out expired));

        clock.UtcNow = clock.UtcNow.AddMinutes(2);
        Assert.False(store.TryValidate(sessionId, userId, clock.UtcNow, idle, out expired));
        Assert.True(expired);
    }

    [Fact]
    public async Task Login_then_logout_ends_server_session()
    {
        using var harness = new StaffHarness();
        var created = await harness.Staff.CreateAsync(new CreateStaffRequest(
            "cashier2",
            "Cashier Two",
            "cashier2@cpcredo.ht",
            "Password!123",
            RoleNames.Caissier));
        Assert.True(created.IsSuccess, created.ErrorMessage);

        var login = await harness.Auth.LoginAsync(new LoginRequest("cashier2", "Password!123"), "127.0.0.1");
        Assert.True(login.IsSuccess, login.ErrorMessage);
        var jti = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(login.Value!.AccessToken).Id);
        Assert.True(harness.Sessions.TryValidate(jti, created.Value!.Id, harness.Clock.UtcNow, TimeSpan.FromMinutes(12), out _));

        await harness.Auth.LogoutAsync(jti, created.Value.Id);
        Assert.False(harness.Sessions.TryValidate(jti, created.Value.Id, harness.Clock.UtcNow, TimeSpan.FromMinutes(12), out _));
    }

    [Fact]
    public async Task Change_password_revokes_server_session()
    {
        using var harness = new StaffHarness();
        var created = await harness.Staff.CreateAsync(new CreateStaffRequest(
            "cashier3",
            "Cashier Three",
            "cashier3@cpcredo.ht",
            "Password!123",
            RoleNames.Caissier));
        Assert.True(created.IsSuccess, created.ErrorMessage);

        var login = await harness.Auth.LoginAsync(new LoginRequest("cashier3", "Password!123"), "127.0.0.1");
        Assert.True(login.IsSuccess, login.ErrorMessage);
        var jti = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(login.Value!.AccessToken).Id);

        var changed = await harness.Auth.ChangePasswordAsync(
            created.Value!.Id,
            new ChangePasswordRequest("Password!123", "Changed!456"));
        Assert.True(changed.IsSuccess, changed.ErrorMessage);
        Assert.False(harness.Sessions.TryValidate(jti, created.Value.Id, harness.Clock.UtcNow, TimeSpan.FromMinutes(12), out _));
    }
}
