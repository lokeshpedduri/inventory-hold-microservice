namespace InventoryHold.Domain.Abstractions;

/// <summary>
/// Port for publishing domain events to the message broker.
/// Implementations use RabbitMQ; Domain has no RabbitMQ dependency.
/// </summary>
/// <remarks>
/// Publishing is fire-and-forget from the caller's perspective: callers must
/// always commit the DB mutation first, then publish. A publish failure must
/// be logged but must NOT roll back state — see CLAUDE.md §8.
/// </remarks>
public interface IEventBus
{
    /// <summary>
    /// Publishes <paramref name="event"/> to the topic exchange with the given
    /// <paramref name="routingKey"/>. Implementations are responsible for
    /// serialisation and delivery guarantees.
    /// </summary>
    Task PublishAsync<T>(string routingKey, T @event, CancellationToken cancellationToken = default) where T : class;
}
