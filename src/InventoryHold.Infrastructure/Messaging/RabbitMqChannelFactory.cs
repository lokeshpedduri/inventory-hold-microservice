using InventoryHold.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace InventoryHold.Infrastructure.Messaging;

/// <summary>
/// Creates and configures the AMQP connection and channel used by <see cref="RabbitMqEventBus"/>.
/// Declares the durable topic exchange on startup so the broker topology is self-initialising —
/// no manual broker configuration required.
/// </summary>
internal static class RabbitMqChannelFactory
{
    /// <summary>
    /// Opens a connection to the broker, declares the <c>inventory.holds</c> topic exchange
    /// as durable, and returns the ready channel.
    /// </summary>
    /// <param name="options">RabbitMQ settings from config — host, port, credentials, exchange name.</param>
    public static async Task<IChannel> CreateChannelAsync(RabbitMqOptions options)
    {
        var factory = new ConnectionFactory
        {
            HostName = options.Host,
            Port = options.Port,
            VirtualHost = options.VirtualHost,
            UserName = options.Username,
            Password = options.Password,
        };

        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();

        // Declare the topic exchange as durable so it survives broker restarts.
        // Idempotent: if the exchange already exists with the same arguments, this is a no-op.
        await channel.ExchangeDeclareAsync(
            exchange: options.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false);

        return channel;
    }
}
