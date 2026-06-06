namespace InventoryHold.WebApi.Workers;

/// <summary>
/// Configuration for the <see cref="ExpiryWorker"/> background service.
/// Bound from the <c>ExpiryWorker</c> configuration section — nothing hardcoded (§11).
/// </summary>
public sealed class ExpiryWorkerOptions
{
    /// <summary>Configuration section key used for binding.</summary>
    public const string SectionName = "ExpiryWorker";

    /// <summary>
    /// How often the worker sweeps for Active holds whose <c>ExpiresAtUtc</c> is in
    /// the past. Shorter intervals → lower worst-case inventory restoration latency;
    /// longer intervals → fewer DB queries. Default 60 seconds.
    /// </summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(60);
}
