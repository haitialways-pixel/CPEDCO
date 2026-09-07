using CPCREDO.Application.Common;
using CPCREDO.Application.Teller;
using CPCREDO.WebApi.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CPCREDO.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/tills")]
public sealed class TellerController : ControllerBase
{
    private readonly ITellerService _teller;

    public TellerController(ITellerService teller)
    {
        _teller = teller;
    }

    [HttpPost("open")]
    [Authorize(Policy = "CanWrite")]
    public async Task<ActionResult<TillSessionDto>> Open([FromBody] OpenTillRequest request, CancellationToken cancellationToken)
    {
        var result = await _teller.OpenAsync(request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("current")]
    public async Task<ActionResult<TillSessionDto>> Current([FromQuery] string? currency, CancellationToken cancellationToken)
    {
        var result = await _teller.GetCurrentAsync(currency, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{tillId:guid}/close")]
    [Authorize(Policy = "CanWrite")]
    [RequiresIdempotencyKey]
    public async Task<ActionResult<TillSessionDto>> Close(
        Guid tillId,
        [FromBody] CloseTillRequest request,
        CancellationToken cancellationToken)
    {
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault();
        var result = await _teller.CloseAsync(tillId, request, key, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("deposit")]
    [Authorize(Policy = "CanWrite")]
    [RequiresIdempotencyKey]
    public async Task<ActionResult<CashPostResultDto>> Deposit(
        [FromBody] CashPostRequest request,
        CancellationToken cancellationToken)
    {
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault();
        var result = await _teller.DepositAsync(request, key, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("withdraw")]
    [Authorize(Policy = "CanWrite")]
    [RequiresIdempotencyKey]
    public async Task<ActionResult<CashPostResultDto>> Withdraw(
        [FromBody] CashPostRequest request,
        CancellationToken cancellationToken)
    {
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault();
        var result = await _teller.WithdrawAsync(request, key, cancellationToken);
        return ToActionResult(result);
    }

    private ActionResult<T> ToActionResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
            return Ok(result.Value);

        var body = new { code = result.ErrorCode, error = result.ErrorMessage };
        return result.ErrorCode switch
        {
            "auth.unauthorized" => Unauthorized(body),
            "auth.forbidden" => StatusCode(StatusCodes.Status403Forbidden, body),
            "till.not_found" or "savings.account.not_found" => NotFound(body),
            "till.not_open" or "teller.insufficient" or "till.already_open" or "till.already_closed" or "savings.blocked" => Conflict(body),
            _ => BadRequest(body)
        };
    }
}
