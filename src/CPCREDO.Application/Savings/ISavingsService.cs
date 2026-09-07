using CPCREDO.Application.Common;

namespace CPCREDO.Application.Savings;

public interface ISavingsService
{
    Task<Result<IReadOnlyList<SavingsProductDto>>> ListProductsAsync(CancellationToken cancellationToken = default);

    Task<Result<SavingsAccountDto>> OpenAccountAsync(Guid memberId, Guid productId, CancellationToken cancellationToken = default);

    Task<Result<SavingsAccountDto>> GetAccountAsync(Guid accountId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<SavingsAccountDto>>> ListMemberAccountsAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task<Result<SavingsStatementDto>> GetStatementAsync(
        Guid accountId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    Task<Result<StatementPdfDto>> GetStatementPdfAsync(
        Guid accountId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    Task<Result<SavingsAccountDto>> PlaceHoldAsync(
        Guid accountId,
        PlaceHoldRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<SavingsAccountDto>> ReleaseHoldAsync(
        Guid accountId,
        Guid holdId,
        CancellationToken cancellationToken = default);

    Task<Result<SavingsAccountDto>> BlockAccountAsync(
        Guid accountId,
        BlockAccountRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<SavingsAccountDto>> UnblockAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default);
}
