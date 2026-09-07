using CPCREDO.Application.Common;
using CPCREDO.Application.Reports;

namespace CPCREDO.Application.Treasury;

public interface ITreasuryService
{
    Task<Result<IReadOnlyList<string>>> ListBankNamesAsync(CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<BankAccountDto>>> ListBanksAsync(CancellationToken cancellationToken = default);

    Task<Result<BankAccountDto>> CreateBankAsync(
        SaveBankAccountRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<BankAccountDto>> UpdateBankAsync(
        Guid id,
        SaveBankAccountRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<BankAccountDto>> DeactivateBankAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<VaultBalanceDto>>> ListVaultsAsync(CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<TreasuryTransferDto>>> ListTransfersAsync(CancellationToken cancellationToken = default);

    Task<Result<TreasuryTransferDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<TreasurySlipFileDto>> GetSlipAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<TreasuryTransferDto>> CreateDraftAsync(
        CreateTreasuryTransferRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<TreasuryTransferDto>> AttachSlipAsync(
        Guid id,
        AttachTreasurySlipRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<TreasuryTransferDto>> ApproveAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<TreasuryTransferDto>> ExecuteAsync(
        Guid id,
        ExecuteTreasuryTransferRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<Result<TreasuryTransferDto>> CancelAsync(
        Guid id,
        CancelTreasuryTransferRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<ReportFileDto>> ExportJournalAsync(
        DateOnly? from,
        DateOnly? to,
        string format,
        CancellationToken cancellationToken = default);
}
