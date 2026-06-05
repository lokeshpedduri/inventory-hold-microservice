using InventoryHold.Domain.Entities;

namespace InventoryHold.Domain.Abstractions;

/// <summary>
/// Port for reading and atomically mutating inventory stock.
/// The critical contract: <see cref="DeductStockAsync"/> is a single conditional
/// atomic operation — it succeeds only when sufficient stock exists, with no
/// read-then-write race window.
/// </summary>
public interface IInventoryRepository
{
    /// <summary>
    /// Returns all inventory items. Results are fed to the cache-aside layer above.
    /// </summary>
    Task<IReadOnlyList<InventoryItem>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically decrements <see cref="InventoryItem.AvailableQuantity"/> by
    /// <paramref name="quantity"/> for the given product, but ONLY if
    /// <c>AvailableQuantity &gt;= quantity</c> at the moment of the write.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the deduction succeeded; <see langword="false"/>
    /// if stock was insufficient (nothing was written).
    /// </returns>
    /// <remarks>
    /// Implemented as a single <c>FindOneAndUpdateAsync</c> with the stock guard in
    /// the filter — see ADR-002. A false return means no stock was changed.
    /// </remarks>
    Task<bool> DeductStockAsync(string productId, int quantity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unconditionally restores <paramref name="quantity"/> units to
    /// <see cref="InventoryItem.AvailableQuantity"/>. Called on release, expiry,
    /// and compensation after a partial multi-item hold failure.
    /// </summary>
    Task RestoreStockAsync(string productId, int quantity, CancellationToken cancellationToken = default);
}
