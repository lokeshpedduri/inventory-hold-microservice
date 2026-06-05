namespace InventoryHold.Contracts.Events;

/// <summary>
/// Published to the <c>inventory.holds</c> topic exchange with routing key
/// <c>hold.created</c> after a hold is successfully persisted. Carries enough
/// context for downstream consumers to act independently — no extra lookups needed.
/// </summary>
/// <param name="HoldId">Unique identifier of the newly created hold.</param>
/// <param name="Items">Item lines that were reserved.</param>
/// <param name="ExpiresAtUtc">When the hold will expire if not released sooner.</param>
/// <param name="OccurredAtUtc">Wall-clock time when the event was emitted (UTC).</param>
public record HoldCreatedEvent(
    string HoldId,
    IReadOnlyList<HoldEventItemLine> Items,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset OccurredAtUtc);
