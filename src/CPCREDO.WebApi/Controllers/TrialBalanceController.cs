using CPCREDO.Application.Accounting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CPCREDO.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/trial-balance")]
public sealed class TrialBalanceController : ControllerBase
{
    private readonly IJournalService _journals;

    public TrialBalanceController(IJournalService journals)
    {
        _journals = journals;
    }

    [HttpGet]
    [ProducesResponseType(typeof(TrialBalanceDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<TrialBalanceDto>> Get(
        [FromQuery] DateOnly? asOf,
        [FromQuery] string? currency,
        CancellationToken cancellationToken)
    {
        var result = await _journals.GetTrialBalanceAsync(asOf, currency, cancellationToken);
        if (!result.IsSuccess)
        {
            var body = new { code = result.ErrorCode, error = result.ErrorMessage };
            return result.ErrorCode == "auth.unauthorized" ? Unauthorized(body) : BadRequest(body);
        }

        return Ok(result.Value);
    }
}
