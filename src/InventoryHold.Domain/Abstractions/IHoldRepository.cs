using InventoryHold.Domain.Entities;

namespace InventoryHold.Domain.Abstractions;

/// <summary>
/// Port for persisting and retrieving <see cref="Hold"/> aggregates.
/// Implementations live in Infrastructure and use MongoDB; callers in Domain and
/// WebApi must not depend on any MongoDB type.
/// </summary>
public interface IHoldRepository
{
    /// <summary>
    /// Persists a newly constructed hold. The hold must not already exist.
    /// </summary>
    Task SaveAsync(Hold hold, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a hold by its identifier, or <see langword="null"/> if not found.
    /// </summary>
    Task<Hold?> GetByIdAsync(string holdId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a status change (Release or Expire) to an existing hold.
    /// </summary>
    Task UpdateStatusAsync(Hold hold, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all holds ordered by status priority (Active first) then by
    /// <c>CreatedAtUtc</c> descending. Used by the frontend list endpoint.
    /// No lazy expiry is applied — use <see cref="GetByIdAsync"/> for that.
    /// </summary>
    Task<IReadOnlyList<Hold>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all holds in <see cref="Contracts.HoldStatus.Active"/> state whose
    /// <c>ExpiresAtUtc</c> is before <paramref name="asOf"/>. Used by the background
    /// expiry worker to find holds that need to be transitioned to Expired.
    /// </summary>
    Task<IReadOnlyList<Hold>> GetExpiredActiveHoldsAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default);
}
