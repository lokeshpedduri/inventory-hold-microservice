using System.Text;
using System.Text.Json;
using InventoryHold.Domain.Abstractions;
using InventoryHold.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace InventoryHold.Infrastructure.Messaging;

/// <summary>
/// RabbitMQ adapter for <see cref="IEventBus"/>. Publishes events to a durable
/// topic exchange (<c>inventory.holds</c>) so that downstream consumers can bind
/// queues to routing key patterns (e.g. <c>hold.*</c>).
/// </summary>
/// <remarks>
/// The channel is created once and reused — <see cref="IChannel"/> is not thread-safe,
/// but publishing is called sequentially from <c>HoldService</c> after each DB commit,
/// so no concurrent access occurs on a single request. For high-throughput scenarios a
/// channel pool would be appropriate; out of scope for this assignment.
/// </remarks>
internal sealed class RabbitMqEventBus : IEventBus, IAsyncDisposable
{
    private readonly IChannel _channel;
    private readonly string _exchangeName;
    private readonly ILogger<RabbitMqEventBus> _logger;

    /// <param name="channel">AMQP channel, pre-initialised by the factory (see <see cref="RabbitMqChannelFactory"/>).</param>
    /// <param name="options">RabbitMQ topology settings from config.</param>
    /// <param name="logger">Structured logger.</param>
    public RabbitMqEventBus(IChannel channel, IOptions<RabbitMqOptions> options, ILogger<RabbitMqEventBus> logger)
    {
        _channel = channel;
        _exchangeName = options.Value.ExchangeName;
        _logger = logger;
    }

    /// <summary>
    /// Serialises <paramref name="event"/> to JSON and publishes it to the topic exchange
    /// with the given <paramref name="routingKey"/>. Message is marked persistent so it
    /// survives broker restart.
    /// </summary>
    /// <remarks>
    /// Publishing happens AFTER the DB mutation in <c>HoldService</c>. A failure here
    /// is caught by <c>HoldService.PublishSafelyAsync</c> and logged — it does not roll
    /// back hold or inventory state (§8).
    /// </remarks>
    public async Task PublishAsync<T>(string routingKey, T @event, CancellationToken cancellationToken = default)
        where T : class
    {
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(@event));

        var props = new BasicProperties
        {
            // Persistent delivery mode survives a broker restart.
            DeliveryMode = DeliveryModes.Persistent,
            ContentType = "application/json",
        };

        await _channel.BasicPublishAsync(
            exchange: _exchangeName,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: props,
            body: body,
            cancellationToken: cancellationToken);

        _logger.LogDebug("Published event to exchange '{Exchange}' with routing key '{RoutingKey}'.",
            _exchangeName, routingKey);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _channel.CloseAsync();
        _channel.Dispose();
    }
}
