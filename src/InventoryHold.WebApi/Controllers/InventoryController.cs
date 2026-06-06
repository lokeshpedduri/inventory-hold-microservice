using InventoryHold.Contracts.Responses;
using InventoryHold.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace InventoryHold.WebApi.Controllers;

/// <summary>
/// Read-only view of current inventory levels. Backed by the cache-aside pattern —
/// responses are served from Redis on cache hits; MongoDB on misses (§9).
/// </summary>
[ApiController]
[Route("api/inventory")]
[Produces("application/json")]
public sealed class InventoryController : ControllerBase
{
    private readonly HoldService _holdService;

    /// <param name="holdService">Domain service that owns the cache-aside inventory read.</param>
    public InventoryController(HoldService holdService)
    {
        _holdService = holdService;
    }

    /// <summary>
    /// Returns all inventory items with their current available (sellable) quantities.
    /// <c>AvailableQuantity</c> is already net of active holds.
    /// </summary>
    /// <param name="cancellationToken">Propagated to all async I/O.</param>
    /// <returns>200 with the full inventory list.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<InventoryItemResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInventory(CancellationToken cancellationToken)
    {
        var items = await _holdService.GetInventoryAsync(cancellationToken);

        var response = items
            .Select(i => new InventoryItemResponse(i.ProductId, i.Name, i.AvailableQuantity))
            .ToList();

        return Ok(response);
    }
}
