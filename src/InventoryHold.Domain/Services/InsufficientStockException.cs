namespace InventoryHold.Domain.Services;

/// <summary>
/// Raised by <see cref="HoldService.PlaceHoldAsync"/> when a product has insufficient
/// available stock to satisfy the requested quantity. By the time this exception
/// reaches the caller, all previously deducted items have already been compensated.
/// </summary>
public sealed class InsufficientStockException : Exception
{
    /// <summary>The product for which stock was insufficient.</summary>
    public string ProductId { get; }

    /// <summary>The quantity that was requested but could not be reserved.</summary>
    public int RequestedQuantity { get; }

    /// <param name="productId">Product that had insufficient stock.</param>
    /// <param name="requestedQuantity">Quantity that could not be satisfied.</param>
    public InsufficientStockException(string productId, int requestedQuantity)
        : base($"Insufficient stock for product '{productId}': requested {requestedQuantity} unit(s).")
    {
        ProductId = productId;
        RequestedQuantity = requestedQuantity;
    }
}
