using CPCREDO.Application.Common;
using CPCREDO.Application.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CPCREDO.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/reports")]
public sealed class ReportsController : ControllerBase
{
    private readonly IReportService _reports;

    public ReportsController(IReportService reports) => _reports = reports;

    [HttpGet("teller-cash-proof")]
    public Task<IActionResult> TellerCashProof(
        [FromQuery] DateOnly? date,
        [FromQuery] string? currency,
        [FromQuery] string? format,
        CancellationToken cancellationToken) =>
        Respond(
            format,
            () => _reports.GetTellerCashProofAsync(date, currency, cancellationToken),
            () => _reports.ExportTellerCashProofAsync(date, currency, format!, cancellationToken));

    [HttpGet("trial-balance")]
    public Task<IActionResult> TrialBalance(
        [FromQuery] DateOnly? asOf,
        [FromQuery] string? currency,
        [FromQuery] string? format,
        CancellationToken cancellationToken) =>
        Respond(
            format,
            () => _reports.GetTrialBalanceAsync(asOf, currency, cancellationToken),
            () => _reports.ExportTrialBalanceAsync(asOf, currency, format!, cancellationToken));

    [HttpGet("financials")]
    public Task<IActionResult> Financials(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? asOf,
        [FromQuery] string? currency,
        [FromQuery] string? format,
        CancellationToken cancellationToken) =>
        Respond(
            format,
            () => _reports.GetFinancialsAsync(from, asOf, currency, cancellationToken),
            () => _reports.ExportFinancialsAsync(from, asOf, currency, format!, cancellationToken));

    [HttpGet("deposits")]
    public Task<IActionResult> Deposits(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? currency,
        [FromQuery] string? format,
        CancellationToken cancellationToken) =>
        Respond(
            format,
            () => _reports.GetDepositsAsync(from, to, currency, cancellationToken),
            () => _reports.ExportDepositsAsync(from, to, currency, format!, cancellationToken));

    [HttpGet("liquidity")]
    public Task<IActionResult> Liquidity(
        [FromQuery] DateOnly? asOf,
        [FromQuery] string? currency,
        [FromQuery] string? format,
        CancellationToken cancellationToken) =>
        Respond(
            format,
            () => _reports.GetLiquidityAsync(asOf, currency, cancellationToken),
            () => _reports.ExportLiquidityAsync(asOf, currency, format!, cancellationToken));

    private async Task<IActionResult> Respond<T>(
        string? format,
        Func<Task<Result<T>>> json,
        Func<Task<Result<ReportFileDto>>> file)
    {
        var kind = (format ?? "json").Trim().ToLowerInvariant();
        if (kind is "pdf" or "csv" or "application/pdf" or "text/csv")
        {
            var exported = await file();
            if (!exported.IsSuccess)
                return Fail(exported.ErrorCode!, exported.ErrorMessage!);
            return File(exported.Value!.Content, exported.Value.ContentType, exported.Value.FileName);
        }

        var result = await json();
        if (!result.IsSuccess)
            return Fail(result.ErrorCode!, result.ErrorMessage!);
        return Ok(result.Value);
    }

    private IActionResult Fail(string code, string error)
    {
        var body = new { code, error };
        return code switch
        {
            "auth.unauthorized" => Unauthorized(body),
            "auth.forbidden" => StatusCode(StatusCodes.Status403Forbidden, body),
            _ => BadRequest(body)
        };
    }
}
