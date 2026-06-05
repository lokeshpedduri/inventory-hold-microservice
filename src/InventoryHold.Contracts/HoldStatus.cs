namespace InventoryHold.Contracts;

/// <summary>
/// Lifecycle state of a hold. Status is the source of truth — holds are never
/// hard-deleted; they transition through this enum so that inventory can be
/// restored and events emitted even after expiry.
/// </summary>
public enum HoldStatus
{
    /// <summary>The hold is active and stock is reserved.</summary>
    Active,

    /// <summary>The hold was explicitly released by the client before expiry.</summary>
    Released,

    /// <summary>The hold passed its <c>ExpiresAtUtc</c> and was expired by the background worker or lazy read.</summary>
    Expired,
}
