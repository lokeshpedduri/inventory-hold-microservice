using InventoryHold.Contracts;
using InventoryHold.Domain.Services;
using Moq;
using Xunit;

namespace InventoryHold.UnitTests;

/// <summary>
/// Tests for <see cref="HoldService.PlaceHoldAsync"/>: happy-path creation, input
/// validation, and the multi-item compensation path (concurrency / oversell guard).
/// </summary>
public sealed class HoldServicePlaceHoldTests
{
    // ── Test 1: happy path ──────────────────────────────────────────────────

    [Fact]
    public async Task PlaceHoldAsync_ValidItemsWithSufficientStock_ReturnsActiveHoldWithCorrectItems()
    {
        // Arrange
        var fx = new HoldServiceFixture();
        var items = new (string ProductId, int Quantity)[] { ("prod-001", 3) };

        fx.InventoryRepo
            .Setup(r => r.DeductStockAsync("prod-001", 3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        fx.HoldRepo
            .Setup(r => r.SaveAsync(It.IsAny<InventoryHold.Domain.Entities.Hold>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        fx.AllowCacheInvalidate();
        // EventBus is MockBehavior.Loose — publish calls succeed without explicit setup.

        var sut = fx.BuildService();

        // Act
        var hold = await sut.PlaceHoldAsync(items);

        // Assert
        Assert.Equal(HoldStatus.Active, hold.Status);
        Assert.Single(hold.Items);
        Assert.Equal("prod-001", hold.Items[0].ProductId);
        Assert.Equal(3, hold.Items[0].Quantity);
        Assert.True(hold.ExpiresAtUtc > hold.CreatedAtUtc);
    }

    // ── Test 2: expiry duration comes from options ──────────────────────────

    [Fact]
    public async Task PlaceHoldAsync_ValidItems_ExpiresAtUtcMatchesConfiguredDuration()
    {
        // Arrange
        var fx = new HoldServiceFixture(); // ExpiryDuration == 15 minutes
        var items = new (string ProductId, int Quantity)[] { ("prod-002", 1) };

        fx.InventoryRepo
            .Setup(r => r.DeductStockAsync("prod-002", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        fx.HoldRepo
            .Setup(r => r.SaveAsync(It.IsAny<InventoryHold.Domain.Entities.Hold>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        fx.AllowCacheInvalidate();

        var before = DateTimeOffset.UtcNow;
        var sut = fx.BuildService();

        // Act
        var hold = await sut.PlaceHoldAsync(items);

        // Assert: ExpiresAtUtc should be ~15 min after creation, within a 5-second tolerance
        var expectedExpiry = before.Add(fx.ExpiryDuration);
        Assert.True(hold.ExpiresAtUtc >= expectedExpiry - TimeSpan.FromSeconds(5));
        Assert.True(hold.ExpiresAtUtc <= expectedExpiry + TimeSpan.FromSeconds(5));
    }

    // ── Test 3: concurrency / insufficient stock on first item (no prior deductions) ─

    [Fact]
    public async Task PlaceHoldAsync_FirstItemInsufficientStock_ThrowsInsufficientStockExceptionWithNoCompensation()
    {
        // Arrange
        // Simulates the race: the atomic Mongo filter finds AvailableQuantity < requested
        // (another request deducted the last units a millisecond earlier).
        var fx = new HoldServiceFixture();
        var items = new (string ProductId, int Quantity)[] { ("prod-003", 10) };

        fx.InventoryRepo
            .Setup(r => r.DeductStockAsync("prod-003", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // ← atomic guard returned null; nothing was written

        // RestoreStockAsync must NOT be called — no stock was deducted, nothing to compensate.
        // MockBehavior.Strict on the mock will fail the test if it IS called.

        var sut = fx.BuildService();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InsufficientStockException>(
            () => sut.PlaceHoldAsync(items));

        Assert.Equal("prod-003", ex.ProductId);
        Assert.Equal(10, ex.RequestedQuantity);

        // Verify that the hold was never saved (no partial state persisted).
        fx.HoldRepo.Verify(
            r => r.SaveAsync(It.IsAny<InventoryHold.Domain.Entities.Hold>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Test 4: concurrency / compensation path — the centrepiece ──────────

    [Fact]
    public async Task PlaceHoldAsync_SecondItemInsufficientStock_CompensatesFirstItemBeforeThrowing()
    {
        // Arrange
        // This test proves the multi-item compensation path described in CLAUDE.md §6:
        //   Item A deducts successfully.
        //   Item B fails (atomic guard returned null — stock gone under contention).
        //   Item A must be RESTORED before the exception surfaces, leaving inventory
        //   consistent without a DB transaction.
        var fx = new HoldServiceFixture();
        var items = new (string ProductId, int Quantity)[]
        {
            ("prod-A", 2), // succeeds
            ("prod-B", 5), // fails — simulates another concurrent request winning the race
        };

        fx.InventoryRepo
            .Setup(r => r.DeductStockAsync("prod-A", 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);  // prod-A deducted OK

        fx.InventoryRepo
            .Setup(r => r.DeductStockAsync("prod-B", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // prod-B: atomic guard says insufficient (race lost)

        // Compensation: prod-A must be restored exactly once with the exact quantity.
        fx.InventoryRepo
            .Setup(r => r.RestoreStockAsync("prod-A", 2, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable("prod-A stock was not compensated after prod-B deduction failure");

        var sut = fx.BuildService();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InsufficientStockException>(
            () => sut.PlaceHoldAsync(items));

        // The exception identifies the product that actually had insufficient stock.
        Assert.Equal("prod-B", ex.ProductId);

        // Compensation must have run — prod-A units returned to inventory.
        fx.InventoryRepo.Verify(
            r => r.RestoreStockAsync("prod-A", 2, It.IsAny<CancellationToken>()),
            Times.Once);

        // The hold must never have been persisted — no partial state in the DB.
        fx.HoldRepo.Verify(
            r => r.SaveAsync(It.IsAny<InventoryHold.Domain.Entities.Hold>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
