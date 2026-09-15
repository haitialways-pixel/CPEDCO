using CPCREDO.Application.Admin;
using CPCREDO.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CPCREDO.WebApi.Controllers;

[ApiController]
[Authorize(Policy = "CanBackup")]
[Route("api/v1/admin/backup")]
public sealed class BackupController : ControllerBase
{
    private readonly IBackupService _backup;

    public BackupController(IBackupService backup) => _backup = backup;

    [HttpGet("settings")]
    public async Task<ActionResult<BackupSettingsDto>> Settings(CancellationToken cancellationToken)
    {
        var result = await _backup.GetSettingsAsync(cancellationToken);
        return ToActionResult(result);
    }

    [HttpPut("settings")]
    public async Task<ActionResult<BackupSettingsDto>> SaveSettings(
        [FromBody] BackupSettingsDto settings,
        CancellationToken cancellationToken)
    {
        var result = await _backup.SaveSettingsAsync(settings, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("files")]
    public async Task<ActionResult<IReadOnlyList<BackupFileDto>>> Files(CancellationToken cancellationToken)
    {
        var result = await _backup.ListAsync(cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("status")]
    public async Task<ActionResult<BackupStatusDto>> Status(CancellationToken cancellationToken)
    {
        var result = await _backup.GetStatusAsync(cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost]
    public async Task<ActionResult<BackupRunResult>> BackupNow(CancellationToken cancellationToken)
    {
        var result = await _backup.BackupNowAsync(cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("restore")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<RestoreBackupResult>> Restore(
        [FromBody] RestoreBackupRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _backup.RestoreAsync(request, cancellationToken);
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
            "auth.forbidden" or "auth.invalid_credentials" => StatusCode(StatusCodes.Status403Forbidden, body),
            "backup.file_not_found" => NotFound(body),
            _ => BadRequest(body)
        };
    }
}
