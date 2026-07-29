namespace MX.Caching.Abstractions;

/// <summary>
/// Records cache outcomes for application observability.
/// </summary>
public interface IMxCacheMetrics
{
    /// <summary>
    /// Records a cache hit.
    /// </summary>
    /// <param name="key">The requested cache key.</param>
    void RecordHit(CacheKey key);

    /// <summary>
    /// Records a cache miss.
    /// </summary>
    /// <param name="key">The requested cache key.</param>
    void RecordMiss(CacheKey key);

    /// <summary>
    /// Records a cache eviction.
    /// </summary>
    /// <param name="key">The evicted cache key.</param>
    void RecordEviction(CacheKey key);
}
