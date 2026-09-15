using CPCREDO.Domain.Identity;

namespace CPCREDO.Application.Identity;

public interface ITokenService
{
    string CreateAccessToken(User user, IReadOnlyList<string> roles, Guid sessionId, out DateTime expiresAtUtc);
}
