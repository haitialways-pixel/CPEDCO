using CPCREDO.Application.Common;

namespace CPCREDO.Application.Identity;

public interface IStaffService
{
    Task<Result<IReadOnlyList<StaffUserDto>>> ListAsync(CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<StaffDirectoryDto>>> ListDirectoryAsync(CancellationToken cancellationToken = default);

    Task<Result<StaffUserDto>> CreateAsync(
        CreateStaffRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<StaffUserDto>> AssignRoleAsync(
        Guid id,
        AssignStaffRoleRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<StaffUserDto>> DisableAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<StaffUserDto>> ResetPasswordAsync(
        Guid id,
        ResetStaffPasswordRequest request,
        CancellationToken cancellationToken = default);
}