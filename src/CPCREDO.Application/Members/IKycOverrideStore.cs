namespace CPCREDO.Application.Members;

public interface IKycOverrideStore
{
    KycOverrideGrant Issue(KycOverrideGrant grant);

    bool TryConsume(
        Guid grantId,
        Guid userId,
        Guid documentId,
        string action,
        DateTime utcNow,
        out KycOverrideGrant grant);
}
