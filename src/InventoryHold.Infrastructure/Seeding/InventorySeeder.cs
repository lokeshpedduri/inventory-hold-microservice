using InventoryHold.Infrastructure.Configuration;
using InventoryHold.Infrastructure.Persistence.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace InventoryHold.Infrastructure.Seeding;

/// <summary>
/// Seeds the inventory collection with a fixed set of products on application startup
/// if the collection is empty. Idempotent — running it multiple times does not create
/// duplicate products.
/// </summary>
public sealed class InventorySeeder
{
    private readonly IMongoCollection<InventoryItemDocument> _collection;
    private readonly ILogger<InventorySeeder> _logger;

    /// <param name="client">Shared MongoDB client.</param>
    /// <param name="options">Collection settings from config.</param>
    /// <param name="logger">Structured logger.</param>
    public InventorySeeder(IMongoClient client, IOptions<MongoOptions> options, ILogger<InventorySeeder> logger)
    {
        var db = client.GetDatabase(options.Value.DatabaseName);
        _collection = db.GetCollection<InventoryItemDocument>(options.Value.InventoryCollection);
        _logger = logger;
    }

    /// <summary>
    /// Inserts seed products if the collection is currently empty.
    /// </summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var count = await _collection.CountDocumentsAsync(_ => true, cancellationToken: cancellationToken);
        if (count > 0)
        {
            _logger.LogInformation("Inventory already seeded ({Count} products). Skipping.", count);
            return;
        }

        var products = new List<InventoryItemDocument>
        {
            new() { ProductId = "prod-001", Name = "Wireless Headphones",    AvailableQuantity = 50  },
            new() { ProductId = "prod-002", Name = "Mechanical Keyboard",    AvailableQuantity = 30  },
            new() { ProductId = "prod-003", Name = "USB-C Hub (7-port)",     AvailableQuantity = 75  },
            new() { ProductId = "prod-004", Name = "27\" 4K Monitor",        AvailableQuantity = 20  },
            new() { ProductId = "prod-005", Name = "Ergonomic Mouse",        AvailableQuantity = 100 },
            new() { ProductId = "prod-006", Name = "Laptop Stand (Aluminium)", AvailableQuantity = 60 },
            new() { ProductId = "prod-007", Name = "Webcam 1080p",           AvailableQuantity = 45  },
        };

        await _collection.InsertManyAsync(products, cancellationToken: cancellationToken);
        _logger.LogInformation("Seeded {Count} inventory products.", products.Count);
    }
}
