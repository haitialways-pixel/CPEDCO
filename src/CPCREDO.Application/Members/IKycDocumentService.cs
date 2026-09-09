using CPCREDO.Application.Common;
using CPCREDO.Domain.Members;

namespace CPCREDO.Application.Members;

public interface IKycDocumentService
{
    Task<Result<IReadOnlyList<KycDocumentDto>>> ListAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task<Result<KycFileResult>> GetFileAsync(
        Guid memberId,
        KycDocumentType type,
        CancellationToken cancellationToken = default);

    Task<Result<KycDocumentDto>> UploadAsync(
        Guid memberId,
        KycDocumentType type,
        Stream content,
        string fileName,
        string contentType,
        long length,
        Guid? overrideGrantId = null,
        CancellationToken cancellationToken = default);

    Task<Result<bool>> DeleteAsync(
        Guid memberId,
        KycDocumentType type,
        Guid? overrideGrantId = null,
        CancellationToken cancellationToken = default);

    Task<Result<KycOverrideAuthResponse>> AuthorizeOverrideAsync(
        Guid documentId,
        KycOverrideAuthRequest request,
        CancellationToken cancellationToken = default);
}
