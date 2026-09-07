using CPCREDO.Application.Common;
using CPCREDO.Application.Treasury;
using CPCREDO.WebApi.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CPCREDO.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/treasury")]
public sealed class TreasuryController : ControllerBase
{
    private readonly ITreasuryService _treasury;

    public TreasuryController(ITreasuryService treasury) => _treasury = treasury;

    [HttpGet("bank-names")]
    public async Task<ActionResult<IReadOnlyList<string>>> BankNames(CancellationToken cancellationToken)
    {
        var result = await _treasury.ListBankNamesAsync(cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("banks")]
    public async Task<ActionResult<IReadOnlyList<BankAccountDto>>> Banks(CancellationToken cancellationToken)
    {
        var result = await _treasury.ListBanksAsync(cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("banks")]
    [Authorize(Policy = "CanWrite")]
    public async Task<ActionResult<BankAccountDto>> CreateBank(
        [FromBody] SaveBankAccountRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _treasury.CreateBankAsync(request, cancellationToken);
        if (result.IsSuccess)
            return CreatedAtAction(nameof(Banks), result.Value);
        return ToActionResult(result);
    }

    [HttpPut("banks/{id:guid}")]
    [Authorize(Policy = "CanWrite")]
    public async Task<ActionResult<BankAccountDto>> UpdateBank(
        Guid id,
        [FromBody] SaveBankAccountRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _treasury.UpdateBankAsync(id, request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("banks/{id:guid}/deactivate")]
    [Authorize(Policy = "CanWrite")]
    public async Task<ActionResult<BankAccountDto>> DeactivateBank(Guid id, CancellationToken cancellationToken)
    {
        var result = await _treasury.DeactivateBankAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("vaults")]
    public async Task<ActionResult<IReadOnlyList<VaultBalanceDto>>> Vaults(CancellationToken cancellationToken)
    {
        var result = await _treasury.ListVaultsAsync(cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("transfers")]
    public async Task<ActionResult<IReadOnlyList<TreasuryTransferDto>>> Transfers(CancellationToken cancellationToken)
    {
        var result = await _treasury.ListTransfersAsync(cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("transfers/{id:guid}")]
    public async Task<ActionResult<TreasuryTransferDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _treasury.GetAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("transfers")]
    [Authorize(Policy = "CanWrite")]
    public async Task<ActionResult<TreasuryTransferDto>> Create(
        [FromBody] CreateTreasuryTransferRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _treasury.CreateDraftAsync(request, cancellationToken);
        if (result.IsSuccess)
            return CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value);
        return ToActionResult(result);
    }

    [HttpPost("transfers/{id:guid}/approve")]
    [Authorize(Policy = "CanWrite")]
    public async Task<ActionResult<TreasuryTransferDto>> Approve(Guid id, CancellationToken cancellationToken)
    {
        var result = await _treasury.ApproveAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("transfers/{id:guid}/slip")]
    [Authorize(Policy = "CanWrite")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<ActionResult<TreasuryTransferDto>> AttachSlip(Guid id, CancellationToken cancellationToken)
    {
        if (!Request.HasFormContentType)
            return Fail("treasury.slip_required", "Le bordereau doit être envoyé en fichier (jpg, png ou pdf).");
        var form = await Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("slip");
        byte[]? content = null;
        if (file is { Length: > 0 })
        {
            using var stream = file.OpenReadStream();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken);
            content = memory.ToArray();
        }

        var result = await _treasury.AttachSlipAsync(id, new AttachTreasurySlipRequest
        {
            SlipRef = form["slipRef"].ToString(),
            SlipType = form["slipType"].ToString(),
            SlipContent = content,
            SlipFileName = file?.FileName,
            SlipContentType = file?.ContentType
        }, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("transfers/{id:guid}/execute")]
    [Authorize(Policy = "CanWrite")]
    [RequiresIdempotencyKey]
    public async Task<ActionResult<TreasuryTransferDto>> Execute(
        Guid id,
        [FromBody] ExecuteTreasuryTransferRequest? request,
        CancellationToken cancellationToken)
    {
        var key = Request.Headers["Idempotency-Key"].FirstOrDefault();
        var result = await _treasury.ExecuteAsync(id, request ?? new ExecuteTreasuryTransferRequest(), key, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("transfers/{id:guid}/slip")]
    public async Task<IActionResult> Slip(Guid id, CancellationToken cancellationToken)
    {
        var result = await _treasury.GetSlipAsync(id, cancellationToken);
        if (!result.IsSuccess)
            return Fail(result.ErrorCode!, result.ErrorMessage!);
        return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
    }

    [HttpPost("transfers/{id:guid}/cancel")]
    [Authorize(Policy = "CanWrite")]
    public async Task<ActionResult<TreasuryTransferDto>> Cancel(
        Guid id,
        [FromBody] CancelTreasuryTransferRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await _treasury.CancelAsync(id, request ?? new CancelTreasuryTransferRequest(), cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("journal")]
    public async Task<IActionResult> Journal(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? format,
        CancellationToken cancellationToken)
    {
        var exported = await _treasury.ExportJournalAsync(from, to, format ?? "pdf", cancellationToken);
        if (!exported.IsSuccess)
            return Fail(exported.ErrorCode!, exported.ErrorMessage!);
        return File(exported.Value!.Content, exported.Value.ContentType, exported.Value.FileName);
    }

    private ActionResult<T> ToActionResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
            return Ok(result.Value);
        return Fail(result.ErrorCode!, result.ErrorMessage!);
    }

    private ActionResult Fail(string code, string error)
    {
        var body = new { code, error };
        return code switch
        {
            "auth.unauthorized" => Unauthorized(body),
            "auth.forbidden" or "treasury.dual_approval_required" or "treasury.same_approver"
                or "treasury.self_execute" or "treasury.slip_required" or "treasury.password_invalid"
                => StatusCode(StatusCodes.Status403Forbidden, body),
            "treasury.not_found" or "treasury.bank_not_found" => NotFound(body),
            "treasury.till_open" or "treasury.invalid_status" or "treasury.insufficient_vault"
                or "treasury.insufficient_bank" or "treasury.insufficient_till"
                => Conflict(body),
            _ => BadRequest(body)
        };
    }
}
