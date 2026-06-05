using InventoryHold.Contracts;

namespace InventoryHold.Domain.Services;

/// <summary>
/// Raised when a mutating operation (release) is attempted on a hold that is
/// already in a terminal state (<see cref="HoldStatus.Released"/> or
/// <see cref="HoldStatus.Expired"/>). Controllers map this to HTTP 409.
/// </summary>
public sealed class HoldAlreadyTerminalException : Exception
{
    /// <summary>The hold ID that was already terminal.</summary>
    public string HoldId { get; }

    /// <summary>The terminal status the hold was in when the operation was attempted.</summary>
    public HoldStatus CurrentStatus { get; }

    /// <param name="holdId">The hold that is already terminal.</param>
    /// <param name="currentStatus">The status at time of the failed operation.</param>
    /// <param name="innerException">Optional underlying exception from the entity guard.</param>
    public HoldAlreadyTerminalException(string holdId, HoldStatus currentStatus, Exception? innerException = null)
        : base($"Hold '{holdId}' is already in terminal state '{currentStatus}' and cannot be modified.", innerException)
    {
        HoldId = holdId;
        CurrentStatus = currentStatus;
    }
}
