using InventoryHold.Contracts;

namespace InventoryHold.Domain.Entities;

/// <summary>
/// Represents a product that can be sold. <see cref="AvailableQuantity"/> is the
/// live sellable count — it is already net of all currently active holds, so callers
/// never need to subtract holds themselves.
/// </summary>
public sealed class InventoryItem
{
    /// <summary>Unique product identifier. Immutable after creation.</summary>
    public string ProductId { get; }

    /// <summary>Human-readable display name.</summary>
    public string Name { get; private set; }

    /// <summary>
    /// Units currently available to sell. Decremented atomically when a hold is
    /// placed and restored atomically when a hold is released or expired.
    /// Must never go below zero; the atomic conditional write in the repository
    /// enforces this at the persistence layer.
    /// </summary>
    public int AvailableQuantity { get; private set; }

    /// <param name="productId">Unique product identifier.</param>
    /// <param name="name">Display name.</param>
    /// <param name="availableQuantity">Initial stock level. Must be ≥ 0.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="availableQuantity"/> is negative.</exception>
    public InventoryItem(string productId, string name, int availableQuantity)
    {
        if (string.IsNullOrWhiteSpace(productId))
            throw new ArgumentException("ProductId must not be empty.", nameof(productId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name must not be empty.", nameof(name));
        if (availableQuantity < 0)
            throw new ArgumentException("AvailableQuantity must be non-negative.", nameof(availableQuantity));

        ProductId = productId;
        Name = name;
        AvailableQuantity = availableQuantity;
    }
}
