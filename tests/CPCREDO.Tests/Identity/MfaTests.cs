using CPCREDO.Application.Identity;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using OtpNet;
using Xunit;

namespace CPCREDO.Tests.Identity;

public sealed class MfaTests
{
    [Fact]
    public async Task Caissier_login_skips_mfa()
    {
        using var harness = new StaffHarness();
        var created = await harness.Staff.CreateAsync(new CreateStaffRequest(
            "cashier-mfa",
            "Cashier Mfa",
            "cashier-mfa@cpcredo.ht",
            "Password!123",
            RoleNames.Caissier));
        Assert.True(created.IsSuccess, created.ErrorMessage);
        var user = await harness.Db.Users.FindAsync(created.Value!.Id);
        user!.MustChangePassword = false;
        await harness.Db.SaveChangesAsync();

        var login = await harness.Auth.LoginAsync(new LoginRequest("cashier-mfa", "Password!123"), "127.0.0.1");
        Assert.True(login.IsSuccess, login.ErrorMessage);
        Assert.False(login.Value!.MfaRequired);
        Assert.False(string.IsNullOrEmpty(login.Value.AccessToken));
    }

    [Fact]
    public async Task Gerant_must_setup_then_verify_totp_to_get_token()
    {
        using var harness = new StaffHarness();
        var created = await harness.Staff.CreateAsync(new CreateStaffRequest(
            "gerant-mfa",
            "Gerant Mfa",
            "gerant-mfa@cpcredo.ht",
            "Password!123",
            RoleNames.Gerant));
        Assert.True(created.IsSuccess, created.ErrorMessage);
        var user = await harness.Db.Users.FindAsync(created.Value!.Id);
        user!.MustChangePassword = false;
        await harness.Db.SaveChangesAsync();

        var login = await harness.Auth.LoginAsync(new LoginRequest("gerant-mfa", "Password!123"), "127.0.0.1");
        Assert.True(login.IsSuccess, login.ErrorMessage);
        Assert.True(login.Value!.MfaRequired);
        Assert.True(login.Value.MfaSetupRequired);
        Assert.False(string.IsNullOrEmpty(login.Value.MfaTicket));
        Assert.False(string.IsNullOrEmpty(login.Value.MfaSetup?.ManualKey));
        Assert.StartsWith("data:image/png;base64,", login.Value.MfaSetup!.QrPngDataUrl);
        Assert.True(string.IsNullOrEmpty(login.Value.AccessToken));

        var audits = harness.Db.AuditLogs.Select(a => a.DetailsJson).ToList();
        Assert.DoesNotContain(audits, d => d != null && d.Contains(login.Value.MfaSetup.ManualKey, StringComparison.Ordinal));

        var bad = await harness.Auth.VerifyMfaAsync(new MfaVerifyRequest(login.Value.MfaTicket!, "000000"), "127.0.0.1");
        Assert.False(bad.IsSuccess);
        Assert.Equal("auth.mfa_invalid", bad.ErrorCode);

        var secret = Base32Encoding.ToBytes(login.Value.MfaSetup.ManualKey);
        var code = new Totp(secret, step: 30, totpSize: 6).ComputeTotp();
        var ok = await harness.Auth.VerifyMfaAsync(new MfaVerifyRequest(login.Value.MfaTicket!, code), "127.0.0.1");
        Assert.True(ok.IsSuccess, ok.ErrorMessage);
        Assert.False(string.IsNullOrEmpty(ok.Value!.AccessToken));
        var enabled = await harness.Db.Users.FindAsync(created.Value.Id);
        Assert.True(enabled!.MfaEnabled);
        Assert.False(string.IsNullOrEmpty(enabled.TotpSecretProtected));
        Assert.NotEqual(login.Value.MfaSetup.ManualKey, enabled.TotpSecretProtected);
    }

    [Fact]
    public async Task Admin_reset_mfa_requires_own_password_and_clears_secret()
    {
        using var harness = new StaffHarness();
        var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<User>();
        var admin = await harness.Db.Users.FindAsync(SeedGuids.AdminUserId);
        admin!.PasswordHash = hasher.HashPassword(admin, "AdminPass!12");
        await harness.Db.SaveChangesAsync();

        var created = await harness.Staff.CreateAsync(new CreateStaffRequest(
            "gerant-reset",
            "Gerant Reset",
            "gerant-reset@cpcredo.ht",
            "Password!123",
            RoleNames.Gerant));
        var target = await harness.Db.Users.FindAsync(created.Value!.Id);
        target!.MustChangePassword = false;
        target.MfaEnabled = true;
        target.TotpSecretProtected = "protected-not-a-secret-in-audit";
        await harness.Db.SaveChangesAsync();

        var denied = await harness.Staff.ResetMfaAsync(target.Id, new ResetMfaRequest("wrong"));
        Assert.False(denied.IsSuccess);
        Assert.Equal("auth.invalid_credentials", denied.ErrorCode);

        var reset = await harness.Staff.ResetMfaAsync(target.Id, new ResetMfaRequest("AdminPass!12"));
        Assert.True(reset.IsSuccess, reset.ErrorMessage);
        Assert.False(reset.Value!.MfaEnabled);
        var reloaded = await harness.Db.Users.FindAsync(target.Id);
        Assert.False(reloaded!.MfaEnabled);
        Assert.Null(reloaded.TotpSecretProtected);
        Assert.True(harness.Db.AuditLogs.Any(a => a.Action == "Mfa.Reset"));
        Assert.DoesNotContain(harness.Db.AuditLogs.Select(a => a.DetailsJson), d => d != null && d.Contains("protected-not-a-secret-in-audit"));
    }
}
