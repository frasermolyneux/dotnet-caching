namespace MX.Caching.Abstractions;

/// <summary>
/// Selects the backend used by MX caching.
/// </summary>
public enum CacheBackend
{
    /// <summary>
    /// Uses the in-process memory backend.
    /// </summary>
    Memory,

    /// <summary>
    /// Uses Azure Table Storage as the distributed backend.
    /// </summary>
    TableStorage,

    /// <summary>
    /// Uses Redis as the distributed backend.
    /// </summary>
    Redis,

    /// <summary>
    /// Uses Cosmos DB as the distributed backend.
    /// </summary>
    Cosmos,
}

/// <summary>
/// Represents configuration for MX caching.
/// </summary>
public sealed class MxCacheOptions
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "MxCaching";

    /// <summary>
    /// Gets or sets the configured cache backend.
    /// </summary>
    public CacheBackend Backend { get; set; } = CacheBackend.Memory;

    /// <summary>
    /// Gets or sets the configuration for Azure Table Storage.
    /// </summary>
    public TableStorageCacheOptions TableStorage { get; set; } = new();

    /// <summary>
    /// Gets or sets the policy values applied to every cacheable operation.
    /// </summary>
    public CachePolicyOptions Policy { get; set; } = new();

    /// <summary>
    /// Gets or sets policy values applied to individual operations, keyed by <c>Client:Method</c>.
    /// </summary>
    public Dictionary<string, CachePolicyOptions> OperationPolicies { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Represents optional policy values that overlay a lower-precedence cache policy.
/// </summary>
public sealed class CachePolicyOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether caching is enabled.
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// Gets or sets the cache tiers used by the operation.
    /// </summary>
    public CacheTier? Tier { get; set; }

    /// <summary>
    /// Gets or sets the default lifetime applied to cached values.
    /// </summary>
    public TimeSpan? Ttl { get; set; }

    /// <summary>
    /// Gets or sets the optional lifetime for the in-process tier.
    /// </summary>
    public TimeSpan? L1Ttl { get; set; }

    /// <summary>
    /// Gets or sets the optional lifetime for the distributed tier.
    /// </summary>
    public TimeSpan? L2Ttl { get; set; }

    /// <summary>
    /// Gets or sets the tags associated with the cached value.
    /// </summary>
    public string[]? Tags { get; set; }
}

/// <summary>
/// Represents Azure Table Storage configuration for MX caching.
/// </summary>
public sealed class TableStorageCacheOptions
{
    /// <summary>
    /// Gets or sets the Azure Table Storage service endpoint.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>
    /// Gets or sets the table name used for distributed cache entries.
    /// </summary>
    public string TableName { get; set; } = "mxcaching";
}
