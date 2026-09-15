using CPCREDO.Application.Common;
using CPCREDO.Application.Identity;
using CPCREDO.Application.Tenancy;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CPCREDO.Infrastructure.Identity;

public sealed class AuthService : IAuthService
{
    private static readonly string DummyHash =
        new PasswordHasher<User>().HashPassword(new User { Username = "dummy" }, "CpcredoDummyPassword!1");

    private readonly CpcredoDbContext _db;
    private readonly ITokenService _tokens;
    private readonly IAuditLogger _audit;
    private readonly IClock _clock;
    private readonly IInstitutionPublicService _institution;
    private readonly IStaffSessionStore _sessions;
    private readonly IMfaChallengeStore _mfa;
    private readonly TotpProtector _totp;
    private readonly PasswordHasher<User> _hasher = new();

    public AuthService(
        CpcredoDbContext db,
        ITokenService tokens,
        IAuditLogger audit,
        IClock clock,
        IInstitutionPublicService institution,
        IStaffSessionStore sessions,
        IMfaChallengeStore mfa,
        TotpProtector totp)
    {
        _db = db;
        _tokens = tokens;
        _audit = audit;
        _clock = clock;
        _institution = institution;
        _sessions = sessions;
        _mfa = mfa;
        _totp = totp;
    }

    public async Task<Result<LoginResponse>> LoginAsync(
        LoginRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var username = request.Username?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Result<LoginResponse>.Fail("auth.invalid_credentials", "Identifiant ou mot de passe incorrect.");
        }

        var user = await _db.Users
            .Include(u => u.Branch)
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(
                u => u.TenantId == SeedGuids.TenantId && u.Username.ToLower() == username.ToLower(),
                cancellationToken);

        if (user is null)
        {
            _hasher.VerifyHashedPassword(new User { Username = username }, DummyHash, request.Password);
            await _audit.LogAsync(
                "Auth.LoginFailed",
                nameof(User),
                details: new { username },
                tenantId: SeedGuids.TenantId,
                ipAddress: ipAddress,
                cancellationToken: cancellationToken);
            return Result<LoginResponse>.Fail("auth.invalid_credentials", "Identifiant ou mot de passe incorrect.");
        }

