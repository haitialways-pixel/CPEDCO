using CPCREDO.Application.Common;

namespace CPCREDO.Application.Accounting;

public interface IJournalService
{
    Task<Result<JournalDto>> PostAsync(
        CreateJournalRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<Result<JournalDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<JournalDto>> ReverseAsync(
        Guid id,
        ReverseJournalRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<Result<TrialBalanceDto>> GetTrialBalanceAsync(
        DateOnly? asOf,
        string? currencyCode,
        CancellationToken cancellationToken = default);
}
