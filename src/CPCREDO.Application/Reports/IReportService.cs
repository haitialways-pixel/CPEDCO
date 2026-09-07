using CPCREDO.Application.Accounting;
using CPCREDO.Application.Common;

namespace CPCREDO.Application.Reports;

public interface IReportService
{
    Task<Result<TellerCashProofDto>> GetTellerCashProofAsync(
        DateOnly? date,
        string? currencyCode,
        CancellationToken cancellationToken = default);

    Task<Result<TrialBalanceDto>> GetTrialBalanceAsync(
        DateOnly? asOf,
        string? currencyCode,
        CancellationToken cancellationToken = default);

    Task<Result<FinancialStatementDto>> GetFinancialsAsync(
        DateOnly? from,
        DateOnly? asOf,
        string? currencyCode,
        CancellationToken cancellationToken = default);

    Task<Result<DepositListingDto>> GetDepositsAsync(
        DateOnly? from,
        DateOnly? to,
        string? currencyCode,
        CancellationToken cancellationToken = default);

    Task<Result<LiquidityRatioDto>> GetLiquidityAsync(
        DateOnly? asOf,
        string? currencyCode,
        CancellationToken cancellationToken = default);

    Task<Result<ReportFileDto>> ExportTellerCashProofAsync(
        DateOnly? date,
        string? currencyCode,
        string format,
        CancellationToken cancellationToken = default);

    Task<Result<ReportFileDto>> ExportTrialBalanceAsync(
        DateOnly? asOf,
        string? currencyCode,
        string format,
        CancellationToken cancellationToken = default);

    Task<Result<ReportFileDto>> ExportFinancialsAsync(
        DateOnly? from,
        DateOnly? asOf,
        string? currencyCode,
        string format,
        CancellationToken cancellationToken = default);

    Task<Result<ReportFileDto>> ExportDepositsAsync(
        DateOnly? from,
        DateOnly? to,
        string? currencyCode,
        string format,
        CancellationToken cancellationToken = default);

    Task<Result<ReportFileDto>> ExportLiquidityAsync(
        DateOnly? asOf,
        string? currencyCode,
        string format,
        CancellationToken cancellationToken = default);
}
