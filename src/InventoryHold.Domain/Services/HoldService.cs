using InventoryHold.Contracts;
using InventoryHold.Contracts.Events;
using InventoryHold.Contracts.Responses;
using InventoryHold.Domain.Abstractions;
using InventoryHold.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InventoryHold.Domain.Services;

/// <summary>
/// Encapsulates all business logic for hold lifecycle: placement, retrieval,
/// release, and expiry. This is the only place where lifecycle rules live —
/// controllers and workers delegate here and never apply rules themselves.
/// </summary>
/// <remarks>
/// Depends only on ports (interfaces). No MongoDB, Redis, or RabbitMQ types appear
/// here — see CLAUDE.md §3 (Ports &amp; Adapters rule).
/// </remarks>
public sealed class HoldService
{
    private readonly IHoldRepository _holdRepository;
    private readonly IInventoryRepository _inventoryRepository;
    private readonly ICache _cache;
    private readonly IEventBus _eventBus;
    private readonly ILogger<HoldService> _logger;
    private readonly HoldOptions _options;

    // WHY: inventory cache key is a constant — there is only one list endpoint and
    // it is invalidated wholesale on any stock mutation (cache-aside pattern, §9).
    internal const string InventoryCacheKey = "inventory:all";

    /// <param name="holdRepository">Port for hold persistence.</param>
    /// <param name="inventoryRepository">Port for atomic stock operations.</param>
    /// <param name="cache">Port for the cache-aside inventory read layer.</param>
    /// <param name="eventBus">Port for publishing lifecycle events.</param>
    /// <param name="logger">Structured logger.</param>
    /// <param name="options">Hold lifecycle tunables (expiry duration etc.).</param>
    public HoldService(
        IHoldRepository holdRepository,
        IInventoryRepository inventoryRepository,
        ICache cache,
        IEventBus eventBus,
        ILogger<HoldService> logger,
        IOptions<HoldOptions> options)
    {
        _holdRepository = holdRepository;
        _inventoryRepository = inventoryRepository;
        _cache = cache;
        _eventBus = eventBus;
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>
    /// Places a hold on the requested items. Each item's stock is deducted atomically.
    /// If any deduction fails (insufficient stock), all already-deducted items are
    /// compensated (restored) before the failure is surfaced — leaving inventory in a
    /// consistent state without a database transaction.
    /// </summary>
    /// <param name="requestedItems">
    /// Product-quantity pairs to reserve. Must be non-empty; quantities must be &gt; 0.
    /// </param>
    /// <param name="cancellationToken">Propagated to all async I/O calls.</param>
    /// <returns>The newly created and persisted <see cref="Hold"/>.</returns>
    /// <exception cref="InsufficientStockException">
    /// Thrown when any item cannot be reserved due to insufficient stock.
    /// Inventory is fully compensated before this exception is raised.
    /// </exception>
    public async Task<Hold> PlaceHoldAsync(
        IReadOnlyList<(string ProductId, int Quantity)> requestedItems,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var holdId = Guid.NewGuid().ToString("N");

        // Track which items were successfully deducted so we can compensate on failure.
        // WHY: we iterate the list sequentially (not in parallel) so that the deducted
        // list is always an accurate prefix of requestedItems — parallel execution would
        // make partial-failure compensation ambiguous.
        var deducted = new List<(string ProductId, int Quantity)>(requestedItems.Count);

        foreach (var (productId, quantity) in requestedItems)
        {
            var success = await _inventoryRepository.DeductStockAsync(productId, quantity, cancellationToken);

            if (!success)
            {
                // WHY: compensate BEFORE throwing. The caller (controller) will map this
                // exception to a 409 and return to the client. If we threw first, the
                // already-deducted stock would remain decremented forever — a silent
                // inventory leak with no transaction to roll back.
                await CompensateAsync(deducted, cancellationToken);

                _logger.LogWarning(
                    "Hold {HoldId}: insufficient stock for product {ProductId} (requested {Quantity}). " +
                    "Compensated {CompensatedCount} already-deducted item(s).",
                    holdId, productId, quantity, deducted.Count);

                throw new InsufficientStockException(productId, quantity);
            }

            deducted.Add((productId, quantity));
        }

        // All items deducted — build the aggregate and persist it.
        var holdItems = requestedItems
            .Select(i => new HoldItem(i.ProductId, i.Quantity))
            .ToList();

        var hold = new Hold(
            holdId,
            holdItems,
            createdAtUtc: now,
            expiresAtUtc: now.Add(_options.ExpiryDuration));

        await _holdRepository.SaveAsync(hold, cancellationToken);

        // Invalidate the inventory cache so the next GET /api/inventory reflects
        // the decremented stock immediately.
        await _cache.InvalidateAsync(InventoryCacheKey, cancellationToken);

        // WHY: publish AFTER the DB write commits. A publish failure must not undo the
        // hold — the DB is the source of truth. Log and continue on error (§8).
        await PublishSafelyAsync(
            RoutingKeys.HoldCreated,
            new HoldCreatedEvent(
                hold.HoldId,
                hold.Items.Select(i => new HoldEventItemLine(i.ProductId, i.Quantity)).ToList(),
                hold.ExpiresAtUtc,
                OccurredAtUtc: now),
            cancellationToken);

        _logger.LogInformation("Hold {HoldId} created with {ItemCount} item(s), expires {ExpiresAt}.",
            hold.HoldId, hold.Items.Count, hold.ExpiresAtUtc);

        return hold;
    }

    /// <summary>
    /// Retrieves a hold by ID, applying lazy expiry if the hold has passed its
    /// <c>ExpiresAtUtc</c> but the background worker has not yet swept it.
    /// </summary>
    /// <returns>
    /// The hold (possibly with status just-transitioned to <see cref="HoldStatus.Expired"/>),
    /// or <see langword="null"/> if not found.
    /// </returns>
    public async Task<Hold?> GetHoldAsync(string holdId, CancellationToken cancellationToken = default)
    {
        var hold = await _holdRepository.GetByIdAsync(holdId, cancellationToken);
        if (hold is null)
            return null;

        // WHY: lazy expiry ensures a hold that slipped past the sweep window still
        // reads as Expired rather than Active — correctness without tightening the
        // sweep interval (ADR-001).
        if (hold.Status == HoldStatus.Active && hold.IsExpiredAt(DateTimeOffset.UtcNow))
        {
            await ExpireHoldInternalAsync(hold, cancellationToken);
        }

        return hold;
    }

    /// <summary>
    /// Explicitly releases a hold at the client's request. Restores all reserved
    /// stock and publishes <c>hold.released</c>.
    /// </summary>
    /// <exception cref="HoldNotFoundException">Thrown when no hold exists for <paramref name="holdId"/>.</exception>
    /// <exception cref="HoldAlreadyTerminalException">Thrown when the hold is already Released or Expired.</exception>
    public async Task<Hold> ReleaseHoldAsync(string holdId, CancellationToken cancellationToken = default)
    {
        var hold = await _holdRepository.GetByIdAsync(holdId, cancellationToken);
        if (hold is null)
            throw new HoldNotFoundException(holdId);

        // WHY: check wall-clock expiry before attempting release. If the hold expired
        // between sweeps and the client tries to release it, we transition to Expired
        // (not Released) — the status semantics must match what actually happened.
        if (hold.Status == HoldStatus.Active && hold.IsExpiredAt(DateTimeOffset.UtcNow))
        {
            await ExpireHoldInternalAsync(hold, cancellationToken);
            // Re-throw as terminal: client cannot release an already-expired hold.
            throw new HoldAlreadyTerminalException(holdId, hold.Status);
        }

        // This throws InvalidOperationException (wrapped below) if already terminal.
        try { hold.Release(); }
        catch (InvalidOperationException ex)
        {
            throw new HoldAlreadyTerminalException(holdId, hold.Status, ex);
        }

        await _holdRepository.UpdateStatusAsync(hold, cancellationToken);

        foreach (var item in hold.Items)
            await _inventoryRepository.RestoreStockAsync(item.ProductId, item.Quantity, cancellationToken);

        await _cache.InvalidateAsync(InventoryCacheKey, cancellationToken);

        await PublishSafelyAsync(
            RoutingKeys.HoldReleased,
            new HoldReleasedEvent(
                hold.HoldId,
                hold.Items.Select(i => new HoldEventItemLine(i.ProductId, i.Quantity)).ToList(),
                Reason: "client_requested",
                OccurredAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);

        _logger.LogInformation("Hold {HoldId} released.", holdId);
        return hold;
    }

    /// <summary>
    /// Expires a single active hold — called by the background worker for each hold
    /// it finds past its TTL, and called internally by the lazy-read path.
    /// Restores all reserved stock and publishes <c>hold.expired</c>.
    /// </summary>
    /// <remarks>
    /// The hold must already be loaded by the caller; this method does not re-fetch it.
    /// </remarks>
    public async Task ExpireHoldInternalAsync(Hold hold, CancellationToken cancellationToken = default)
    {
        hold.Expire();

        await _holdRepository.UpdateStatusAsync(hold, cancellationToken);

        foreach (var item in hold.Items)
            await _inventoryRepository.RestoreStockAsync(item.ProductId, item.Quantity, cancellationToken);

        await _cache.InvalidateAsync(InventoryCacheKey, cancellationToken);

        await PublishSafelyAsync(
            RoutingKeys.HoldExpired,
            new HoldExpiredEvent(
                hold.HoldId,
                hold.Items.Select(i => new HoldEventItemLine(i.ProductId, i.Quantity)).ToList(),
                ExpiredAtUtc: hold.ExpiresAtUtc,
                OccurredAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);

        _logger.LogInformation("Hold {HoldId} expired (TTL was {ExpiresAt}).", hold.HoldId, hold.ExpiresAtUtc);
    }

    /// <summary>
    /// Returns all inventory items, consulting the cache first (cache-aside).
    /// </summary>
    /// <remarks>
    /// WHY we cache <see cref="InventoryItemResponse"/> records rather than domain
    /// <see cref="InventoryItem"/> entities: <c>System.Text.Json</c> cannot round-trip
    /// entities that have <c>private set</c> properties and no parameterless constructor.
    /// <see cref="InventoryItemResponse"/> is a plain <c>record</c> — fully serialisable.
    /// The mapping from entity → response happens once (on cache miss) and the response
    /// shape is stable, so this is safe and efficient.
    /// </remarks>
    public async Task<IReadOnlyList<InventoryItem>> GetInventoryAsync(CancellationToken cancellationToken = default)
    {
        // Cache stores the serialisable response DTO, not the domain entity.
        var cached = await _cache.GetAsync<List<InventoryItemResponse>>(InventoryCacheKey, cancellationToken);
        if (cached is not null)
        {
            // Re-hydrate back to domain entities for consistency with the return type.
            return cached.Select(r => new InventoryItem(r.ProductId, r.Name, r.AvailableQuantity)).ToList();
        }

        var items = await _inventoryRepository.GetAllAsync(cancellationToken);
        var responses = items
            .Select(i => new InventoryItemResponse(i.ProductId, i.Name, i.AvailableQuantity))
            .ToList();

        // WHY: cache TTL is a safety net in case an invalidation is missed (e.g.
        // process crash between write and invalidate). The primary consistency
        // mechanism is explicit InvalidateAsync after every mutation (§9).
        await _cache.SetAsync(InventoryCacheKey, responses, TimeSpan.FromSeconds(30), cancellationToken);

        return items;
    }

    // Restores stock for every item in `deducted`. Called on partial hold failure
    // to leave inventory consistent without a database transaction.
    private async Task CompensateAsync(
        IReadOnlyList<(string ProductId, int Quantity)> deducted,
        CancellationToken cancellationToken)
    {
        foreach (var (productId, quantity) in deducted)
        {
            try
            {
                await _inventoryRepository.RestoreStockAsync(productId, quantity, cancellationToken);
            }
            catch (Exception ex)
            {
                // WHY: log and continue rather than rethrowing. A compensation failure
                // is serious (inventory leak) but rethrowing here would mask the original
                // InsufficientStockException. The caller gets the right error; ops gets an
                // alert from the log. A reconciliation job or idempotent retry can fix this.
                _logger.LogError(ex,
                    "COMPENSATION FAILURE: could not restore {Quantity} unit(s) of product {ProductId}. " +
                    "Manual reconciliation may be required.",
                    quantity, productId);
            }
        }
    }

    // Publishes an event, swallowing exceptions so that a broker outage never
    // corrupts hold or inventory state. DB write is the source of truth (§8).
    private async Task PublishSafelyAsync<T>(string routingKey, T @event, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            await _eventBus.PublishAsync(routingKey, @event, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish event with routing key '{RoutingKey}'. " +
                "State is consistent; event delivery was not guaranteed.", routingKey);
        }
    }
}
