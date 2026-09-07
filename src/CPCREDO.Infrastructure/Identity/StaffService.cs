using CPCREDO.Application.Common;
using CPCREDO.Application.Identity;
using CPCREDO.Domain.Common;
using CPCREDO.Domain.Identity;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CPCREDO.Infrastructure.Identity;

public sealed class StaffService : IStaffService
{
    private readonly CpcredoDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditLogger _audit;
    private readonly PasswordHasher<User> _hasher = new();

    public StaffService(
        CpcredoDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        IAuditLogger audit)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
    }

    public async Task<Result<IReadOnlyList<StaffUserDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var auth = RequireAdmin();
        if (!auth.IsSuccess)
            return Result<IReadOnlyList<StaffUserDto>>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var users = await _db.Users
            .AsNoTracking()
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .Where(u => u.TenantId == _currentUser.TenantId!.Value)
            .OrderBy(u => u.FullName)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<StaffUserDto>>.Ok(users.Select(Map).ToList());
    }

    public async Task<Result<IReadOnlyList<StaffDirectoryDto>>> ListDirectoryAsync(CancellationToken cancellationToken = default)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.TenantId is null)
            return Result<IReadOnlyList<StaffDirectoryDto>>.Fail("auth.unauthorized", "Session invalide.");

        var users = await _db.Users
            .AsNoTracking()
            .Where(u => u.TenantId == _currentUser.TenantId.Value && u.IsActive)
            .OrderBy(u => u.FullName)
            .Select(u => new StaffDirectoryDto(u.Id, u.Username, u.FullName))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<StaffDirectoryDto>>.Ok(users);
    }

    public async Task<Result<StaffUserDto>> CreateAsync(CreateStaffRequest request, CancellationToken cancellationToken = default)
    {
        var auth = RequireAdmin();
        if (!auth.IsSuccess)
            return Result<StaffUserDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var username = request.Username?.Trim() ?? string.Empty;
        var email = request.Email?.Trim() ?? string.Empty;
        var fullName = request.FullName?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email) || password.Length < 8)
            return Result<StaffUserDto>.Fail("staff.invalid", "Les informations du personnel ou le mot de passe sont invalides.");

        var role = await FindRoleAsync(request.RoleName, cancellationToken);
        if (role is null)
            return Result<StaffUserDto>.Fail("staff.role_not_found", "Rôle introuvable.");

        var tenantId = _currentUser.TenantId!.Value;
        if (await _db.Users.AnyAsync(u => u.TenantId == tenantId && u.Username.ToLower() == username.ToLower(), cancellationToken))
            return Result<StaffUserDto>.Fail("staff.duplicate_username", "Cet identifiant existe déjà.");

        var branchId = request.BranchId ?? _currentUser.BranchId;
        if (branchId is null || !await _db.Branches.AnyAsync(b => b.Id == branchId && b.TenantId == tenantId, cancellationToken))
            return Result<StaffUserDto>.Fail("staff.branch_not_found", "Agence introuvable.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId.Value,
            Username = username,
            Email = email,
            FullName = fullName,
            IsActive = true,
            MustChangePassword = true,
            CreatedAtUtc = _clock.UtcNow
        };
        user.PasswordHash = _hasher.HashPassword(user, password);
        user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, User = user, Role = role });
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Staff.Created", nameof(User), user.Id, new { user.Username, role = role.Name }, tenantId, _currentUser.UserId, _currentUser.IpAddress, cancellationToken);

        return Result<StaffUserDto>.Ok(Map(user));
    }

    public async Task<Result<StaffUserDto>> AssignRoleAsync(Guid id, AssignStaffRoleRequest request, CancellationToken cancellationToken = default)
    {
        var auth = RequireAdmin();
        if (!auth.IsSuccess)
            return Result<StaffUserDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var user = await LoadAsync(id, cancellationToken);
        if (user is null)
            return Result<StaffUserDto>.Fail("staff.not_found", "Personnel introuvable.");

        var role = await FindRoleAsync(request.RoleName, cancellationToken);
        if (role is null)
            return Result<StaffUserDto>.Fail("staff.role_not_found", "Rôle introuvable.");

        var hadAdmin = user.UserRoles.Any(ur => ur.Role?.Name == RoleNames.Admin);
        if (hadAdmin && role.Name != RoleNames.Admin && await ActiveAdminCountAsync(user.TenantId, cancellationToken) <= 1)
            return Result<StaffUserDto>.Fail("staff.last_admin", "Le dernier Admin ne peut pas perdre son rôle.");

        user.UserRoles.Clear();
        user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, User = user, Role = role });
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Staff.RoleAssigned", nameof(User), user.Id, new { user.Username, role = role.Name }, user.TenantId, _currentUser.UserId, _currentUser.IpAddress, cancellationToken);
        return Result<StaffUserDto>.Ok(Map(user));
    }

    public async Task<Result<StaffUserDto>> DisableAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var auth = RequireAdmin();
        if (!auth.IsSuccess)
            return Result<StaffUserDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var user = await LoadAsync(id, cancellationToken);
        if (user is null)
            return Result<StaffUserDto>.Fail("staff.not_found", "Personnel introuvable.");

        if (user.IsActive && user.UserRoles.Any(ur => ur.Role?.Name == RoleNames.Admin) && await ActiveAdminCountAsync(user.TenantId, cancellationToken) <= 1)
            return Result<StaffUserDto>.Fail("staff.last_admin", "Le dernier Admin ne peut pas être désactivé.");

        user.IsActive = false;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Staff.Disabled", nameof(User), user.Id, new { user.Username }, user.TenantId, _currentUser.UserId, _currentUser.IpAddress, cancellationToken);
        return Result<StaffUserDto>.Ok(Map(user));
    }

    public async Task<Result<StaffUserDto>> ResetPasswordAsync(Guid id, ResetStaffPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var auth = RequireAdmin();
        if (!auth.IsSuccess)
            return Result<StaffUserDto>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var newPassword = request.NewPassword ?? string.Empty;
        if (newPassword.Length < 8)
            return Result<StaffUserDto>.Fail("staff.password_invalid", "Le nouveau mot de passe doit contenir au moins 8 caractères.");

        var user = await LoadAsync(id, cancellationToken);
        if (user is null)
            return Result<StaffUserDto>.Fail("staff.not_found", "Personnel introuvable.");

        user.PasswordHash = _hasher.HashPassword(user, newPassword);
        user.MustChangePassword = true;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("Staff.PasswordReset", nameof(User), user.Id, new { user.Username }, user.TenantId, _currentUser.UserId, _currentUser.IpAddress, cancellationToken);
        return Result<StaffUserDto>.Ok(Map(user));
    }

    private async Task<User?> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await _db.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == _currentUser.TenantId!.Value, cancellationToken);

    private Task<Role?> FindRoleAsync(string roleName, CancellationToken cancellationToken) =>
        _db.Roles.FirstOrDefaultAsync(r => r.Name == roleName, cancellationToken);

    private Task<int> ActiveAdminCountAsync(Guid tenantId, CancellationToken cancellationToken) =>
        _db.Users.CountAsync(u => u.TenantId == tenantId && u.IsActive && u.UserRoles.Any(ur => ur.Role!.Name == RoleNames.Admin), cancellationToken);

    private Result<bool> RequireAdmin() =>
        _currentUser.IsAuthenticated && _currentUser.TenantId is not null && _currentUser.Roles.Contains(RoleNames.Admin, StringComparer.OrdinalIgnoreCase)
            ? Result<bool>.Ok(true)
            : Result<bool>.Fail(_currentUser.IsAuthenticated ? "auth.forbidden" : "auth.unauthorized", _currentUser.IsAuthenticated ? "Accès réservé aux Admins." : "Session invalide.");

    private static StaffUserDto Map(User user) =>
        new(
            user.Id,
            user.Username,
            user.FullName,
            user.Email,
            user.BranchId,
            user.IsActive,
            user.MustChangePassword,
            user.UserRoles.Where(ur => ur.Role is not null).Select(ur => new RoleDto(
                ur.Role!.Name,
                ur.Role.DisplayNameFr,
                ur.Role.DisplayNameHt,
                ur.Role.DisplayNameEn,
                ur.Role.IsReadOnly)).ToList());
}
