using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CPCREDO.Application.Common;
using CPCREDO.Application.Identity;
using CPCREDO.Domain.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CPCREDO.Infrastructure.Identity;

public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly IClock _clock;

    public JwtTokenService(IOptions<JwtOptions> options, IClock clock)
    {
        _options = options.Value;
        _clock = clock;
    }

    public string CreateAccessToken(User user, IReadOnlyList<string> roles, out DateTime expiresAtUtc)
    {
        var now = _clock.UtcNow;
        expiresAtUtc = now.AddMinutes(_options.ExpiryMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new("tenant_id", user.TenantId.ToString()),
            new("branch_id", user.BranchId.ToString()),
            new("must_change_password", user.MustChangePassword.ToString().ToLowerInvariant())
        };

        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAtUtc,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
