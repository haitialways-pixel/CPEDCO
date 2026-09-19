using CPCREDO.Application.Common;

namespace CPCREDO.Application.Teller;

public interface ITellerService
{
    Task<Result<TillSessionDto>> OpenAsync(OpenTillRequest request, CancellationToken cancellationToken = default);

    Task<Result<TillSessionDto>> GetCurrentAsync(string? currencyCode, CancellationToken cancellationToken = default);

    Task<Result<TillSessionDto>> CloseAsync(
        Guid tillId,
        CloseTillRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<Result<CashPostResultDto>> DepositAsync(
        CashPostRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<Result<CashPostResultDto>> WithdrawAsync(
        CashPostRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<Result<CashPostResultDto>> CollectMixedAsync(
        MixedCollectRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<OpenTillPeerDto>>> ListOpenTillsAsync(
        string? currencyCode,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<InternalCashMovementDto>>> ListInternalMovementsAsync(
        string? currencyCode,
        CancellationToken cancellationToken = default);

    Task<Result<InternalCashMovementDto>> CreateInternalMovementAsync(
        CreateInternalCashRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<Result<InternalCashMovementDto>> AcceptInternalMovementAsync(
        Guid movementId,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);
}
