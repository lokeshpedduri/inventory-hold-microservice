namespace InventoryHold.Domain.Services;

/// <summary>
/// Tunables for hold lifecycle behaviour. Bound from configuration so nothing
/// is hardcoded — see CLAUDE.md §11.
/// </summary>
public sealed class HoldOptions
{
    /// <summary>Configuration section key used for binding.</summary>
    public const string SectionName = "Hold";

    /// <summary>
    /// How long a hold remains active before it is eligible for expiry.
    /// Defaults to 15 minutes; override via <c>Hold:ExpiryDuration</c> in config.
    /// </summary>
    public TimeSpan ExpiryDuration { get; set; } = TimeSpan.FromMinutes(15);
}
