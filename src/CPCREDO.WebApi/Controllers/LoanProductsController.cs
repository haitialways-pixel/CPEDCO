using CPCREDO.Application.Common;
using CPCREDO.Application.Loans;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CPCREDO.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/loan-products")]
public sealed class LoanProductsController : ControllerBase
{
    private readonly ILoanProductService _products;

    public LoanProductsController(ILoanProductService products) => _products = products;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LoanProductDto>>> List(
        [FromQuery] bool activeOnly,
        CancellationToken cancellationToken)
    {
        var result = await _products.ListAsync(activeOnly, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<LoanProductDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _products.GetAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost]
    [Authorize(Policy = "CanWrite")]
    public async Task<ActionResult<LoanProductDto>> Create(
        [FromBody] SaveLoanProductRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _products.CreateAsync(request, cancellationToken);
        if (result.IsSuccess)
            return CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value);
        return ToActionResult(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "CanWrite")]
    public async Task<ActionResult<LoanProductDto>> Update(
        Guid id,
        [FromBody] SaveLoanProductRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _products.UpdateAsync(id, request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = "CanWrite")]
    public async Task<ActionResult<LoanProductDto>> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        var result = await _products.DeactivateAsync(id, cancellationToken);
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
            "loan.product_not_found" => NotFound(body),
            "loan.product_code_taken" => Conflict(body),
            _ => BadRequest(body)
        };
    }
}
