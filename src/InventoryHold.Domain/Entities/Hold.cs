using InventoryHold.Contracts;

namespace InventoryHold.Domain.Entities;

/// <summary>
/// The aggregate root for the reservation. A hold reserves one or more inventory
/// items during checkout and transitions through a strict lifecycle:
/// Active → Released (explicit client cancel) or Active → Expired (TTL elapsed).
/// Holds are never hard-deleted; terminal status is the source of truth.
/// </summary>
public sealed class Hold
{
    /// <summary>Unique, immutable identifier assigned at creation.</summary>
    public string HoldId { get; }

    /// <summary>Item lines that were reserved. Immutable after creation.</summary>
    public IReadOnlyList<HoldItem> Items { get; }

    /// <summary>Current lifecycle state. Transitions are enforced via <see cref="Release"/> and <see cref="Expire"/>.</summary>
    public HoldStatus Status { get; private set; }

    /// <summary>When the hold was created (UTC).</summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>When the hold expires or expired (UTC). Set once at construction.</summary>
    public DateTimeOffset ExpiresAtUtc { get; }

    /// <param name="holdId">Unique identifier for this hold.</param>
    /// <param name="items">Item lines to reserve. Must be non-empty.</param>
    /// <param name="createdAtUtc">Creation timestamp (UTC).</param>
    /// <param name="expiresAtUtc">Expiry timestamp (UTC). Must be after <paramref name="createdAtUtc"/>.</param>
    /// <exception cref="ArgumentException">Thrown when items list is empty or timestamps are invalid.</exception>
    public Hold(string holdId, IReadOnlyList<HoldItem> items, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(holdId))
            throw new ArgumentException("HoldId must not be empty.", nameof(holdId));
        if (items == null || items.Count == 0)
            throw new ArgumentException("A hold must contain at least one item.", nameof(items));
        if (expiresAtUtc <= createdAtUtc)
            throw new ArgumentException("ExpiresAtUtc must be after CreatedAtUtc.", nameof(expiresAtUtc));

        HoldId = holdId;
        Items = items;
        Status = HoldStatus.Active;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the hold's wall-clock expiry has passed,
    /// regardless of current <see cref="Status"/>. Used by the lazy-read path on
    /// GET to detect holds that expired between background sweeps.
    /// </summary>
    public bool IsExpiredAt(DateTimeOffset now) => now >= ExpiresAtUtc;

    /// <summary>
    /// Transitions the hold to <see cref="HoldStatus.Released"/>.
    /// Only valid from <see cref="HoldStatus.Active"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the hold is already terminal.</exception>
    public void Release()
    {
        EnsureActive("release");
        Status = HoldStatus.Released;
    }

    /// <summary>
    /// Transitions the hold to <see cref="HoldStatus.Expired"/>.
    /// Only valid from <see cref="HoldStatus.Active"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the hold is already terminal.</exception>
    public void Expire()
    {
        EnsureActive("expire");
        Status = HoldStatus.Expired;
    }

    // WHY: terminal-state guard is extracted here because both Release() and Expire() need the
    // same check, and having it inline would duplicate the error message and make future
    // status extensions fragile (e.g. if a new state is ever added).
    private void EnsureActive(string operation)
    {
        if (Status != HoldStatus.Active)
            throw new InvalidOperationException(
                $"Cannot {operation} hold '{HoldId}' — it is already in terminal state '{Status}'.");
    }
}
