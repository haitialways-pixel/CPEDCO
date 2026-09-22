using CPCREDO.Application.Common;
using CPCREDO.Application.Loans;
using CPCREDO.WebApi.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CPCREDO.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/loans")]
public sealed class LoansController : ControllerBase
{
    private readonly ILoanService _loans;

    public LoansController(ILoanService loans) => _loans = loans;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LoanDto>>> List(
        [FromQuery] string? status,
        [FromQuery] Guid? memberId,
        CancellationToken cancellationToken)
    {
        var result = await _loans.ListAsync(status, memberId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LoanDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _loans.GetAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("preview")]
    [Authorize(Policy = "CanWrite")]
    public async Task<ActionResult<LoanScheduleDto>> Preview(
        [FromBody] PreviewLoanRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _loans.PreviewScheduleAsync(request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("payoff-quote")]
    [Authorize(Policy = "CanWrite")]
    public async Task<ActionResult<PayoffQuoteDto>> PayoffQuote(
        [FromBody] PreviewLoanRequest request,
        [FromQuery] int daysElapsed,
        CancellationToken cancellationToken)
    {
        var result = await _loans.PreviewPayoffAsync(request, daysElapsed, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost]
    [Authorize(Policy = "CanLoanDraft")]
    public async Task<ActionResult<LoanDto>> Create(
        [FromBody] CreateLoanRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _loans.CreateDraftAsync(request, cancellationToken);
        if (result.IsSuccess)
            return CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value);
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/submit")]
    [Authorize(Policy = "CanLoanDraft")]
    public async Task<ActionResult<LoanDto>> Submit(Guid id, CancellationToken cancellationToken)
    {
        var result = await _loans.SubmitAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = "CanLoanApprove")]
    public async Task<ActionResult<LoanDto>> Approve(Guid id, CancellationToken cancellationToken)
    {
        var result = await _loans.ApproveAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = "CanLoanApprove")]
    public async Task<ActionResult<LoanDto>> Reject(
        Guid id,
        [FromBody] RejectLoanRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await _loans.RejectAsync(id, request ?? new RejectLoanRequest(), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/disburse")]
    [Authorize(Policy = "CanDisburse")]
    [RequiresIdempotencyKey]
    public async Task<ActionResult<LoanDto>> Disburse(
        Guid id,
        [FromBody] DisburseLoanRequest? request,
        CancellationToken cancellationToken)
    {
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault();
        var result = await _loans.DisburseAsync(id, request ?? new DisburseLoanRequest(), key, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/renewals")]
    [Authorize(Policy = "CanLoanDraft")]
    [RequiresIdempotencyKey]
    public async Task<ActionResult<LoanDto>> Renew(Guid id, CancellationToken cancellationToken)
    {
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault();
        var result = await _loans.RenewAsync(id, key, cancellationToken);
        if (result.IsSuccess)
            return CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value);
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/repayments")]
    [Authorize(Policy = "CanCollect")]
    [RequiresIdempotencyKey]
    public async Task<ActionResult<RepaymentResultDto>> Repay(
        Guid id,
        [FromBody] RepayLoanRequest request,
        CancellationToken cancellationToken)
    {
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault();
        var result = await _loans.RepayAsync(id, request, key, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("run-accrual")]
    [Authorize(Policy = "CanLoanApprove")]
    public async Task<ActionResult<AccrualResultDto>> RunAccrual(CancellationToken cancellationToken)
    {
        var result = await _loans.RunAccrualAsync(cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("collection-sheet")]
    public async Task<IActionResult> CollectionSheet(
        [FromQuery] string? period,
        [FromQuery] string? format,
        CancellationToken cancellationToken)
    {
        var kind = (format ?? "json").Trim().ToLowerInvariant();
        if (kind is "pdf" or "csv" or "application/pdf" or "text/csv")
        {
            var exported = await _loans.ExportCollectionSheetAsync(period, format!, cancellationToken);
            if (!exported.IsSuccess)
            {
                var fail = new { code = exported.ErrorCode, error = exported.ErrorMessage };
                return exported.ErrorCode == "auth.unauthorized" ? Unauthorized(fail) : BadRequest(fail);
            }
            return File(exported.Value!.Content, exported.Value.ContentType, exported.Value.FileName);
        }

        var result = await _loans.GetCollectionSheetAsync(period, cancellationToken);
        if (result.IsSuccess)
            return Ok(result.Value);
        var body = new { code = result.ErrorCode, error = result.ErrorMessage };
        return result.ErrorCode == "auth.unauthorized" ? Unauthorized(body) : BadRequest(body);
    }

    private ActionResult<T> ToActionResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
            return Ok(result.Value);
        if (result.ErrorCode == "till.insufficient_cash")
            return Conflict(result.Details ?? new { code = result.ErrorCode, error = result.ErrorMessage });
        var body = new { code = result.ErrorCode, error = result.ErrorMessage };
        return result.ErrorCode switch
        {
            "auth.unauthorized" => Unauthorized(body),
            "auth.forbidden" or "till.not_open" => StatusCode(StatusCodes.Status403Forbidden, body),
            "loan.not_found" or "loan.product_not_found" or "loan.member_not_found" or "savings.account.not_found" => NotFound(body),
            "idempotency.conflict" => Conflict(body),
            _ => BadRequest(body)
        };
    }
}
