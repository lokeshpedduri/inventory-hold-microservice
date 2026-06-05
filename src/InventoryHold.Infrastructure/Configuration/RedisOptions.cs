namespace InventoryHold.Infrastructure.Configuration;

/// <summary>
/// Redis connection settings. Bound from the <c>Redis</c> configuration section.
/// </summary>
public sealed class RedisOptions
{
    /// <summary>Configuration section key used for binding.</summary>
    public const string SectionName = "Redis";

    /// <summary>StackExchange.Redis connection string, e.g. <c>redis:6379</c>.</summary>
    public string ConnectionString { get; set; } = string.Empty;
}
