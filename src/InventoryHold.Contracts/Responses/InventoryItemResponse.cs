namespace InventoryHold.Contracts.Responses;

/// <summary>
/// A single inventory item as returned by <c>GET /api/inventory</c>.
/// <see cref="AvailableQuantity"/> reflects the live sellable count — it is
/// already net of any currently active holds.
/// </summary>
/// <param name="ProductId">Unique product identifier.</param>
/// <param name="Name">Human-readable product name.</param>
/// <param name="AvailableQuantity">Units available for sale right now.</param>
public record InventoryItemResponse(string ProductId, string Name, int AvailableQuantity);
