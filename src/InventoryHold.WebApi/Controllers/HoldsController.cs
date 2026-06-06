using InventoryHold.Contracts.Requests;
using InventoryHold.Contracts.Responses;
using InventoryHold.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace InventoryHold.WebApi.Controllers;

/// <summary>
/// Manages hold lifecycle. All business logic is delegated to <see cref="HoldService"/>;
/// this controller only maps domain results/exceptions to HTTP responses.
/// </summary>
[ApiController]
[Route("api/holds")]
[Produces("application/json")]
public sealed class HoldsController : ControllerBase
{
    private readonly HoldService _holdService;

    /// <param name="holdService">Domain service — injected, never newed up here.</param>
    public HoldsController(HoldService holdService)
    {
        _holdService = holdService;
    }

    /// <summary>Places a new hold, reserving the requested items for checkout.</summary>
    /// <param name="request">Product-quantity pairs to reserve.</param>
    /// <param name="cancellationToken">Propagated to all async I/O.</param>
    /// <returns>201 with the created hold, 400 on invalid input, 409 on insufficient stock.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(HoldResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateHold(
        [FromBody] CreateHoldRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
            return ValidationProblem("At least one item is required.");

        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.ProductId))
                return ValidationProblem($"ProductId must not be empty.");
            if (item.Quantity <= 0)
                return ValidationProblem($"Quantity for product '{item.ProductId}' must be greater than zero.");
        }

        var items = request.Items
            .Select(i => (i.ProductId, i.Quantity))
            .ToList();

        var hold = await _holdService.PlaceHoldAsync(items, cancellationToken);

        return CreatedAtAction(
            nameof(GetHold),
            new { holdId = hold.HoldId },
            MapToResponse(hold));
    }

    /// <summary>Retrieves a hold by ID, applying lazy expiry if past TTL.</summary>
    /// <param name="holdId">Hold identifier.</param>
    /// <param name="cancellationToken">Propagated to all async I/O.</param>
    /// <returns>200 with the hold state, or 404 if not found.</returns>
    [HttpGet("{holdId}")]
    [ProducesResponseType(typeof(HoldResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetHold(string holdId, CancellationToken cancellationToken)
    {
        var hold = await _holdService.GetHoldAsync(holdId, cancellationToken);

        if (hold is null)
            return Problem(
                title: "Hold not found",
                detail: $"No hold with ID '{holdId}' exists.",
                statusCode: StatusCodes.Status404NotFound);

        return Ok(MapToResponse(hold));
    }

    /// <summary>
    /// Releases an active hold, returning reserved stock to available inventory.
    /// </summary>
    /// <param name="holdId">Hold identifier.</param>
    /// <param name="cancellationToken">Propagated to all async I/O.</param>
    /// <returns>200 with the released hold, 404 if not found, 409 if already terminal.</returns>
    [HttpDelete("{holdId}")]
    [ProducesResponseType(typeof(HoldResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReleaseHold(string holdId, CancellationToken cancellationToken)
    {
        var hold = await _holdService.ReleaseHoldAsync(holdId, cancellationToken);
        return Ok(MapToResponse(hold));
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static HoldResponse MapToResponse(Domain.Entities.Hold hold) => new(
        hold.HoldId,
        hold.Items.Select(i => new HoldItemLineResponse(i.ProductId, i.Quantity)).ToList(),
        hold.Status,
        hold.CreatedAtUtc,
        hold.ExpiresAtUtc);
}