        var verify = _hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verify == PasswordVerificationResult.Failed || !user.IsActive)
        {
            await _audit.LogAsync(
                "Auth.LoginFailed",
                nameof(User),
                user.Id,
                new { username = user.Username, active = user.IsActive },
                user.TenantId,
                user.Id,
                ipAddress,
                cancellationToken);
            return Result<LoginResponse>.Fail("auth.invalid_credentials", "Identifiant ou mot de passe incorrect.");
        }

        var roles = user.UserRoles
            .Select(ur => ur.Role!)
            .Where(r => r is not null)
            .ToList();
        var roleNames = roles.Select(r => r.Name).ToList();
        var mfaRequired = user.MfaEnabled || roleNames.Any(r => RoleNames.MfaRequiredRoles.Contains(r));

        if (!user.MustChangePassword && mfaRequired)
        {
            byte[]? pending = null;
            MfaSetupDto? setup = null;
            var setupRequired = !user.MfaEnabled;
            if (setupRequired)
            {
                pending = TotpProtector.NewSecret();
                setup = TotpProtector.BuildSetup(pending, user.Username);
            }

            var ticket = _mfa.Start(user.Id, _clock.UtcNow, pending);
            await _audit.LogAsync(
                setupRequired ? "Mfa.SetupStarted" : "Mfa.Challenge",
                nameof(User),
                user.Id,
                new { user.Username, setupRequired },
                user.TenantId,
                user.Id,
                ipAddress,
                cancellationToken);

            return Result<LoginResponse>.Ok(new LoginResponse(
                null, null, null, MapUser(user), null, null, roles.Select(MapRole).ToList(),
                MfaRequired: true,
                MfaSetupRequired: setupRequired,
                MfaTicket: ticket.ToString(),
                MfaSetup: setup));
        }

        user.LastLoginAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Result<LoginResponse>.Ok(await IssueFullSessionAsync(user, roles, ipAddress, cancellationToken));
    }

    public async Task<Result<LoginResponse>> VerifyMfaAsync(
        MfaVerifyRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(request.Ticket, out var ticket))
            return Result<LoginResponse>.Fail("auth.mfa_invalid", "Code ou session MFA invalide.");

        var challenge = _mfa.Get(ticket, _clock.UtcNow);
        if (challenge is null)
            return Result<LoginResponse>.Fail("auth.mfa_expired", "La session MFA a expiré. Reconnectez-vous.");

        var user = await _db.Users
            .Include(u => u.Branch)
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == challenge.UserId, cancellationToken);
        if (user is null || !user.IsActive)
            return Result<LoginResponse>.Fail("auth.unauthorized", "Session invalide.");

        byte[] secret;
        try
        {
            if (challenge.PendingSecret is { Length: > 0 })
                secret = challenge.PendingSecret;
            else if (!string.IsNullOrEmpty(user.TotpSecretProtected))
                secret = _totp.Unprotect(user.TotpSecretProtected);
            else
                return Result<LoginResponse>.Fail("auth.mfa_invalid", "Code ou session MFA invalide.");
        }
        catch
        {
            return Result<LoginResponse>.Fail("auth.mfa_invalid", "Code ou session MFA invalide.");
        }

        if (!TotpProtector.Verify(secret, request.Code, user.LastTotpTimestep, out var timestep))
        {
            _mfa.RegisterFailure(ticket);
            await _audit.LogAsync("Mfa.Failed", nameof(User), user.Id, new { user.Username }, user.TenantId, user.Id, ipAddress, cancellationToken);
            return Result<LoginResponse>.Fail("auth.mfa_invalid", "Code d’authentification incorrect.");
        }

        if (challenge.PendingSecret is { Length: > 0 })
        {
            user.TotpSecretProtected = _totp.Protect(secret);
            user.MfaEnabled = true;
        }

        user.LastTotpTimestep = timestep;
        user.LastLoginAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        _mfa.Consume(ticket);

        var roles = user.UserRoles.Select(ur => ur.Role!).Where(r => r is not null).ToList();
        await _audit.LogAsync("Mfa.Verified", nameof(User), user.Id, new { user.Username }, user.TenantId, user.Id, ipAddress, cancellationToken);
        return Result<LoginResponse>.Ok(await IssueFullSessionAsync(user, roles, ipAddress, cancellationToken));
    }

    private async Task<LoginResponse> IssueFullSessionAsync(
        User user,
        List<Role> roles,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var sessionId = _sessions.Start(user.Id, _clock.UtcNow);
        var token = _tokens.CreateAccessToken(user, roles.Select(r => r.Name).ToList(), sessionId, out var expiresAtUtc);
        var institution = await _institution.GetAsync(cancellationToken);
        await _audit.LogAsync(
            "Auth.LoginSucceeded",
            nameof(User),
            user.Id,
            new { user.Username },
            user.TenantId,
            user.Id,
            ipAddress,
            cancellationToken);
        return new LoginResponse(
            token,
            expiresAtUtc,
            _clock.ToPortAuPrince(expiresAtUtc),
            MapUser(user),
            institution,
            MapBranch(user),
            roles.Select(MapRole).ToList());
    }

    public async Task<Result<bool>> ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var newPassword = request.NewPassword ?? string.Empty;
        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || newPassword.Length < 10)
            return Result<bool>.Fail("auth.password_invalid", "Le nouveau mot de passe doit contenir au moins 10 caractères.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || !user.IsActive)
            return Result<bool>.Fail("auth.unauthorized", "Session invalide.");

        var verify = _hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword);
        if (verify == PasswordVerificationResult.Failed)
            return Result<bool>.Fail("auth.invalid_credentials", "Mot de passe actuel incorrect.");

        user.PasswordHash = _hasher.HashPassword(user, newPassword);
        user.MustChangePassword = false;
        await _db.SaveChangesAsync(cancellationToken);
        _sessions.RevokeUser(user.Id);
        await _audit.LogAsync("Auth.PasswordChanged", nameof(User), user.Id, new { user.Username }, user.TenantId, user.Id, cancellationToken: cancellationToken);
        return Result<bool>.Ok(true);
    }

    public Task LogoutAsync(Guid? sessionId, Guid? userId, CancellationToken cancellationToken = default)
    {
        if (sessionId is { } sid)
            _sessions.Revoke(sid);
        else if (userId is { } uid)
            _sessions.RevokeUser(uid);
        return Task.CompletedTask;
    }

    public async Task<Result<MeResponse>> GetMeAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .Include(u => u.Branch)
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null || !user.IsActive)
            return Result<MeResponse>.Fail("auth.unauthorized", "Session invalide.");

        var roles = user.UserRoles.Select(ur => ur.Role!).Where(r => r is not null).ToList();
        var institution = await _institution.GetAsync(cancellationToken);

        return Result<MeResponse>.Ok(new MeResponse(
            MapUser(user),
            institution,
            MapBranch(user),
            roles.Select(MapRole).ToList()));
    }

    private static UserDto MapUser(User user) =>
        new(user.Id, user.Username, user.FullName, user.Email, user.BranchId, user.MustChangePassword);

    private static BranchDto MapBranch(User user) =>
        new(
            user.BranchId,
            user.Branch?.Code ?? string.Empty,
            user.Branch?.Name ?? string.Empty,
            user.Branch?.City ?? string.Empty,
            user.Branch?.IsHeadquarters ?? false);

    private static RoleDto MapRole(Role role) =>
        new(role.Name, role.DisplayNameFr, role.DisplayNameHt, role.DisplayNameEn, role.IsReadOnly);
}
