namespace MX.Caching.Abstractions;

/// <summary>
/// Selects the cache tiers available to a cache operation.
/// </summary>
public enum CacheTier
{
    /// <summary>
    /// Disables caching for the operation.
    /// </summary>
    None,

    /// <summary>
    /// Uses the in-process cache tier only.
    /// </summary>
    InProcess,

    /// <summary>
    /// Uses the distributed cache tier only.
    /// </summary>
    Distributed,

    /// <summary>
    /// Uses both in-process and distributed cache tiers.
    /// </summary>
    Tiered,
}
