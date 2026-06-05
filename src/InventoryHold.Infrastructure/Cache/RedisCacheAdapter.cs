using System.Text.Json;
using InventoryHold.Domain.Abstractions;
using StackExchange.Redis;

namespace InventoryHold.Infrastructure.Cache;

/// <summary>
/// Redis adapter for <see cref="ICache"/>. Uses StackExchange.Redis with JSON
/// serialisation for type-safe cache entries. The Domain layer depends only on
/// <see cref="ICache"/> — no Redis types cross the boundary.
/// </summary>
internal sealed class RedisCacheAdapter : ICache
{
    private readonly IDatabase _db;

    /// <param name="connection">Shared Redis connection multiplexer (singleton in DI).</param>
    public RedisCacheAdapter(IConnectionMultiplexer connection)
    {
        _db = connection.GetDatabase();
    }

    /// <inheritdoc/>
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class
    {
        var value = await _db.StringGetAsync(key);
        if (value.IsNullOrEmpty)
            return null;

        return JsonSerializer.Deserialize<T>(value!);
    }

    /// <inheritdoc/>
    public async Task SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken cancellationToken = default)
        where T : class
    {
        var json = JsonSerializer.Serialize(value);
        await _db.StringSetAsync(key, json, expiry);
    }

    /// <summary>
    /// Removes <paramref name="key"/> from cache.
    /// </summary>
    /// <remarks>
    /// CACHE INVALIDATION POINTS (§9):
    /// This method is called by <c>HoldService</c> after every stock mutation:
    /// <list type="bullet">
    ///   <item><c>PlaceHoldAsync</c> — after all deductions succeed and the hold is persisted.</item>
    ///   <item><c>ReleaseHoldAsync</c> — after the hold status is updated and stock restored.</item>
    ///   <item><c>ExpireHoldInternalAsync</c> — after the hold status is updated and stock restored.</item>
    /// </list>
    /// The 30-second TTL set in <c>HoldService.GetInventoryAsync</c> is a safety net in case a
    /// process crash occurs between a DB write and this invalidation call.
    /// </remarks>
    public async Task InvalidateAsync(string key, CancellationToken cancellationToken = default)
    {
        await _db.KeyDeleteAsync(key);
    }
}
