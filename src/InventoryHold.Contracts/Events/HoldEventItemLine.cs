namespace InventoryHold.Contracts.Events;

/// <summary>
/// A single item line embedded in hold lifecycle events. Provides enough context
/// for consumers to act on the event without performing additional lookups.
/// </summary>
/// <param name="ProductId">Identifier of the product involved.</param>
/// <param name="Quantity">Number of units that were held or released.</param>
public record HoldEventItemLine(string ProductId, int Quantity);
