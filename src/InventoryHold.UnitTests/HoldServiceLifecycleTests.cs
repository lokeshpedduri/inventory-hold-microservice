using InventoryHold.Contracts;
using InventoryHold.Domain.Services;
using Moq;
using Xunit;

namespace InventoryHold.UnitTests;

/// <summary>
/// Tests for the full hold lifecycle (Get, Release) and lifecycle guard rules.
/// </summary>
public sealed class HoldServiceLifecycleTests
{
    // ── Test 5: Get returns null for unknown hold ───────────────────────────

    [Fact]
    public async Task GetHoldAsync_UnknownHoldId_ReturnsNull()
    {
        // Arrange
        var fx = new HoldServiceFixture();

        fx.HoldRepo
            .Setup(r => r.GetByIdAsync("missing-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync((InventoryHold.Domain.Entities.Hold?)null);

        var sut = fx.BuildService();

        // Act
        var result = await sut.GetHoldAsync("missing-id");

        // Assert
        Assert.Null(result);
    }

    // ── Test 6: Release transitions Active → Released and restores stock ───

    [Fact]
    public async Task ReleaseHoldAsync_ActiveHold_TransitionsToReleasedAndRestoresStock()
    {
        // Arrange
        var fx = new HoldServiceFixture();
        var hold = HoldServiceFixture.ActiveHold(productId: "prod-001", quantity: 3);

        fx.HoldRepo
            .Setup(r => r.GetByIdAsync(hold.HoldId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hold);

        fx.HoldRepo
            .Setup(r => r.UpdateStatusAsync(hold, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        fx.InventoryRepo
            .Setup(r => r.RestoreStockAsync("prod-001", 3, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        fx.AllowCacheInvalidate();
        // EventBus is MockBehavior.Loose — publish calls succeed without explicit setup.

        var sut = fx.BuildService();

        // Act
        var result = await sut.ReleaseHoldAsync(hold.HoldId);

        // Assert: status transitioned
        Assert.Equal(HoldStatus.Released, result.Status);

        // Assert: stock was restored for the released item
        fx.InventoryRepo.Verify(
            r => r.RestoreStockAsync("prod-001", 3, It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert: cache invalidated so next inventory read reflects restored units
        fx.Cache.Verify(
            c => c.InvalidateAsync(HoldService.InventoryCacheKey, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Test 7: Release of an already-Released hold → 409 path ────────────

    [Fact]
    public async Task ReleaseHoldAsync_AlreadyReleasedHold_ThrowsHoldAlreadyTerminalException()
    {
        // Arrange
        var fx = new HoldServiceFixture();
        var hold = HoldServiceFixture.ActiveHold();
        hold.Release(); // put it into terminal state before the service sees it

        fx.HoldRepo
            .Setup(r => r.GetByIdAsync(hold.HoldId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hold);

        var sut = fx.BuildService();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<HoldAlreadyTerminalException>(
            () => sut.ReleaseHoldAsync(hold.HoldId));

        Assert.Equal(hold.HoldId, ex.HoldId);
        Assert.Equal(HoldStatus.Released, ex.CurrentStatus);

        // Verify no stock was touched and no DB write occurred — idempotency of error path
        fx.InventoryRepo.Verify(
            r => r.RestoreStockAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        fx.HoldRepo.Verify(
            r => r.UpdateStatusAsync(It.IsAny<InventoryHold.Domain.Entities.Hold>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Test 8: Release of missing hold → 404 path ─────────────────────────

    [Fact]
    public async Task ReleaseHoldAsync_NonExistentHold_ThrowsHoldNotFoundException()
    {
        // Arrange
        var fx = new HoldServiceFixture();

        fx.HoldRepo
            .Setup(r => r.GetByIdAsync("ghost-id", It.IsAny<CancellationToken>()))
            .ReturnsAsync((InventoryHold.Domain.Entities.Hold?)null);

        var sut = fx.BuildService();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<HoldNotFoundException>(
            () => sut.ReleaseHoldAsync("ghost-id"));

        Assert.Equal("ghost-id", ex.HoldId);
    }

    // ── Test 9: lazy expiry on GET — expired-but-Active hold is transitioned ─

    [Fact]
    public async Task GetHoldAsync_ActiveHoldPastExpiryTime_LazilyTransitionsToExpiredAndRestoresStock()
    {
        // Arrange
        // The hold's ExpiresAtUtc is 1 second in the past. The background worker hasn't
        // swept it yet. The lazy-read path in GetHoldAsync must catch this and expire it.
        var fx = new HoldServiceFixture();
        var hold = HoldServiceFixture.AlreadyExpiredHold(productId: "prod-007", quantity: 1);

        fx.HoldRepo
            .Setup(r => r.GetByIdAsync(hold.HoldId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hold);

        // The lazy path calls ExpireHoldInternalAsync which needs UpdateStatus + RestoreStock.
        fx.HoldRepo
            .Setup(r => r.UpdateStatusAsync(hold, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        fx.InventoryRepo
            .Setup(r => r.RestoreStockAsync("prod-007", 1, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        fx.AllowCacheInvalidate();
        // EventBus is MockBehavior.Loose — publish calls succeed without explicit setup.

        var sut = fx.BuildService();

        // Act
        var result = await sut.GetHoldAsync(hold.HoldId);

        // Assert: returned hold reflects the true expired state (not stale Active)
        Assert.NotNull(result);
        Assert.Equal(HoldStatus.Expired, result!.Status);

        // Verify stock was restored as part of lazy expiry
        fx.InventoryRepo.Verify(
            r => r.RestoreStockAsync("prod-007", 1, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Test 10: cache-aside — GetInventoryAsync serves from cache on hit ──

    [Fact]
    public async Task GetInventoryAsync_CacheHit_ReturnsCachedDataWithoutHittingRepository()
    {
        // Arrange
        var fx = new HoldServiceFixture();
        var cachedItems = new List<InventoryHold.Domain.Entities.InventoryItem>
        {
            new("prod-001", "Headphones", 42),
        };

        fx.Cache
            .Setup(c => c.GetAsync<List<InventoryHold.Domain.Entities.InventoryItem>>(
                HoldService.InventoryCacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedItems);

        var sut = fx.BuildService();

        // Act
        var result = await sut.GetInventoryAsync();

        // Assert: returned the cached data
        Assert.Single(result);
        Assert.Equal("prod-001", result[0].ProductId);

        // Repository must NOT have been called — cache hit should short-circuit
        fx.InventoryRepo.Verify(
            r => r.GetAllAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
