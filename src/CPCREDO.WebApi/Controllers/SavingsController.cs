using CPCREDO.Application.Common;
using CPCREDO.Application.Savings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CPCREDO.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/savings")]
public sealed class SavingsController : ControllerBase
{
    private readonly ISavingsService _savings;

    public SavingsController(ISavingsService savings)
    {
        _savings = savings;
    }

    [HttpGet("products")]
    [ProducesResponseType(typeof(IReadOnlyList<SavingsProductDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SavingsProductDto>>> GetProducts(CancellationToken cancellationToken)
    {
        var result = await _savings.ListProductsAsync(cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("accounts")]
    [Authorize(Policy = "CanWrite")]
    [ProducesResponseType(typeof(SavingsAccountDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<SavingsAccountDto>> OpenAccount(
        [FromBody] OpenSavingsAccountRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _savings.OpenAccountAsync(request.MemberId, request.ProductId, cancellationToken);
        if (result.IsSuccess)
            return CreatedAtAction(nameof(GetAccount), new { accountId = result.Value!.Id }, result.Value);
        return ToActionResult(result);
    }

    [HttpGet("accounts/{accountId:guid}")]
    [ProducesResponseType(typeof(SavingsAccountDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SavingsAccountDto>> GetAccount(Guid accountId, CancellationToken cancellationToken)
    {
        var result = await _savings.GetAccountAsync(accountId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("accounts/{accountId:guid}/statement")]
    [ProducesResponseType(typeof(SavingsStatementDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SavingsStatementDto>> GetStatement(
        Guid accountId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        var result = await _savings.GetStatementAsync(accountId, from, to, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("accounts/{accountId:guid}/statement.pdf")]
    [Produces("application/pdf")]
    public async Task<IActionResult> GetStatementPdf(
        Guid accountId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        var result = await _savings.GetStatementPdfAsync(accountId, from, to, cancellationToken);
        if (!result.IsSuccess)
        {
            var body = new { code = result.ErrorCode, error = result.ErrorMessage };
            return result.ErrorCode switch
            {
                "auth.unauthorized" => Unauthorized(body),
                "savings.account.not_found" => NotFound(body),
                _ => BadRequest(body)
            };
        }

        return File(result.Value!.Content, "application/pdf", result.Value.FileName);
    }

    [HttpPost("accounts/{accountId:guid}/holds")]
    [Authorize(Policy = "CanWrite")]
    [ProducesResponseType(typeof(SavingsAccountDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SavingsAccountDto>> PlaceHold(
        Guid accountId,
        [FromBody] PlaceHoldRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _savings.PlaceHoldAsync(accountId, request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("accounts/{accountId:guid}/holds/{holdId:guid}/release")]
    [Authorize(Policy = "CanWrite")]
    [ProducesResponseType(typeof(SavingsAccountDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SavingsAccountDto>> ReleaseHold(
        Guid accountId,
        Guid holdId,
        CancellationToken cancellationToken)
    {
        var result = await _savings.ReleaseHoldAsync(accountId, holdId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("accounts/{accountId:guid}/block")]
    [Authorize(Policy = "CanWrite")]
    [ProducesResponseType(typeof(SavingsAccountDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SavingsAccountDto>> Block(
        Guid accountId,
        [FromBody] BlockAccountRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _savings.BlockAccountAsync(accountId, request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("accounts/{accountId:guid}/unblock")]
    [Authorize(Policy = "CanWrite")]
    [ProducesResponseType(typeof(SavingsAccountDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<SavingsAccountDto>> Unblock(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var result = await _savings.UnblockAccountAsync(accountId, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("members/{memberId:guid}/accounts")]
    [ProducesResponseType(typeof(IReadOnlyList<SavingsAccountDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SavingsAccountDto>>> ListMemberAccounts(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var result = await _savings.ListMemberAccountsAsync(memberId, cancellationToken);
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
            "savings.member.not_found" or "savings.product.not_found" or "savings.account.not_found" or "savings.hold.not_found" => NotFound(body),
            "savings.account.exists" => Conflict(body),
            _ => BadRequest(body)
        };
    }
}

public sealed record OpenSavingsAccountRequest(Guid MemberId, Guid ProductId);
