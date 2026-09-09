using CPCREDO.Domain.Members;

namespace CPCREDO.Application.Members;

public sealed class KycStorageOptions
{
    public const string SectionName = "KycStorage";
    public string RootPath { get; set; } = "data/kyc";
}

public sealed record KycDocumentDto(
    Guid Id,
    string Type,
    string ContentType,
    string FileName,
    DateTime UploadedAtUtc,
    Guid UploadedBy,
    string UploadedByName);

public sealed record KycFileResult(
    string FullPath,
    string ContentType,
    string FileName);

public sealed class KycOverrideAuthRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
}

public sealed record KycOverrideAuthResponse(
    Guid GrantId,
    Guid DocumentId,
    string Action,
    DateTime ExpiresAtUtc);

public sealed record KycOverrideGrant(
    Guid GrantId,
    Guid UserId,
    Guid DocumentId,
    string Action,
    Guid AuthorizedByUserId,
    string AuthorizedByUsername,
    DateTime ExpiresAtUtc);
