namespace InventoryHold.Contracts.Events;

/// <summary>
/// Published to the <c>inventory.holds</c> topic exchange with routing key
/// <c>hold.released</c> after a hold is explicitly released via
/// <c>DELETE /api/holds/{holdId}</c>. Inventory has already been restored
/// before this event is emitted.
/// </summary>
/// <param name="HoldId">Unique identifier of the released hold.</param>
/// <param name="Items">Item lines that were released back to available stock.</param>
/// <param name="Reason">Free-text reason for the release (e.g. "client_requested").</param>
/// <param name="OccurredAtUtc">Wall-clock time when the event was emitted (UTC).</param>
public record HoldReleasedEvent(
    string HoldId,
    IReadOnlyList<HoldEventItemLine> Items,
    string Reason,
    DateTimeOffset OccurredAtUtc);
