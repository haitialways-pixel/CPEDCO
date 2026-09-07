using CPCREDO.Application.Common;

namespace CPCREDO.Application.Identity;

public interface IAuthService
{
    Task<Result<LoginResponse>> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    Task<Result<MeResponse>> GetMeAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result<bool>> ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default);
}
