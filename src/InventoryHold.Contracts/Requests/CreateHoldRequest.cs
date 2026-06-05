namespace InventoryHold.Contracts.Requests;

/// <summary>
/// Request body for <c>POST /api/holds</c>. Carries the set of items the caller
/// wishes to reserve. Each item line must reference a known product with a positive
/// quantity; validation is enforced at the controller boundary.
/// </summary>
/// <param name="Items">One or more item lines to place on hold.</param>
public record CreateHoldRequest(IReadOnlyList<HoldItemLine> Items);

/// <summary>
/// A single product-quantity pair within a <see cref="CreateHoldRequest"/>.
/// </summary>
/// <param name="ProductId">Identifier of the product to hold.</param>
/// <param name="Quantity">Number of units to reserve. Must be &gt; 0.</param>
public record HoldItemLine(string ProductId, int Quantity);
