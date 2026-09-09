using CPCREDO.Application.Common;
using CPCREDO.Application.Members;
using CPCREDO.Domain.Identity;
using CPCREDO.Domain.Members;
using CPCREDO.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CPCREDO.Infrastructure.Members;

public sealed class KycDocumentService : IKycDocumentService
{
    public const long MaxBytes = 5 * 1024 * 1024;

    private static readonly HashSet<string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/jpg",
        "image/pjpeg",
        "image/png"
    };

    private static readonly string DummyHash =
        new PasswordHasher<User>().HashPassword(new User { Username = "dummy" }, "CpcredoDummyPassword!1");

    private readonly CpcredoDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IAuditLogger _audit;
    private readonly IKycOverrideStore _overrides;
    private readonly PasswordHasher<User> _hasher = new();
    private readonly string _root;

    public KycDocumentService(
        CpcredoDbContext db,
        ICurrentUser currentUser,
        IClock clock,
        IAuditLogger audit,
        IOptions<KycStorageOptions> options,
        IKycOverrideStore overrides)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _overrides = overrides;
        _root = string.IsNullOrWhiteSpace(options.Value.RootPath)
            ? Path.Combine("data", "kyc")
            : options.Value.RootPath;
    }

    public async Task<Result<IReadOnlyList<KycDocumentDto>>> ListAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<IReadOnlyList<KycDocumentDto>>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var member = await FindMemberAsync(memberId, cancellationToken);
        if (member is null)
            return Result<IReadOnlyList<KycDocumentDto>>.Fail("member.not_found", "Membre introuvable.");

        var rows = await _db.KycDocuments.AsNoTracking()
            .Where(d => d.TenantId == member.TenantId && d.MemberId == member.Id)
            .OrderBy(d => d.Type)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<KycDocumentDto>>.Ok(await MapManyAsync(rows, cancellationToken));
    }

    public async Task<Result<KycFileResult>> GetFileAsync(
        Guid memberId,
        KycDocumentType type,
        CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<KycFileResult>.Fail(auth.ErrorCode!, auth.ErrorMessage!);

        var member = await FindMemberAsync(memberId, cancellationToken);
        if (member is null)
            return Result<KycFileResult>.Fail("member.not_found", "Membre introuvable.");

        var doc = await _db.KycDocuments.AsNoTracking()
            .FirstOrDefaultAsync(
                d => d.TenantId == member.TenantId && d.MemberId == member.Id && d.Type == type,
                cancellationToken);
        if (doc is null)
            return Result<KycFileResult>.Fail("kyc.not_found", "Pièce introuvable.");

        var full = ResolveFullPath(doc.FilePath);
        if (full is null || !File.Exists(full))
            return Result<KycFileResult>.Fail("kyc.not_found", "Fichier introuvable.");

        return Result<KycFileResult>.Ok(new KycFileResult(full, doc.ContentType, Path.GetFileName(full)));
    }

    public async Task<Result<KycDocumentDto>> UploadAsync(
        Guid memberId,
        KycDocumentType type,
        Stream content,
        string fileName,
        string contentType,
        long length,
        Guid? overrideGrantId = null,
        CancellationToken cancellationToken = default)
    {
        var session = RequireUser();
        if (!session.IsSuccess)
            return Result<KycDocumentDto>.Fail(session.ErrorCode!, session.ErrorMessage!);

        var member = await FindMemberAsync(memberId, cancellationToken);
        if (member is null)
            return Result<KycDocumentDto>.Fail("member.not_found", "Membre introuvable.");

        var existing = await _db.KycDocuments
            .FirstOrDefaultAsync(
                d => d.TenantId == member.TenantId && d.MemberId == member.Id && d.Type == type,
                cancellationToken);

        KycOverrideGrant? grant = null;
        if (existing is null)
        {
            var gate = RequireUploader();
            if (!gate.IsSuccess)
                return Result<KycDocumentDto>.Fail(gate.ErrorCode!, gate.ErrorMessage!);
        }
        else
        {
            var change = AuthorizeFilledChange(existing, "replace", overrideGrantId);
            if (!change.IsSuccess)
                return Result<KycDocumentDto>.Fail(change.ErrorCode!, change.ErrorMessage!);
            grant = change.Value;
        }

        if (length <= 0)
            return Result<KycDocumentDto>.Fail("kyc.file_required", "Joignez un fichier.");
        if (length > MaxBytes)
            return Result<KycDocumentDto>.Fail("kyc.too_large", "Fichier trop volumineux (max. 5 Mo).");

        var extension = NormalizeExtension(fileName, contentType);
        var validation = ValidateSlot(type, extension, contentType);
        if (validation is not null)
            return validation;

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length == 0)
            return Result<KycDocumentDto>.Fail("kyc.file_required", "Joignez un fichier.");
        if (buffer.Length > MaxBytes)
            return Result<KycDocumentDto>.Fail("kyc.too_large", "Fichier trop volumineux (max. 5 Mo).");
        if (!HasMagic(buffer, extension))
            return Result<KycDocumentDto>.Fail("kyc.content_type", "Le contenu du fichier ne correspond pas au format annoncé.");

        var processed = KycImageProcessor.Process(type, buffer.ToArray(), extension);
        if (!processed.IsSuccess)
            return Result<KycDocumentDto>.Fail(processed.ErrorCode!, processed.ErrorMessage!);
        extension = processed.Value.Extension;
        var payload = processed.Value.Bytes;
        var mime = processed.Value.ContentType;

        var storedName = StoredFileName(type, extension);
        var relative = $"{member.Id:D}/{storedName}";
        var full = Path.GetFullPath(Path.Combine(_root, relative));
        var rootFull = Path.GetFullPath(_root);
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            return Result<KycDocumentDto>.Fail("kyc.path", "Chemin de fichier invalide.");

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        var replacing = existing is not null;
        if (existing is not null)
            DeleteStoredFile(existing.FilePath);

        DeleteSiblingFiles(member.Id, type, storedName);

        await File.WriteAllBytesAsync(full, payload, cancellationToken);

        var now = _clock.UtcNow;
        if (existing is null)
        {
            existing = new KycDocument
            {
                Id = Guid.NewGuid(),
                TenantId = member.TenantId,
                MemberId = member.Id,
                Type = type,
                FilePath = relative.Replace('\\', '/'),
                ContentType = mime,
                UploadedAtUtc = now,
                UploadedBy = _currentUser.UserId!.Value
            };
            _db.KycDocuments.Add(existing);
        }
        else
        {
            existing.FilePath = relative.Replace('\\', '/');
            existing.ContentType = mime;
            existing.UploadedAtUtc = now;
            existing.UploadedBy = _currentUser.UserId!.Value;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(
            replacing ? "KycDocument.Replaced" : "KycDocument.Uploaded",
            nameof(KycDocument),
            existing.Id,
            new
            {
                type = type.ToString(),
                fileName = storedName,
                contentType = mime,
                memberId = member.Id,
                slot = type.ToString(),
                actor = _currentUser.Username,
                authorizedBy = grant?.AuthorizedByUsername,
                authorizedByUserId = grant?.AuthorizedByUserId
            },
            member.TenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);

        var mapped = await MapManyAsync([existing], cancellationToken);
        return Result<KycDocumentDto>.Ok(mapped[0]);
    }

    public async Task<Result<bool>> DeleteAsync(
        Guid memberId,
        KycDocumentType type,
        Guid? overrideGrantId = null,
        CancellationToken cancellationToken = default)
    {
        var session = RequireUser();
        if (!session.IsSuccess)
            return Result<bool>.Fail(session.ErrorCode!, session.ErrorMessage!);

        var member = await FindMemberAsync(memberId, cancellationToken);
        if (member is null)
            return Result<bool>.Fail("member.not_found", "Membre introuvable.");

        var existing = await _db.KycDocuments
            .FirstOrDefaultAsync(
                d => d.TenantId == member.TenantId && d.MemberId == member.Id && d.Type == type,
                cancellationToken);
        if (existing is null)
            return Result<bool>.Fail("kyc.not_found", "Pièce introuvable.");

        var change = AuthorizeFilledChange(existing, "delete", overrideGrantId);
        if (!change.IsSuccess)
            return Result<bool>.Fail(change.ErrorCode!, change.ErrorMessage!);

        var path = existing.FilePath;
        var id = existing.Id;
        var slot = existing.Type.ToString();
        _db.KycDocuments.Remove(existing);
        await _db.SaveChangesAsync(cancellationToken);
        DeleteStoredFile(path);

        await _audit.LogAsync(
            "KycDocument.Deleted",
            nameof(KycDocument),
            id,
            new
            {
                type = slot,
                filePath = path,
                memberId = member.Id,
                slot,
                actor = _currentUser.Username,
                authorizedBy = change.Value?.AuthorizedByUsername,
                authorizedByUserId = change.Value?.AuthorizedByUserId
            },
            member.TenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);

        return Result<bool>.Ok(true);
    }

    public async Task<Result<KycOverrideAuthResponse>> AuthorizeOverrideAsync(
        Guid documentId,
        KycOverrideAuthRequest request,
        CancellationToken cancellationToken = default)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<KycOverrideAuthResponse>.Fail(auth.ErrorCode!, auth.ErrorMessage!);
        if (IsManager())
            return Result<KycOverrideAuthResponse>.Fail("kyc.override_unnecessary", "Le gérant ou l’administrateur peut modifier sans autorisation.");
        if (!_currentUser.Roles.Any(r => RoleNames.KycUploadRoles.Contains(r)))
            return Result<KycOverrideAuthResponse>.Fail("auth.forbidden", "Consultation uniquement.");

        var action = (request.Action ?? string.Empty).Trim().ToLowerInvariant();
        if (action is not "replace" and not "delete")
            return Result<KycOverrideAuthResponse>.Fail("kyc.override_action", "Action invalide. Utilisez replace ou delete.");

        var doc = await _db.KycDocuments.AsNoTracking()
            .FirstOrDefaultAsync(
                d => d.Id == documentId && d.TenantId == _currentUser.TenantId,
                cancellationToken);
        if (doc is null)
            return Result<KycOverrideAuthResponse>.Fail("kyc.not_found", "Pièce introuvable.");

        var username = request.Username?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(request.Password))
        {
            _hasher.VerifyHashedPassword(new User { Username = username }, DummyHash, request.Password ?? string.Empty);
            return Result<KycOverrideAuthResponse>.Fail("auth.invalid_credentials", "Identifiant ou mot de passe incorrect.");
        }

        var manager = await _db.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(
                u => u.TenantId == _currentUser.TenantId && u.Username.ToLower() == username.ToLower(),
                cancellationToken);

        if (manager is null || !manager.IsActive)
        {
            _hasher.VerifyHashedPassword(new User { Username = username }, DummyHash, request.Password);
            return Result<KycOverrideAuthResponse>.Fail("auth.invalid_credentials", "Identifiant ou mot de passe incorrect.");
        }

        var verify = _hasher.VerifyHashedPassword(manager, manager.PasswordHash, request.Password);
        var isManagerRole = manager.UserRoles.Any(ur =>
            ur.Role is not null && RoleNames.KycManageRoles.Contains(ur.Role.Name));
        if (verify == PasswordVerificationResult.Failed || !isManagerRole)
            return Result<KycOverrideAuthResponse>.Fail("auth.invalid_credentials", "Identifiant ou mot de passe incorrect.");

        var expires = _clock.UtcNow.AddMinutes(5);
        var grant = _overrides.Issue(new KycOverrideGrant(
            Guid.NewGuid(),
            _currentUser.UserId!.Value,
            doc.Id,
            action,
            manager.Id,
            manager.Username,
            expires));

        await _audit.LogAsync(
            "KycDocument.OverrideAuthorized",
            nameof(KycDocument),
            doc.Id,
            new
            {
                actor = _currentUser.Username,
                actorUserId = _currentUser.UserId,
                authorizedBy = manager.Username,
                authorizedByUserId = manager.Id,
                action,
                memberId = doc.MemberId,
                slot = doc.Type.ToString(),
                at = _clock.UtcNow
            },
            doc.TenantId,
            _currentUser.UserId,
            cancellationToken: cancellationToken);

        return Result<KycOverrideAuthResponse>.Ok(
            new KycOverrideAuthResponse(grant.GrantId, doc.Id, action, expires));
    }

    private Result<bool> RequireUser()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.TenantId is null || _currentUser.UserId is null)
            return Result<bool>.Fail("auth.unauthorized", "Session invalide.");
        return Result<bool>.Ok(true);
    }

    private Result<bool> RequireUploader()
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return auth;
        if (!_currentUser.Roles.Any(r => RoleNames.KycUploadRoles.Contains(r)))
            return Result<bool>.Fail("auth.forbidden", "Vous ne pouvez pas joindre de pièces.");
        return Result<bool>.Ok(true);
    }

    private bool IsManager() =>
        _currentUser.Roles.Any(r => RoleNames.KycManageRoles.Contains(r));

    private Result<KycOverrideGrant?> AuthorizeFilledChange(
        KycDocument existing,
        string action,
        Guid? overrideGrantId)
    {
        var auth = RequireUser();
        if (!auth.IsSuccess)
            return Result<KycOverrideGrant?>.Fail(auth.ErrorCode!, auth.ErrorMessage!);
        if (IsManager())
            return Result<KycOverrideGrant?>.Ok(null);
        if (!_currentUser.Roles.Any(r => RoleNames.KycUploadRoles.Contains(r)))
            return Result<KycOverrideGrant?>.Fail("auth.forbidden", "Consultation uniquement.");
        if (overrideGrantId is Guid grantId
            && _overrides.TryConsume(
                grantId,
                _currentUser.UserId!.Value,
                existing.Id,
                action,
                _clock.UtcNow,
                out var grant))
            return Result<KycOverrideGrant?>.Ok(grant);

        return Result<KycOverrideGrant?>.Fail("kyc.locked", "Verrouillé — demandez au gérant");
    }

    private Task<Member?> FindMemberAsync(Guid memberId, CancellationToken cancellationToken) =>
        _db.Members.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == memberId && m.TenantId == _currentUser.TenantId, cancellationToken);

    private async Task<IReadOnlyList<KycDocumentDto>> MapManyAsync(
        IReadOnlyList<KycDocument> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
            return [];

        var ids = rows.Select(r => r.UploadedBy).Distinct().ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        return rows.Select(r => new KycDocumentDto(
            r.Id,
            r.Type.ToString(),
            r.ContentType,
            Path.GetFileName(r.FilePath.Replace('\\', '/')),
            r.UploadedAtUtc,
            r.UploadedBy,
            names.GetValueOrDefault(r.UploadedBy, string.Empty))).ToList();
    }

    private static Result<KycDocumentDto>? ValidateSlot(KycDocumentType type, string extension, string contentType)
    {
        var image = extension is "jpg" or "png";
        var pdf = extension == "pdf";
        if (type is KycDocumentType.Photo or KycDocumentType.Signature)
        {
            if (!image || pdf)
                return Result<KycDocumentDto>.Fail("kyc.content_type", "La photo et la signature acceptent JPG ou PNG uniquement.");
            if (!ImageTypes.Contains(contentType) && contentType.Length > 0)
                return Result<KycDocumentDto>.Fail("kyc.content_type", "La photo et la signature acceptent JPG ou PNG uniquement.");
            return null;
        }

        if (!image || pdf)
            return Result<KycDocumentDto>.Fail("kyc.content_type", "La pièce d’identité accepte JPG ou PNG.");
        if (contentType.Length > 0 && !ImageTypes.Contains(contentType))
            return Result<KycDocumentDto>.Fail("kyc.content_type", "La pièce d’identité accepte JPG ou PNG.");
        return null;
    }

    private static string NormalizeExtension(string fileName, string contentType)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        if (ext is "jpeg" or "jpe")
            ext = "jpg";
        if (ext is "jpg" or "png" or "pdf")
            return ext;
        if (ImageTypes.Contains(contentType))
            return contentType.Contains("png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpg";
        return ext;
    }

    private static string StoredFileName(KycDocumentType type, string extension) => type switch
    {
        KycDocumentType.Photo => $"photo.{extension}",
        KycDocumentType.IdFront => $"id-front.{extension}",
        KycDocumentType.IdBack => $"id-back.{extension}",
        KycDocumentType.Signature => $"signature.{extension}",
        _ => $"file.{extension}"
    };

    private static bool HasMagic(MemoryStream buffer, string extension)
    {
        buffer.Position = 0;
        Span<byte> header = stackalloc byte[8];
        var read = buffer.Read(header);
        buffer.Position = 0;
        if (read < 3)
            return false;
        return extension switch
        {
            "jpg" => header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            "png" => read >= 8
                && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
                && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A,
            "pdf" => read >= 4 && header[0] == (byte)'%' && header[1] == (byte)'P' && header[2] == (byte)'D' && header[3] == (byte)'F',
            _ => false
        };
    }

    private string? ResolveFullPath(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains("..", StringComparison.Ordinal))
            return null;
        var full = Path.GetFullPath(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var rootFull = Path.GetFullPath(_root);
        return full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private void DeleteStoredFile(string relative)
    {
        var full = ResolveFullPath(relative);
        if (full is not null && File.Exists(full))
            File.Delete(full);
    }

    private void DeleteSiblingFiles(Guid memberId, KycDocumentType type, string keepName)
    {
        var prefix = type switch
        {
            KycDocumentType.Photo => "photo.",
            KycDocumentType.IdFront => "id-front.",
            KycDocumentType.IdBack => "id-back.",
            KycDocumentType.Signature => "signature.",
            _ => null
        };
        if (prefix is null)
            return;
        var dir = Path.GetFullPath(Path.Combine(_root, memberId.ToString("D")));
        if (!Directory.Exists(dir))
            return;
        foreach (var file in Directory.GetFiles(dir, prefix + "*"))
        {
            if (!string.Equals(Path.GetFileName(file), keepName, StringComparison.OrdinalIgnoreCase))
                File.Delete(file);
        }
    }
}
