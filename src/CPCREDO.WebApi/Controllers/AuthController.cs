using CPCREDO.Application.Common;
using CPCREDO.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CPCREDO.WebApi.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly ICurrentUser _currentUser;

    public AuthController(IAuthService auth, ICurrentUser currentUser)
    {
        _auth = auth;
        _currentUser = currentUser;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _auth.LoginAsync(request, ip, cancellationToken);
        if (!result.IsSuccess)
            return Unauthorized(new { code = result.ErrorCode, error = result.ErrorMessage });

        return Ok(result.Value);
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(MeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is null)
            return Unauthorized(new { code = "auth.unauthorized", error = "Session invalide." });

        var result = await _auth.GetMeAsync(_currentUser.UserId.Value, cancellationToken);
        if (!result.IsSuccess)
            return Unauthorized(new { code = result.ErrorCode, error = result.ErrorMessage });

        return Ok(result.Value);
    }

    [Authorize]
    [HttpPost("change-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is null)
            return Unauthorized(new { code = "auth.unauthorized", error = "Session invalide." });

        var result = await _auth.ChangePasswordAsync(_currentUser.UserId.Value, request, cancellationToken);
        if (!result.IsSuccess)
        {
            var body = new { code = result.ErrorCode, error = result.ErrorMessage };
            return result.ErrorCode == "auth.unauthorized"
                ? Unauthorized(body)
                : BadRequest(body);
        }

        return NoContent();
    }
}
