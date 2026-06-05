using InventoryHold.Domain.Abstractions;
using InventoryHold.Domain.Entities;
using InventoryHold.Infrastructure.Configuration;
using InventoryHold.Infrastructure.Persistence.Documents;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace InventoryHold.Infrastructure.Persistence.Repositories;

/// <summary>
/// MongoDB adapter for <see cref="IInventoryRepository"/>.
/// The critical operation is <see cref="DeductStockAsync"/>: a single conditional
/// atomic <c>FindOneAndUpdateAsync</c> that prevents overselling without a transaction.
/// </summary>
internal sealed class MongoInventoryRepository : IInventoryRepository
{
    private readonly IMongoCollection<InventoryItemDocument> _collection;

    /// <param name="client">Shared MongoDB client (registered as singleton in DI).</param>
    /// <param name="options">MongoDB connection and collection settings from config.</param>
    public MongoInventoryRepository(IMongoClient client, IOptions<MongoOptions> options)
    {
        var db = client.GetDatabase(options.Value.DatabaseName);
        _collection = db.GetCollection<InventoryItemDocument>(options.Value.InventoryCollection);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<InventoryItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var docs = await _collection.Find(_ => true).ToListAsync(cancellationToken);
        return docs.Select(d => new InventoryItem(d.ProductId, d.Name, d.AvailableQuantity)).ToList();
    }

    /// <summary>
    /// Atomically decrements <c>AvailableQuantity</c> by <paramref name="quantity"/> for
    /// the given product — but ONLY if the document satisfies <c>AvailableQuantity &gt;= quantity</c>
    /// at the moment of the write.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if stock was deducted; <see langword="false"/> if stock was
    /// insufficient (the document was not modified).
    /// </returns>
    /// <remarks>
    /// RACE-SAFETY MECHANISM (ADR-002):
    /// The quantity guard is embedded in the Mongo filter, not evaluated in application code.
    /// This means the check and the decrement are a single atomic document-level operation.
    /// There is no window between reading the quantity and writing the decrement where a
    /// concurrent request could observe the same stock and both succeed — a plain
    /// read-modify-write in application code would have that race window.
    ///
    /// If the filter matches no document (stock insufficient or product not found),
    /// FindOneAndUpdateAsync returns null and nothing is written. No compensation needed.
    /// </remarks>
    public async Task<bool> DeductStockAsync(
        string productId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        // RACE-SAFETY FILTER: both conditions must be true in the same atomic operation.
        // - ProductId == productId   → targets the right document
        // - AvailableQuantity >= qty → stock guard; prevents oversell and negative quantities
        var filter = Builders<InventoryItemDocument>.Filter.And(
            Builders<InventoryItemDocument>.Filter.Eq(d => d.ProductId, productId),
            Builders<InventoryItemDocument>.Filter.Gte(d => d.AvailableQuantity, quantity)
        );

        // ATOMIC DECREMENT: $inc with a negative value.
        // MongoDB applies filter + update as a single document-level atomic operation.
        var update = Builders<InventoryItemDocument>.Update
            .Inc(d => d.AvailableQuantity, -quantity);

        var result = await _collection.FindOneAndUpdateAsync(
            filter,
            update,
            cancellationToken: cancellationToken);

        // A null result means the filter matched nothing — insufficient stock (or unknown product).
        // Nothing was written; the caller must NOT assume any stock was consumed.
        return result is not null;
    }

    /// <summary>
    /// Unconditionally restores <paramref name="quantity"/> units to <c>AvailableQuantity</c>.
    /// Called after hold release, expiry, or compensation of a partial multi-item failure.
    /// </summary>
    /// <remarks>
    /// No stock guard here — restores are always safe because we only restore exactly what
    /// was previously deducted by a successful <see cref="DeductStockAsync"/>.
    /// </remarks>
    public async Task RestoreStockAsync(
        string productId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<InventoryItemDocument>.Filter
            .Eq(d => d.ProductId, productId);

        // UNCONDITIONAL INCREMENT: no guard needed — we are restoring units that were
        // previously deducted by a confirmed successful DeductStockAsync call.
        var update = Builders<InventoryItemDocument>.Update
            .Inc(d => d.AvailableQuantity, quantity);

        await _collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
    }
}
