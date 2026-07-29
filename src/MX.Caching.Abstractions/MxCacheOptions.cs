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
