namespace InventoryHold.Domain.Services;

/// <summary>
/// RabbitMQ routing key constants for the <c>inventory.holds</c> topic exchange.
/// Centralised here so HoldService and the expiry worker use identical strings.
/// </summary>
public static class RoutingKeys
{
    /// <summary>Emitted when a new hold is successfully created.</summary>
    public const string HoldCreated = "hold.created";

    /// <summary>Emitted when a hold is explicitly released by the client.</summary>
    public const string HoldReleased = "hold.released";

    /// <summary>Emitted when a hold expires (worker sweep or lazy read).</summary>
    public const string HoldExpired = "hold.expired";
}
