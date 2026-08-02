using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Caching.Distributed;

namespace MX.Caching.TableStorage;

/// <summary>
/// Stores distributed cache entries in Azure Table Storage.
/// </summary>
/// <remarks>
/// <para>The underlying table is created lazily on first use. A transient failure during
/// table creation does not permanently poison the instance; subsequent operations will
/// reattempt initialization.</para>
/// <para>All cache values are limited to <c>64 KB</c> (the Azure Table binary-property
/// maximum). Writes that exceed this limit throw <see cref="CacheValueTooLargeException"/>
/// before any network call is made.</para>
/// <para>All cache entries share the <c>cache</c> partition key. In high-throughput
/// scenarios consider sharding entries across multiple partitions in a future work item
/// once a backward-compatible migration strategy is in place.</para>
/// <para>Expired entries are cleaned up lazily when they are read. A host-driven bulk
/// cleanup mechanism (e.g. Azure Table Storage lifecycle rules or a background job) is
/// recommended for workloads that write many short-lived entries.</para>
/// </remarks>
public sealed class TableStorageDistributedCache : IDistributedCache, IDisposable
{
    private const string PartitionKey = "cache";
    private const string ValuePropertyName = "Value";
    private const string ExpiresUtcPropertyName = "ExpiresUtc";
    private const string AbsoluteExpirationTicksPropertyName = "AbsoluteExpirationTicks";
    private const string SlidingExpirationSecondsPropertyName = "SlidingExpirationSeconds";
    private const string CacheEntryVersionPropertyName = "CacheEntryVersion";
    private const long CurrentCacheEntryVersion = 1;

    /// <summary>
    /// Gets the maximum permitted byte length for a single cache entry value.
    /// This reflects the Azure Table Storage binary-property limit.
    /// </summary>
    public const int MaximumValueLength = 64 * 1024;

    private readonly TableClient _tableClient;
    private readonly TableStorageCacheMetrics? _metrics;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile bool _initialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="TableStorageDistributedCache"/> class.
    /// </summary>
    /// <param name="tableServiceClient">The Azure Table service client.</param>
    /// <param name="tableName">The table used for cache entries.</param>
    public TableStorageDistributedCache(TableServiceClient tableServiceClient, string tableName)
        : this(tableServiceClient, tableName, null)
    {
    }

