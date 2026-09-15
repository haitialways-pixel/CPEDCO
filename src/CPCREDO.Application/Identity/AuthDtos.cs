using CPCREDO.Application.Tenancy;

namespace CPCREDO.Application.Identity;

public sealed record LoginRequest(string Username, string Password);

public sealed record MfaVerifyRequest(string Ticket, string Code);

public sealed record MfaSetupDto(string ManualKey, string OtpauthUrl, string QrPngDataUrl);

public sealed record ResetMfaRequest(string Password);

public sealed record RoleDto(
    string Name,
    string DisplayNameFr,
    string DisplayNameHt,
    string DisplayNameEn,
    bool IsReadOnly);

public sealed record UserDto(
    Guid Id,
    string Username,
    string FullName,
    string Email,
    Guid BranchId,
    bool MustChangePassword);

public sealed record BranchDto(
    Guid Id,
    string Code,
    string Name,
    string City,
    bool IsHeadquarters);

public sealed record LoginResponse(
    string? AccessToken,
    DateTime? ExpiresAtUtc,
    DateTime? ExpiresAtPortAuPrince,
    UserDto? User,
    InstitutionPublicDto? Institution,
    BranchDto? Branch,
    IReadOnlyList<RoleDto>? Roles,
    bool MfaRequired = false,
    bool MfaSetupRequired = false,
    string? MfaTicket = null,
    MfaSetupDto? MfaSetup = null);

public sealed record MeResponse(
    UserDto User,
    InstitutionPublicDto Institution,
    BranchDto Branch,
    IReadOnlyList<RoleDto> Roles);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record StaffUserDto(
    Guid Id,
    string Username,
    string FullName,
    string Email,
    Guid BranchId,
    bool IsActive,
    bool MustChangePassword,
    IReadOnlyList<RoleDto> Roles,
    bool MfaEnabled = false);

public sealed record CreateStaffRequest(
    string Username,
    string FullName,
    string Email,
    string Password,
    string RoleName,
    Guid? BranchId = null);

public sealed record AssignStaffRoleRequest(string RoleName);

public sealed record ResetStaffPasswordRequest(string NewPassword);

public sealed record StaffDirectoryDto(Guid Id, string Username, string FullName);
