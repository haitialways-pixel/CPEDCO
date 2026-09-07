using CPCREDO.Application.Common;
using CPCREDO.Application.Members;
using CPCREDO.Domain.Members;
using CPCREDO.WebApi.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CPCREDO.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/members")]
public sealed class MembersController : ControllerBase
{
    private readonly IMemberService _members;

    public MembersController(IMemberService members)
    {
        _members = members;
    }

    [HttpGet("ag-export")]
    [ProducesResponseType(typeof(AgExportDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AgExportDto>> AgExport(CancellationToken cancellationToken)
    {
        var result = await _members.GetAgExportAsync(cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet]
    [ProducesResponseType(typeof(MemberListDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MemberListDto>> Search(
        [FromQuery] string? q,
        [FromQuery] MemberStatus? status,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _members.SearchAsync(q, status, skip, take, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("{id:guid}")]
    [HttpGet("{id:guid}/360")]
    [ProducesResponseType(typeof(Member360Dto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Member360Dto>> Get360(Guid id, CancellationToken cancellationToken)
    {
        var result = await _members.Get360Async(id, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost]
    [Authorize(Policy = "CanWrite")]
    [ProducesResponseType(typeof(Member360Dto), StatusCodes.Status201Created)]
    public async Task<ActionResult<Member360Dto>> Create([FromBody] MemberWriteRequest request, CancellationToken cancellationToken)
    {
        var result = await _members.CreateAsync(request, cancellationToken);
        if (result.IsSuccess)
            return CreatedAtAction(nameof(Get360), new { id = result.Value!.Id }, result.Value);
        return ToActionResult(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "CanWrite")]
    [ProducesResponseType(typeof(Member360Dto), StatusCodes.Status200OK)]
    public async Task<ActionResult<Member360Dto>> Update(
        Guid id,
        [FromBody] MemberWriteRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _members.UpdateAsync(id, request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/convert-to-societaire")]
    [Authorize(Policy = "CanWrite")]
    [RequiresIdempotencyKey]
    [ProducesResponseType(typeof(Member360Dto), StatusCodes.Status200OK)]
    public async Task<ActionResult<Member360Dto>> ConvertToSocietaire(Guid id, CancellationToken cancellationToken)
    {
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault();
        var result = await _members.ConvertToSocietaireAsync(id, key, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/permanent-shares")]
    [Authorize(Policy = "CanWrite")]
    [RequiresIdempotencyKey]
    [ProducesResponseType(typeof(Member360Dto), StatusCodes.Status200OK)]
    public async Task<ActionResult<Member360Dto>> SubscribePermanentShares(
        Guid id,
        [FromBody] SubscribePermanentSharesRequest request,
        CancellationToken cancellationToken)
    {
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault();
        var result = await _members.SubscribePermanentSharesAsync(id, request, key, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/tickets")]
    [Authorize(Policy = "CanWrite")]
    [ProducesResponseType(typeof(MemberTicketDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MemberTicketDto>> OpenTicket(
        Guid id,
        [FromBody] OpenTicketRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _members.OpenTicketAsync(id, request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/tickets/{ticketId:guid}/assign")]
    [Authorize(Policy = "CanWrite")]
    [ProducesResponseType(typeof(MemberTicketDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MemberTicketDto>> AssignTicket(
        Guid id,
        Guid ticketId,
        [FromBody] AssignTicketRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _members.AssignTicketAsync(id, ticketId, request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/tickets/{ticketId:guid}/close")]
    [Authorize(Policy = "CanWrite")]
    [ProducesResponseType(typeof(MemberTicketDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MemberTicketDto>> CloseTicket(
        Guid id,
        Guid ticketId,
        CancellationToken cancellationToken)
    {
        var result = await _members.CloseTicketAsync(id, ticketId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "CanWrite")]
    public ActionResult RejectDelete() =>
        StatusCode(StatusCodes.Status405MethodNotAllowed, new
        {
            code = "member.no_delete",
            error = "Un membre ne se supprime pas. Passez le statut à Closed."
        });

    private ActionResult<T> ToActionResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
            return Ok(result.Value);

        var body = new { code = result.ErrorCode, error = result.ErrorMessage };
        return result.ErrorCode switch
        {
            "auth.unauthorized" => Unauthorized(body),
            "auth.forbidden" => StatusCode(StatusCodes.Status403Forbidden, body),
            "member.not_found" or "ticket.not_found" => NotFound(body),
            "member.duplicate_cin" or "member.duplicate_nif" => Conflict(body),
            _ => BadRequest(body)
        };
    }
}
