using CPCREDO.Application.Common;
using CPCREDO.Application.Loans;
using CPCREDO.WebApi.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CPCREDO.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/credit-pool")]
public sealed class CreditPoolController : ControllerBase
{
    private readonly ICreditPoolService _pool;

    public CreditPoolController(ICreditPoolService pool) => _pool = pool;

    [HttpGet]
    [Authorize(Policy = "CanCreditReport")]
    public async Task<ActionResult<CreditPoolDto>> Get([FromQuery] string? currency, CancellationToken cancellationToken)
    {
        var result = await _pool.GetAsync(currency, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("funding")]
    [Authorize(Policy = "CanFundCreditPool")]
    [RequiresIdempotencyKey]
    public async Task<ActionResult<CreditPoolDto>> Fund(
        [FromBody] FundCreditPoolRequest request,
        CancellationToken cancellationToken)
    {
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault();
        var result = await _pool.FundAsync(request ?? new FundCreditPoolRequest(), key, cancellationToken);
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
            "internal.insufficient_vault" or "internal.insufficient" or "pool.insufficient" => Conflict(body),
            _ => BadRequest(body)
        };
    }
}
