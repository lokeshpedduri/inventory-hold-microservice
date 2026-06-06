using InventoryHold.Domain.Abstractions;
using InventoryHold.Domain.Services;
using Microsoft.Extensions.Options;

namespace InventoryHold.WebApi.Workers;

/// <summary>
/// Background service that periodically sweeps for Active holds whose
/// <c>ExpiresAtUtc</c> has passed, transitions them to <c>Expired</c>, restores
/// inventory, and publishes <c>HoldExpired</c> events.
/// </summary>
/// <remarks>
/// This is one half of the hybrid expiry strategy (ADR-001). The other half is the
/// lazy-read check in <see cref="HoldService.GetHoldAsync"/>. Together they ensure
/// inventory is always eventually restored and the right status is always returned —
/// without relying on a destructive MongoDB TTL index.
///
/// WHY IHostedService via BackgroundService: the sweep must run independently of
/// HTTP requests so holds expire even when no client is polling. BackgroundService
/// gives us a cooperative cancellation loop without threading boilerplate.
/// </remarks>
public sealed class ExpiryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _sweepInterval;
    private readonly ILogger<ExpiryWorker> _logger;

    // WHY IServiceScopeFactory: HoldService, repositories, and the event bus are all
    // registered as Scoped. BackgroundService is a Singleton, so we cannot inject
    // Scoped services directly — we create a fresh scope on every sweep iteration
    // instead, which also ensures connections/sessions are released after each sweep.
    /// <param name="scopeFactory">DI scope factory for resolving scoped services.</param>
    /// <param name="options">Worker configuration (sweep interval from config).</param>
    /// <param name="logger">Structured logger.</param>
    public ExpiryWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<ExpiryWorkerOptions> options,
        ILogger<ExpiryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _sweepInterval = options.Value.SweepInterval;
        _logger = logger;
    }

    /// <summary>
    /// Main loop: wait <see cref="ExpiryWorkerOptions.SweepInterval"/>, sweep, repeat
    /// until the host signals cancellation (graceful shutdown).
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ExpiryWorker started. Sweep interval: {Interval}.", _sweepInterval);

        // WHY delay-first: on cold start the DB may still be initialising (especially
        // in docker-compose). A short initial delay avoids a noisy first-sweep error.
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await SweepAsync(stoppingToken);
            await Task.Delay(_sweepInterval, stoppingToken);
        }
    }

    /// <summary>
    /// Single sweep: finds all Active holds past their TTL, delegates expiry to
    /// <see cref="HoldService.ExpireHoldInternalAsync"/> for each, which atomically
    /// restores stock and publishes <c>HoldExpired</c>.
    /// </summary>
    private async Task SweepAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();

            var holdRepo = scope.ServiceProvider.GetRequiredService<IHoldRepository>();
            var holdService = scope.ServiceProvider.GetRequiredService<HoldService>();

            var now = DateTimeOffset.UtcNow;
            var expiredHolds = await holdRepo.GetExpiredActiveHoldsAsync(now, stoppingToken);

            if (expiredHolds.Count == 0)
            {
                _logger.LogDebug("ExpiryWorker sweep at {Now}: no expired holds found.", now);
                return;
            }

            _logger.LogInformation(
                "ExpiryWorker sweep at {Now}: found {Count} expired hold(s) to process.",
                now, expiredHolds.Count);

            foreach (var hold in expiredHolds)
            {
                try
                {
                    await holdService.ExpireHoldInternalAsync(hold, stoppingToken);
                    _logger.LogInformation(
                        "ExpiryWorker: expired hold {HoldId} (was due {ExpiresAt}).",
                        hold.HoldId, hold.ExpiresAtUtc);
                }
                catch (Exception ex)
                {
                    // WHY: log-and-continue rather than aborting the whole sweep.
                    // One bad hold document (e.g. corrupt item line) must not block
                    // restoration of all other expired holds in the same batch.
                    _logger.LogError(ex,
                        "ExpiryWorker: failed to expire hold {HoldId}. Will retry on next sweep.",
                        hold.HoldId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown — let the loop exit cleanly.
        }
        catch (Exception ex)
        {
            // Outer catch: repository or scope failure. Log and let the loop retry on
            // the next interval — don't crash the host over a transient DB hiccup.
            _logger.LogError(ex, "ExpiryWorker: sweep failed unexpectedly. Will retry.");
        }
    }
}
