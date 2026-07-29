using System.Diagnostics.Metrics;
using MX.Caching.Abstractions;

namespace MX.Caching;

/// <summary>
/// Emits vendor-neutral metrics for MX cache operations.
/// </summary>
public sealed class MxCacheMetrics : IMxCacheMetrics, IDisposable
{
    /// <summary>
    /// Gets the meter name emitted by MX caching.
    /// </summary>
    public const string MeterName = "MX.Caching";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _hits;
    private readonly Counter<long> _misses;
    private readonly Counter<long> _evictions;

    /// <summary>
    /// Initializes a new instance of the <see cref="MxCacheMetrics"/> class.
    /// </summary>
    public MxCacheMetrics()
    {
        _hits = _meter.CreateCounter<long>("mx.cache.hits");
        _misses = _meter.CreateCounter<long>("mx.cache.misses");
        _evictions = _meter.CreateCounter<long>("mx.cache.evictions");
    }

    /// <inheritdoc/>
    public void RecordHit(CacheKey key)
    {
        _ = key;
        _hits.Add(1);
    }

    /// <inheritdoc/>
    public void RecordMiss(CacheKey key)
    {
        _ = key;
        _misses.Add(1);
    }

    /// <inheritdoc/>
    public void RecordEviction(CacheKey key)
    {
        _ = key;
        _evictions.Add(1);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _meter.Dispose();
    }
}
