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
    private readonly IKycDocumentService _kyc;

    public MembersController(IMemberService members, IKycDocumentService kyc)
    {
        _members = members;
        _kyc = kyc;
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
    [Authorize(Policy = "CanCreateMember")]
    [ProducesResponseType(typeof(Member360Dto), StatusCodes.Status201Created)]
    public async Task<ActionResult<Member360Dto>> Create([FromBody] MemberWriteRequest request, CancellationToken cancellationToken)
    {
        var result = await _members.CreateAsync(request, cancellationToken);
        if (result.IsSuccess)
            return CreatedAtAction(nameof(Get360), new { id = result.Value!.Id }, result.Value);
        return ToActionResult(result);
    }

    [HttpPut("{id:guid}")]
    [HttpPatch("{id:guid}")]
    [HttpPost("{id:guid}")]
    [Authorize(Policy = "CanEditMember")]
    [ProducesResponseType(typeof(Member360Dto), StatusCodes.Status200OK)]
    public async Task<ActionResult<Member360Dto>> Update(
        Guid id,
        [FromBody] MemberWriteRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _members.UpdateAsync(id, request, ReadMemberOverrideGrant(), cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/override-auth")]
    [Authorize(Policy = "CanEditMember")]
    [ProducesResponseType(typeof(KycOverrideAuthResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<KycOverrideAuthResponse>> FicheOverrideAuth(
        Guid id,
        [FromBody] KycOverrideAuthRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _members.AuthorizeFicheOverrideAsync(id, request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/convert-to-societaire")]
    [Authorize(Policy = "CanEditMember")]
    [RequiresIdempotencyKey]
    [ProducesResponseType(typeof(Member360Dto), StatusCodes.Status200OK)]
    public async Task<ActionResult<Member360Dto>> ConvertToSocietaire(Guid id, CancellationToken cancellationToken)
    {
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault();
        var result = await _members.ConvertToSocietaireAsync(id, key, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/permanent-shares")]
    [Authorize(Policy = "CanEditMember")]
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
    [Authorize(Policy = "CanTickets")]
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
    [Authorize(Policy = "CanTickets")]
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
    [Authorize(Policy = "CanTickets")]
    [ProducesResponseType(typeof(MemberTicketDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MemberTicketDto>> CloseTicket(
        Guid id,
        Guid ticketId,
        CancellationToken cancellationToken)
    {
        var result = await _members.CloseTicketAsync(id, ticketId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("{id:guid}/kyc")]
    [ProducesResponseType(typeof(IReadOnlyList<KycDocumentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<KycDocumentDto>>> ListKyc(Guid id, CancellationToken cancellationToken)
    {
        var result = await _kyc.ListAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("{id:guid}/kyc/{type}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetKycFile(Guid id, string type, CancellationToken cancellationToken)
    {
        if (!TryParseKycType(type, out var parsed))
            return BadRequest(new { code = "kyc.type", error = "Type de pièce inconnu." });
        var result = await _kyc.GetFileAsync(id, parsed, cancellationToken);
        if (!result.IsSuccess)
        {
            var body = new { code = result.ErrorCode, error = result.ErrorMessage };
            return result.ErrorCode switch
            {
                "auth.unauthorized" => Unauthorized(body),
                "auth.forbidden" => StatusCode(StatusCodes.Status403Forbidden, body),
                "member.not_found" or "kyc.not_found" => NotFound(body),
                _ => BadRequest(body)
            };
        }
        return PhysicalFile(result.Value!.FullPath, result.Value.ContentType);
    }

    [HttpPut("{id:guid}/kyc/{type}")]
    [Authorize(Policy = "CanKycUpload")]
    [RequestSizeLimit(6_291_456)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(KycDocumentDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<KycDocumentDto>> UploadKyc(
        Guid id,
        string type,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (!TryParseKycType(type, out var parsed))
            return BadRequest(new { code = "kyc.type", error = "Type de pièce inconnu." });
        if (file is null || file.Length == 0)
            return BadRequest(new { code = "kyc.file_required", error = "Joignez un fichier." });
        await using var stream = file.OpenReadStream();
        var result = await _kyc.UploadAsync(
            id,
            parsed,
            stream,
            file.FileName,
            file.ContentType ?? string.Empty,
            file.Length,
            ReadOverrideGrant(),
            cancellationToken);
        return ToActionResult(result);
    }

    [HttpDelete("{id:guid}/kyc/{type}")]
    [Authorize(Policy = "CanKycManage")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<bool>> DeleteKyc(Guid id, string type, CancellationToken cancellationToken)
    {
        if (!TryParseKycType(type, out var parsed))
            return BadRequest(new { code = "kyc.type", error = "Type de pièce inconnu." });
        var result = await _kyc.DeleteAsync(id, parsed, ReadOverrideGrant(), cancellationToken);
        return ToActionResult(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "CanEditMember")]
    public ActionResult RejectDelete() =>
        StatusCode(StatusCodes.Status405MethodNotAllowed, new
        {
            code = "member.no_delete",
            error = "Un membre ne se supprime pas. Passez le statut à Closed."
        });

    private static bool TryParseKycType(string raw, out KycDocumentType type)
    {
        type = default;
        var key = (raw ?? string.Empty).Trim().ToLowerInvariant().Replace("_", "-");
        type = key switch
        {
            "photo" => KycDocumentType.Photo,
            "idfront" or "id-front" => KycDocumentType.IdFront,
            "idback" or "id-back" => KycDocumentType.IdBack,
            "signature" => KycDocumentType.Signature,
            _ => (KycDocumentType)(-1)
        };
        return Enum.IsDefined(type);
    }

    private Guid? ReadOverrideGrant()
    {
        var raw = Request.Headers["X-Kyc-Override"].FirstOrDefault();
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private Guid? ReadMemberOverrideGrant()
    {
        var raw = Request.Headers["X-Member-Override"].FirstOrDefault();
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private ActionResult<T> ToActionResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
            return Ok(result.Value);

        var body = new { code = result.ErrorCode, error = result.ErrorMessage };
        return result.ErrorCode switch
        {
            "auth.unauthorized" or "auth.invalid_credentials" => Unauthorized(body),
            "auth.forbidden" or "kyc.locked" or "member.locked" => StatusCode(StatusCodes.Status403Forbidden, body),
            "member.not_found" or "ticket.not_found" or "kyc.not_found" => NotFound(body),
            "member.duplicate_cin" or "member.duplicate_nif" => Conflict(body),
            _ => BadRequest(body)
        };
    }
}
