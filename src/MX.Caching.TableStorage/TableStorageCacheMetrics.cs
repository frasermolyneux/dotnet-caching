using System.Diagnostics.Metrics;

namespace MX.Caching.TableStorage;

/// <summary>
/// Emits backend-level metrics for Azure Table Storage cache operations.
/// These are published under the <c>MX.Caching</c> meter alongside logical cache metrics.
/// </summary>
internal sealed class TableStorageCacheMetrics : IDisposable
{
    private readonly Meter _meter = new("MX.Caching");
    private readonly Histogram<double> _operationDuration;
    private readonly Counter<long> _operationErrors;
    private readonly Counter<long> _oversizeRejections;
    private readonly Counter<long> _generationRetries;

    /// <summary>
    /// Initializes a new instance of the <see cref="TableStorageCacheMetrics"/> class.
    /// </summary>
    public TableStorageCacheMetrics()
    {
        _operationDuration = _meter.CreateHistogram<double>(
            "mx.cache.storage.operation.duration",
            unit: "s",
            description: "Duration of Azure Table Storage cache backend operations.");

        _operationErrors = _meter.CreateCounter<long>(
            "mx.cache.storage.operation.errors",
            description: "Number of Azure Table Storage cache backend operations that failed with a storage error.");

        _oversizeRejections = _meter.CreateCounter<long>(
            "mx.cache.storage.oversize_rejections",
            description: "Number of cache set operations rejected because the value exceeded the storage limit.");

        _generationRetries = _meter.CreateCounter<long>(
            "mx.cache.storage.generation_retries",
            description: "Number of optimistic-concurrency retries when advancing a cache tag generation.");
    }

    /// <summary>
    /// Records the duration of a completed storage operation.
    /// </summary>
    /// <param name="operation">The operation name (e.g. <c>get</c>, <c>set</c>). Must be low-cardinality.</param>
    /// <param name="durationSeconds">The elapsed duration in seconds.</param>
    public void RecordDuration(string operation, double durationSeconds)
    {
        _operationDuration.Record(durationSeconds, new KeyValuePair<string, object?>("operation", operation));
    }

    /// <summary>
    /// Records a storage error for an operation.
    /// </summary>
    /// <param name="operation">The operation name that failed.</param>
    public void RecordError(string operation)
    {
        _operationErrors.Add(1, new KeyValuePair<string, object?>("operation", operation));
    }

    /// <summary>
    /// Records a rejected set operation because the value exceeded the size limit.
    /// </summary>
    public void RecordOversizeRejection()
    {
        _oversizeRejections.Add(1);
    }

    /// <summary>
    /// Records one optimistic-concurrency retry while advancing a tag generation.
    /// </summary>
    public void RecordGenerationRetry()
    {
        _generationRetries.Add(1);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _meter.Dispose();
    }
}
