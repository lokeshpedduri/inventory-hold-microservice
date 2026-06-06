using InventoryHold.Contracts;
using InventoryHold.Domain.Abstractions;
using InventoryHold.Domain.Entities;
using InventoryHold.Infrastructure.Configuration;
using InventoryHold.Infrastructure.Persistence.Documents;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace InventoryHold.Infrastructure.Persistence.Repositories;

/// <summary>
/// MongoDB adapter for <see cref="IHoldRepository"/>. Translates between the domain
/// <see cref="Hold"/> aggregate and the <see cref="HoldDocument"/> persistence model
/// so that no BSON types cross into the Domain layer.
/// </summary>
internal sealed class MongoHoldRepository : IHoldRepository
{
    private readonly IMongoCollection<HoldDocument> _collection;

    /// <param name="client">Shared MongoDB client (registered as singleton in DI).</param>
    /// <param name="options">MongoDB connection and collection settings from config.</param>
    public MongoHoldRepository(IMongoClient client, IOptions<MongoOptions> options)
    {
        var db = client.GetDatabase(options.Value.DatabaseName);
        _collection = db.GetCollection<HoldDocument>(options.Value.HoldsCollection);
    }

    /// <inheritdoc/>
    public async Task SaveAsync(Hold hold, CancellationToken cancellationToken = default)
    {
        var doc = ToDocument(hold);
        await _collection.InsertOneAsync(doc, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Hold?> GetByIdAsync(string holdId, CancellationToken cancellationToken = default)
    {
        var doc = await _collection
            .Find(d => d.HoldId == holdId)
            .FirstOrDefaultAsync(cancellationToken);

        return doc is null ? null : ToDomain(doc);
    }

    /// <inheritdoc/>
    public async Task UpdateStatusAsync(Hold hold, CancellationToken cancellationToken = default)
    {
        var update = Builders<HoldDocument>.Update
            .Set(d => d.Status, hold.Status);

        await _collection.UpdateOneAsync(
            d => d.HoldId == hold.HoldId,
            update,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Hold>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        // Active holds sorted by ExpiresAtUtc ascending (soonest to expire first),
        // then all other holds by CreatedAtUtc descending (newest first).
        // Sorting is done in application code — Mongo sort on a compound discriminated
        // field would require a more complex aggregation pipeline.
        var docs = await _collection
            .Find(_ => true)
            .SortByDescending(d => d.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return docs
            .OrderBy(d => d.Status == HoldStatus.Active ? 0 : 1)
            .ThenBy(d => d.Status == HoldStatus.Active ? d.ExpiresAtUtc.Ticks : -d.CreatedAtUtc.Ticks)
            .Select(ToDomain)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Hold>> GetExpiredActiveHoldsAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        // Filter: Status == Active AND ExpiresAtUtc < asOf
        // This is the query the background expiry worker runs on every sweep interval.
        var docs = await _collection
            .Find(d => d.Status == HoldStatus.Active && d.ExpiresAtUtc < asOf)
            .ToListAsync(cancellationToken);

        return docs.Select(ToDomain).ToList();
    }

    // ── Mapping helpers ─────────────────────────────────────────────────────

    private static HoldDocument ToDocument(Hold hold) => new()
    {
        HoldId = hold.HoldId,
        Items = hold.Items.Select(i => new HoldItemDocument
        {
            ProductId = i.ProductId,
            Quantity = i.Quantity,
        }).ToList(),
        Status = hold.Status,
        CreatedAtUtc = hold.CreatedAtUtc,
        ExpiresAtUtc = hold.ExpiresAtUtc,
    };

    private static Hold ToDomain(HoldDocument doc)
    {
        var items = doc.Items
            .Select(i => new HoldItem(i.ProductId, i.Quantity))
            .ToList();

        var hold = new Hold(doc.HoldId, items, doc.CreatedAtUtc, doc.ExpiresAtUtc);

        // WHY: the domain Hold constructor always sets Status = Active, but a document
        // retrieved from the DB may be in a terminal state. We apply the persisted status
        // by calling the appropriate transition method rather than setting a field directly,
        // so the entity's invariant-guarding methods remain the only mutation path.
        if (doc.Status == HoldStatus.Released) hold.Release();
        else if (doc.Status == HoldStatus.Expired) hold.Expire();

        return hold;
    }
}
