using InventoryHold.Contracts;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace InventoryHold.Infrastructure.Persistence.Documents;

/// <summary>
/// MongoDB persistence model for a hold. Kept separate from the domain entity so
/// that BSON attributes and driver types never leak into the Domain project (ADR-004).
/// </summary>
internal sealed class HoldDocument
{
    /// <summary>MongoDB internal document id — mapped to HoldId for simplicity.</summary>
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string HoldId { get; set; } = string.Empty;

    /// <summary>Reserved item lines.</summary>
    public List<HoldItemDocument> Items { get; set; } = [];

    /// <summary>Current lifecycle status stored as a string for readability in the DB.</summary>
    [BsonRepresentation(BsonType.String)]
    public HoldStatus Status { get; set; }

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// Expiry timestamp (UTC). Indexed for the background worker query
    /// (<c>Status == Active &amp;&amp; ExpiresAtUtc &lt; now</c>).
    /// </summary>
    public DateTimeOffset ExpiresAtUtc { get; set; }
}

/// <summary>
/// Embedded sub-document for a single reserved item line within a <see cref="HoldDocument"/>.
/// </summary>
internal sealed class HoldItemDocument
{
    /// <summary>Product identifier.</summary>
    public string ProductId { get; set; } = string.Empty;

    /// <summary>Units reserved.</summary>
    public int Quantity { get; set; }
}
