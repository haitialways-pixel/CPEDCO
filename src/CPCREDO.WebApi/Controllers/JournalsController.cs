using CPCREDO.Application.Accounting;
using CPCREDO.Application.Common;
using CPCREDO.WebApi.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CPCREDO.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/journals")]
public sealed class JournalsController : ControllerBase
{
    private readonly IJournalService _journals;

    public JournalsController(IJournalService journals)
    {
        _journals = journals;
    }

    [HttpPost]
    [Authorize(Policy = "CanWrite")]
    [RequiresIdempotencyKey]
    [ProducesResponseType(typeof(JournalDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<JournalDto>> Post([FromBody] CreateJournalRequest request, CancellationToken cancellationToken)
    {
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault();
        var result = await _journals.PostAsync(request, key, cancellationToken);
        return ToActionResult(result, created: true);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(JournalDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JournalDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _journals.GetAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/reversal")]
    [Authorize(Policy = "CanReverse")]
    [RequiresIdempotencyKey]
    [ProducesResponseType(typeof(JournalDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<JournalDto>> Reverse(
        Guid id,
        [FromBody] ReverseJournalRequest? request,
        CancellationToken cancellationToken)
    {
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault();
        var result = await _journals.ReverseAsync(id, request ?? new ReverseJournalRequest(), key, cancellationToken);
        return ToActionResult(result, created: true);
    }

    [HttpPut("{id:guid}")]
    [HttpPatch("{id:guid}")]
    [HttpDelete("{id:guid}")]
    public ActionResult RejectMutation() =>
        StatusCode(StatusCodes.Status405MethodNotAllowed, new
        {
            code = "journal.immutable",
            error = "Les écritures comptables validées sont immuables. Effectuez une contre-passation (nouvelle écriture inverse)."
        });

    private ActionResult<JournalDto> ToActionResult(Result<JournalDto> result, bool created = false)
    {
        if (result.IsSuccess)
        {
            if (created)
                return CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value);
            return Ok(result.Value);
        }

        var body = new { code = result.ErrorCode, error = result.ErrorMessage };
        return result.ErrorCode switch
        {
            "auth.unauthorized" => Unauthorized(body),
            "auth.forbidden" => StatusCode(StatusCodes.Status403Forbidden, body),
            "journal.not_found" => NotFound(body),
            "journal.immutable" or "journal.already_reversed" => Conflict(body),
            _ => BadRequest(body)
        };
    }
}
