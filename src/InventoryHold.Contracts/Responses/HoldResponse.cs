namespace InventoryHold.Contracts.Responses;

/// <summary>
/// Response body returned for <c>POST /api/holds</c> (201) and
/// <c>GET /api/holds/{holdId}</c> (200). Represents the full state of a hold
/// at the time of the response, including its current lifecycle status.
/// </summary>
/// <param name="HoldId">Unique identifier of the hold.</param>
/// <param name="Items">Item lines that are (or were) reserved.</param>
/// <param name="Status">Current lifecycle status of the hold.</param>
/// <param name="CreatedAtUtc">When the hold was created (UTC).</param>
/// <param name="ExpiresAtUtc">When the hold expires or expired (UTC).</param>
public record HoldResponse(
    string HoldId,
    IReadOnlyList<HoldItemLineResponse> Items,
    HoldStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// A single reserved item line within a <see cref="HoldResponse"/>.
/// </summary>
/// <param name="ProductId">Identifier of the reserved product.</param>
/// <param name="Quantity">Number of units reserved.</param>
public record HoldItemLineResponse(string ProductId, int Quantity);
