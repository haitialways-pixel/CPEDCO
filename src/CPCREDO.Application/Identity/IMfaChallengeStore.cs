namespace CPCREDO.Application.Identity;

public sealed record MfaChallenge(
    Guid Ticket,
    Guid UserId,
    DateTime ExpiresUtc,
    byte[]? PendingSecret,
    int Failures);

public interface IMfaChallengeStore
{
    Guid Start(Guid userId, DateTime utcNow, byte[]? pendingSecret);
    MfaChallenge? Get(Guid ticket, DateTime utcNow);
    void RegisterFailure(Guid ticket);
    void Consume(Guid ticket);
}
