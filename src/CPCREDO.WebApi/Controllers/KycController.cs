using CPCREDO.Application.Common;
using CPCREDO.Application.Members;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CPCREDO.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/kyc")]
public sealed class KycController : ControllerBase
{
    private readonly IKycDocumentService _kyc;

    public KycController(IKycDocumentService kyc)
    {
        _kyc = kyc;
    }

    [HttpPost("{documentId:guid}/override-auth")]
    [Authorize(Policy = "CanKycUpload")]
    [ProducesResponseType(typeof(KycOverrideAuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<KycOverrideAuthResponse>> OverrideAuth(
        Guid documentId,
        [FromBody] KycOverrideAuthRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _kyc.AuthorizeOverrideAsync(documentId, request, cancellationToken);
        if (result.IsSuccess)
            return Ok(result.Value);

        var body = new { code = result.ErrorCode, error = result.ErrorMessage };
        return result.ErrorCode switch
        {
            "auth.unauthorized" or "auth.invalid_credentials" => Unauthorized(body),
            "auth.forbidden" or "kyc.locked" => StatusCode(StatusCodes.Status403Forbidden, body),
            "kyc.not_found" => NotFound(body),
            _ => BadRequest(body)
        };
    }
}
