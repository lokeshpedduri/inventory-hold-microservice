namespace InventoryHold.Domain.Entities;

/// <summary>
/// An immutable line item within a <see cref="Hold"/>. Captures which product
/// and how many units are reserved so that inventory can be restored exactly on
/// release or expiry — even if the product catalogue changes later.
/// </summary>
public sealed class HoldItem
{
    /// <summary>Identifier of the reserved product.</summary>
    public string ProductId { get; }

    /// <summary>Number of units reserved. Always &gt; 0.</summary>
    public int Quantity { get; }

    /// <param name="productId">Product being reserved.</param>
    /// <param name="quantity">Units to reserve. Must be &gt; 0.</param>
    /// <exception cref="ArgumentException">Thrown when quantity is not positive.</exception>
    public HoldItem(string productId, int quantity)
    {
        if (string.IsNullOrWhiteSpace(productId))
            throw new ArgumentException("ProductId must not be empty.", nameof(productId));
        if (quantity <= 0)
            throw new ArgumentException("Quantity must be greater than zero.", nameof(quantity));

        ProductId = productId;
        Quantity = quantity;
    }
}
