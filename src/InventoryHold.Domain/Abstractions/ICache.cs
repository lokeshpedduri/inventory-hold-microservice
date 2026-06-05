namespace InventoryHold.Domain.Abstractions;

/// <summary>
/// Port for a simple key/value cache (cache-aside pattern).
/// The inventory read path uses this to avoid hitting MongoDB on every GET.
/// Implementations use Redis; Domain has no Redis dependency.
/// </summary>
public interface ICache
{
    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, or
    /// <see langword="null"/> if the key is absent or expired.
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Stores <paramref name="value"/> under <paramref name="key"/> with the
    /// given absolute <paramref name="expiry"/>.
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Removes the entry for <paramref name="key"/> if it exists.
    /// Called after any stock mutation to keep the inventory read path consistent.
    /// </summary>
    Task InvalidateAsync(string key, CancellationToken cancellationToken = default);
}
