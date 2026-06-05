namespace InventoryHold.Contracts.Events;

/// <summary>
/// Published to the <c>inventory.holds</c> topic exchange with routing key
/// <c>hold.expired</c> when the background expiry worker (or a lazy GET read)
/// transitions a hold to <see cref="HoldStatus.Expired"/>. Inventory has already
/// been restored before this event is emitted.
/// </summary>
/// <param name="HoldId">Unique identifier of the expired hold.</param>
/// <param name="Items">Item lines whose stock was restored on expiry.</param>
/// <param name="ExpiredAtUtc">The <c>ExpiresAtUtc</c> timestamp of the hold.</param>
/// <param name="OccurredAtUtc">Wall-clock time when the event was emitted (UTC).</param>
public record HoldExpiredEvent(
    string HoldId,
    IReadOnlyList<HoldEventItemLine> Items,
    DateTimeOffset ExpiredAtUtc,
    DateTimeOffset OccurredAtUtc);
