using CPCREDO.Application.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CPCREDO.WebApi.Controllers;

[ApiController]
[Route("api/public")]
public sealed class InstitutionController : ControllerBase
{
    private readonly IInstitutionPublicService _institution;

    public InstitutionController(IInstitutionPublicService institution)
    {
        _institution = institution;
    }

    [AllowAnonymous]
    [HttpGet("institution")]
    [ProducesResponseType(typeof(InstitutionPublicDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<InstitutionPublicDto>> Get(CancellationToken cancellationToken)
    {
        var dto = await _institution.GetAsync(cancellationToken);
        return Ok(dto);
    }
}
