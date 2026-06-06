using InventoryHold.Domain.Abstractions;
using InventoryHold.Domain.Entities;
using InventoryHold.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace InventoryHold.UnitTests;

/// <summary>
/// Shared test fixture that wires up a <see cref="HoldService"/> with all four ports
/// mocked via Moq. Each test class instantiates this directly so the mocks are fresh
/// per test — no shared state, no cross-test interference.
///
/// Mock behaviour choices:
/// - <see cref="HoldRepo"/>, <see cref="InventoryRepo"/>, <see cref="Cache"/>: Strict —
///   any unexpected call fails fast, making accidental calls visible.
/// - <see cref="EventBus"/>: Loose — <c>PublishAsync&lt;T&gt;</c> is generic and fire-and-
///   forget; tests focus on inventory/hold mutations, not publish payloads. Loose means
///   unexpected publish calls return a completed Task rather than throwing.
/// </summary>
internal sealed class HoldServiceFixture
{
    public Mock<IHoldRepository> HoldRepo { get; } = new(MockBehavior.Strict);
    public Mock<IInventoryRepository> InventoryRepo { get; } = new(MockBehavior.Strict);
    public Mock<ICache> Cache { get; } = new(MockBehavior.Strict);

    // WHY: Loose here because IEventBus.PublishAsync<T> is open-generic. Setting up
    // MockBehavior.Strict for every concrete event type in every test would be noise —
    // the behaviour under test is inventory + hold state, not event payload content.
    public Mock<IEventBus> EventBus { get; } = new(MockBehavior.Loose);

    /// <summary>Expiry duration used by the service under test. Exposed so tests can assert against it.</summary>
    public TimeSpan ExpiryDuration { get; } = TimeSpan.FromMinutes(15);

    public HoldService BuildService()
    {
        var options = Options.Create(new HoldOptions { ExpiryDuration = ExpiryDuration });
        return new HoldService(
            HoldRepo.Object,
            InventoryRepo.Object,
            Cache.Object,
            EventBus.Object,
            NullLogger<HoldService>.Instance,
            options);
    }

    // ── Convenience helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Returns a valid, freshly-Active hold with one item line. Its ExpiresAtUtc is
    /// 15 minutes in the future so it will NOT be treated as expired by default.
    /// </summary>
    public static Hold ActiveHold(
        string holdId = "hold-abc",
        string productId = "prod-001",
        int quantity = 2)
    {
        var now = DateTimeOffset.UtcNow;
        return new Hold(
            holdId,
            [new HoldItem(productId, quantity)],
            createdAtUtc: now,
            expiresAtUtc: now.AddMinutes(15));
    }

    /// <summary>
    /// Returns a hold whose <c>ExpiresAtUtc</c> is 1 second in the past, so
    /// <see cref="Hold.IsExpiredAt"/> returns <see langword="true"/> immediately.
    /// Used to exercise the lazy-expiry path on GET.
    /// </summary>
    public static Hold AlreadyExpiredHold(
        string holdId = "hold-expired",
        string productId = "prod-001",
        int quantity = 2)
    {
        var past = DateTimeOffset.UtcNow.AddSeconds(-1);
        return new Hold(
            holdId,
            [new HoldItem(productId, quantity)],
            createdAtUtc: past.AddMinutes(-15),
            expiresAtUtc: past);
    }

    /// <summary>Sets up Cache.InvalidateAsync to accept any key silently.</summary>
    public void AllowCacheInvalidate()
    {
        Cache
            .Setup(c => c.InvalidateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }
}
