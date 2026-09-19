using CPCREDO.Application.Common;
using CPCREDO.Application.Savings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CPCREDO.WebApi.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/savings-products")]
public sealed class SavingsProductsController : ControllerBase
{
    private readonly ISavingsProductService _products;

    public SavingsProductsController(ISavingsProductService products) => _products = products;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SavingsProductDto>>> List(
        [FromQuery] bool activeOnly,
        CancellationToken cancellationToken)
    {
        var result = await _products.ListAsync(activeOnly, cancellationToken);
        return ToActionResult(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SavingsProductDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _products.GetAsync(id, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost]
    [Authorize(Policy = "CanManageProducts")]
    public async Task<ActionResult<SavingsProductDto>> Create(
        [FromBody] SaveSavingsProductRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _products.CreateAsync(request, cancellationToken);
        if (result.IsSuccess)
            return CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, result.Value);
        return ToActionResult(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "CanManageProducts")]
    public async Task<ActionResult<SavingsProductDto>> Update(
        Guid id,
        [FromBody] SaveSavingsProductRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _products.UpdateAsync(id, request, cancellationToken);
        return ToActionResult(result);
    }

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = "CanManageProducts")]
    public async Task<ActionResult<SavingsProductDto>> Deactivate(Guid id, CancellationToken cancellationToken)
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
            "savings.product.not_found" => NotFound(body),
            "savings.product_code_taken" => Conflict(body),
            _ => BadRequest(body)
        };
    }
}
