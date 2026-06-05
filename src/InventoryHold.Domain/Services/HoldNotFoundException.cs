namespace InventoryHold.Domain.Services;

/// <summary>
/// Raised when a hold operation targets a hold ID that does not exist in the repository.
/// Controllers map this to HTTP 404.
/// </summary>
public sealed class HoldNotFoundException : Exception
{
    /// <summary>The hold ID that was not found.</summary>
    public string HoldId { get; }

    /// <param name="holdId">The missing hold identifier.</param>
    public HoldNotFoundException(string holdId)
        : base($"Hold '{holdId}' was not found.")
    {
        HoldId = holdId;
    }
}