    internal TableStorageDistributedCache(
        TableServiceClient tableServiceClient,
        string tableName,
        TableStorageCacheMetrics? metrics)
    {
        ArgumentNullException.ThrowIfNull(tableServiceClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        _tableClient = tableServiceClient.GetTableClient(tableName);
        _metrics = metrics;
    }

    /// <inheritdoc/>
    public byte[]? Get(string key)
    {
        return GetAsync(key).GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await EnsureInitializedAsync(token).ConfigureAwait(false);

        var start = Stopwatch.GetTimestamp();
        var hasError = false;
        try
        {
            var response = await _tableClient.GetEntityAsync<TableEntity>(
                PartitionKey,
                CreateRowKey(key),
                cancellationToken: token).ConfigureAwait(false);
            var entity = response.Value;

            if (IsExpired(entity))
            {
                _ = await _tableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, ETag.All, token).ConfigureAwait(false);
                return null;
            }

            return entity.GetBinary(ValuePropertyName);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
        catch
        {
            hasError = true;
            throw;
        }
        finally
        {
            if (_metrics is not null)
            {
                _metrics.RecordDuration("get", Stopwatch.GetElapsedTime(start).TotalSeconds);
                if (hasError)
                {
                    _metrics.RecordError("get");
                }
            }
        }
    }

    /// <inheritdoc/>
    public void Refresh(string key)
    {
        RefreshAsync(key).GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async Task RefreshAsync(string key, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await EnsureInitializedAsync(token).ConfigureAwait(false);

        var start = Stopwatch.GetTimestamp();
        var hasError = false;
        try
        {
            var response = await _tableClient.GetEntityAsync<TableEntity>(
                PartitionKey,
                CreateRowKey(key),
                cancellationToken: token).ConfigureAwait(false);
            var entity = response.Value;
            var slidingExpirationSeconds = entity.GetInt64(SlidingExpirationSecondsPropertyName);

            if (slidingExpirationSeconds is null || IsExpired(entity))
            {
                return;
            }

            var expiresUtc = DateTimeOffset.UtcNow.AddSeconds(slidingExpirationSeconds.Value);
            var absoluteExpirationTicks = entity.GetInt64(AbsoluteExpirationTicksPropertyName);

            if (absoluteExpirationTicks is null && entity.GetInt64(CacheEntryVersionPropertyName) is null)
            {
                return;
            }

            entity[ExpiresUtcPropertyName] = absoluteExpirationTicks is null
                ? expiresUtc
                : Min(expiresUtc, new DateTimeOffset(absoluteExpirationTicks.Value, TimeSpan.Zero));
            _ = await _tableClient.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, token).ConfigureAwait(false);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
        }
        catch
        {
            hasError = true;
            throw;
        }
        finally
        {
            if (_metrics is not null)
            {
                _metrics.RecordDuration("refresh", Stopwatch.GetElapsedTime(start).TotalSeconds);
                if (hasError)
                {
                    _metrics.RecordError("refresh");
                }
            }
        }
    }

    /// <inheritdoc/>
    public void Remove(string key)
    {
        RemoveAsync(key).GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await EnsureInitializedAsync(token).ConfigureAwait(false);

        var start = Stopwatch.GetTimestamp();
        var hasError = false;
        try
        {
            _ = await _tableClient.DeleteEntityAsync(PartitionKey, CreateRowKey(key), ETag.All, token).ConfigureAwait(false);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
        }
        catch
        {
            hasError = true;
            throw;
        }
        finally
        {
            if (_metrics is not null)
            {
                _metrics.RecordDuration("remove", Stopwatch.GetElapsedTime(start).TotalSeconds);
                if (hasError)
                {
                    _metrics.RecordError("remove");
                }
            }
        }
    }

    /// <inheritdoc/>
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        SetAsync(key, value, options).GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public async Task SetAsync(
        string key,
        byte[] value,
        DistributedCacheEntryOptions options,
        CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);

        if (value.Length > MaximumValueLength)
        {
            _metrics?.RecordOversizeRejection();
            throw new CacheValueTooLargeException(value.Length, MaximumValueLength);
        }

        await EnsureInitializedAsync(token).ConfigureAwait(false);

        var start = Stopwatch.GetTimestamp();
        var hasError = false;
        try
        {
            var (expiresUtc, absoluteExpiresUtc, slidingExpirationSeconds) = GetExpiration(options);
            var entity = new TableEntity(PartitionKey, CreateRowKey(key))
            {
                [ValuePropertyName] = value,
                [ExpiresUtcPropertyName] = expiresUtc,
                [AbsoluteExpirationTicksPropertyName] = absoluteExpiresUtc?.UtcTicks,
                [SlidingExpirationSecondsPropertyName] = slidingExpirationSeconds,
                [CacheEntryVersionPropertyName] = CurrentCacheEntryVersion,
            };

            _ = await _tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, token).ConfigureAwait(false);
        }
        catch
        {
            hasError = true;
            throw;
        }
        finally
        {
            if (_metrics is not null)
            {
                _metrics.RecordDuration("set", Stopwatch.GetElapsedTime(start).TotalSeconds);
                if (hasError)
                {
                    _metrics.RecordError("set");
                }
            }
        }
    }

    /// <summary>
    /// Ensures the backing table exists before the first storage operation. Uses double-checked
    /// locking so initialization is attempted at most once per instance under normal conditions.
    /// A failure leaves the instance in an uninitialised state so the next caller retries.
    /// </summary>
    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            _ = await _tableClient.CreateIfNotExistsAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _ = _initLock.Release();
        }
    }

    private static string CreateRowKey(string key)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    private static bool IsExpired(TableEntity entity)
    {
        var expiresUtc = entity.GetDateTimeOffset(ExpiresUtcPropertyName);
        return expiresUtc is not null && expiresUtc <= DateTimeOffset.UtcNow;
    }

    private static (DateTimeOffset? ExpiresUtc, DateTimeOffset? AbsoluteExpiresUtc, long? SlidingExpirationSeconds) GetExpiration(
        DistributedCacheEntryOptions options)
    {
        var now = DateTimeOffset.UtcNow;
        var absoluteExpiration = options.AbsoluteExpiration;
        var relativeExpiration = options.AbsoluteExpirationRelativeToNow;
        var slidingExpiration = options.SlidingExpiration;
        var absoluteExpiresUtc = absoluteExpiration
            ?? (relativeExpiration is null ? null : now.Add(relativeExpiration.Value));

        var expiresUtc = absoluteExpiresUtc
            ?? (slidingExpiration is null ? null : now.Add(slidingExpiration.Value));

        if (slidingExpiration is not null && absoluteExpiresUtc is not null)
        {
            expiresUtc = Min(absoluteExpiresUtc.Value, now.Add(slidingExpiration.Value));
        }

        long? slidingExpirationSeconds = slidingExpiration is null
            ? null
            : (long)Math.Ceiling(slidingExpiration.Value.TotalSeconds);

        return (expiresUtc, absoluteExpiresUtc, slidingExpirationSeconds);
    }

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second)
    {
        return first <= second ? first : second;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _initLock.Dispose();
    }
}
