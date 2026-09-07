using CPCREDO.Application.Common;
using CPCREDO.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CPCREDO.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/staff")]
public sealed class StaffController : ControllerBase
{
    private readonly IStaffService _staff;

    public StaffController(IStaffService staff) => _staff = staff;

    [HttpGet("directory")]
    [ProducesResponseType(typeof(IReadOnlyList<StaffDirectoryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<StaffDirectoryDto>>> Directory(CancellationToken cancellationToken)
    {
        var result = await _staff.ListDirectoryAsync(cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(IReadOnlyList<StaffUserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<StaffUserDto>>> List(CancellationToken cancellationToken)
    {
        var result = await _staff.ListAsync(cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(StaffUserDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<StaffUserDto>> Create([FromBody] CreateStaffRequest request, CancellationToken cancellationToken)
    {
        var result = await _staff.CreateAsync(request, cancellationToken);
        if (result.IsSuccess)
            return CreatedAtAction(nameof(List), result.Value);
        return ToActionResult(result);
    }

    [HttpPut("{id:guid}/role")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<StaffUserDto>> AssignRole(Guid id, [FromBody] AssignStaffRoleRequest request, CancellationToken cancellationToken)
    {
        var result = await _staff.AssignRoleAsync(id, request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/disable")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<StaffUserDto>> Disable(Guid id, CancellationToken cancellationToken)
    {
        var result = await _staff.DisableAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/reset-password")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<StaffUserDto>> ResetPassword(Guid id, [FromBody] ResetStaffPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _staff.ResetPasswordAsync(id, request, cancellationToken);
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
            "staff.not_found" or "staff.role_not_found" or "staff.branch_not_found" => NotFound(body),
            "staff.duplicate_username" or "staff.last_admin" => Conflict(body),
            _ => BadRequest(body)
        };
    }
}