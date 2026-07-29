namespace MX.Caching.Abstractions;

/// <summary>
/// Describes the cache behaviour for a single operation.
/// </summary>
public sealed record CachePolicy
{
    /// <summary>
    /// Gets the policy that explicitly bypasses caching.
    /// </summary>
    public static CachePolicy NotCached { get; } = new()
    {
        Enabled = false,
        Tier = CacheTier.None,
    };

    /// <summary>
    /// Gets a value indicating whether caching is enabled.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Gets the cache tiers used by the operation.
    /// </summary>
    public CacheTier Tier { get; init; } = CacheTier.Tiered;

    /// <summary>
    /// Gets the default lifetime applied to cached values.
    /// </summary>
    public TimeSpan Ttl { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets the optional lifetime for the in-process tier.
    /// </summary>
    public TimeSpan? L1Ttl { get; init; }

    /// <summary>
    /// Gets the optional lifetime for the distributed tier.
    /// </summary>
    public TimeSpan? L2Ttl { get; init; }

    /// <summary>
    /// Gets the tags associated with the cached value.
    /// </summary>
    public IReadOnlyCollection<string> Tags { get; init; } = [];
}
