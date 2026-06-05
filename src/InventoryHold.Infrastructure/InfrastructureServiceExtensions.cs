using InventoryHold.Domain.Abstractions;
using InventoryHold.Infrastructure.Cache;
using InventoryHold.Infrastructure.Configuration;
using InventoryHold.Infrastructure.Messaging;
using InventoryHold.Infrastructure.Persistence.Repositories;
using InventoryHold.Infrastructure.Seeding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using RabbitMQ.Client;
using StackExchange.Redis;

namespace InventoryHold.Infrastructure;

/// <summary>
/// Extension methods for registering all Infrastructure services in the DI container.
/// Called once from <c>Program.cs</c> — keeps WebApi free of Infrastructure internals.
/// </summary>
public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// Registers MongoDB, Redis, RabbitMQ clients, repository adapters, the cache adapter,
    /// the event bus, and the inventory seeder. All connection details come from
    /// <paramref name="configuration"/> — nothing is hardcoded (§11).
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Options binding ──────────────────────────────────────────────────
        services.Configure<MongoOptions>(configuration.GetSection(MongoOptions.SectionName));
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));

        // ── MongoDB ──────────────────────────────────────────────────────────
        // Singleton: the official driver's MongoClient is thread-safe and designed for reuse.
        services.AddSingleton<IMongoClient>(sp =>
        {
            var opts = configuration.GetSection(MongoOptions.SectionName).Get<MongoOptions>()
                ?? throw new InvalidOperationException("Mongo configuration section is missing.");
            return new MongoClient(opts.ConnectionString);
        });

        services.AddScoped<IHoldRepository, MongoHoldRepository>();
        services.AddScoped<IInventoryRepository, MongoInventoryRepository>();

        // ── Redis ────────────────────────────────────────────────────────────
        // Singleton: IConnectionMultiplexer is explicitly designed to be shared.
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var opts = configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>()
                ?? throw new InvalidOperationException("Redis configuration section is missing.");
            return ConnectionMultiplexer.Connect(opts.ConnectionString);
        });

        services.AddScoped<ICache, RedisCacheAdapter>();

        // ── RabbitMQ ─────────────────────────────────────────────────────────
        // Channel created once at startup and reused (see RabbitMqEventBus remarks).
        services.AddSingleton<IChannel>(sp =>
        {
            var opts = configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>()
                ?? throw new InvalidOperationException("RabbitMq configuration section is missing.");

            // WHY: blocking .GetAwaiter().GetResult() is acceptable here because this runs
            // once during host startup before the app starts serving requests.
            return RabbitMqChannelFactory.CreateChannelAsync(opts).GetAwaiter().GetResult();
        });

        services.AddScoped<IEventBus, RabbitMqEventBus>();

        // ── Seeding ──────────────────────────────────────────────────────────
        services.AddTransient<InventorySeeder>();

        return services;
    }
}
