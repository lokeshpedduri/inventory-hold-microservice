using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace InventoryHold.Infrastructure.Persistence.Documents;

/// <summary>
/// MongoDB persistence model for an inventory item. The <see cref="AvailableQuantity"/>
/// field is the target of the race-safe conditional atomic deduct — see
/// <c>MongoInventoryRepository.DeductStockAsync</c>.
/// </summary>
internal sealed class InventoryItemDocument
{
    /// <summary>MongoDB internal document id — mapped to ProductId.</summary>
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public string ProductId { get; set; } = string.Empty;

    /// <summary>Human-readable display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Live sellable unit count. Mutated only via atomic <c>$inc</c> operations
    /// in the repository — never via read-modify-write.
    /// </summary>
    public int AvailableQuantity { get; set; }
}
