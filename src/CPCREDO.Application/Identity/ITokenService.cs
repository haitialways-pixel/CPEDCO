using CPCREDO.Domain.Identity;

namespace CPCREDO.Application.Identity;

public interface ITokenService
{
    string CreateAccessToken(User user, IReadOnlyList<string> roles, out DateTime expiresAtUtc);
}
