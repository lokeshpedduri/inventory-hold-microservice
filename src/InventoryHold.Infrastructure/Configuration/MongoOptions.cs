namespace InventoryHold.Infrastructure.Configuration;

/// <summary>
/// MongoDB connection settings. Bound from the <c>Mongo</c> configuration section;
/// all values are injected by docker-compose or environment overrides — nothing hardcoded.
/// </summary>
public sealed class MongoOptions
{
    /// <summary>Configuration section key used for binding.</summary>
    public const string SectionName = "Mongo";

    /// <summary>MongoDB connection string, e.g. <c>mongodb://mongo:27017</c>.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Name of the database that holds both the Holds and InventoryItems collections.</summary>
    public string DatabaseName { get; set; } = "inventory_hold";

    /// <summary>Name of the collection that stores <see cref="Persistence.Documents.HoldDocument"/> records.</summary>
    public string HoldsCollection { get; set; } = "holds";

    /// <summary>Name of the collection that stores <see cref="Persistence.Documents.InventoryItemDocument"/> records.</summary>
    public string InventoryCollection { get; set; } = "inventory";
}
