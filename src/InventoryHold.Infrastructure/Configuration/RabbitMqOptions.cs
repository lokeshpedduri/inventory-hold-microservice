namespace InventoryHold.Infrastructure.Configuration;

/// <summary>
/// RabbitMQ connection and topology settings. Bound from the <c>RabbitMq</c>
/// configuration section.
/// </summary>
public sealed class RabbitMqOptions
{
    /// <summary>Configuration section key used for binding.</summary>
    public const string SectionName = "RabbitMq";

    /// <summary>Hostname of the RabbitMQ broker, e.g. <c>rabbitmq</c>.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>AMQP port. Default 5672.</summary>
    public int Port { get; set; } = 5672;

    /// <summary>Virtual host. Default <c>/</c>.</summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>Broker username.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Broker password.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Topic exchange name. Routing keys <c>hold.created</c>, <c>hold.released</c>,
    /// and <c>hold.expired</c> are published here (§8).
    /// </summary>
    public string ExchangeName { get; set; } = "inventory.holds";
}
