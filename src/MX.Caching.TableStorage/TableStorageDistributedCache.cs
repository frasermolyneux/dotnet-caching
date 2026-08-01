using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Caching.Distributed;

namespace MX.Caching.TableStorage;

/// <summary>
/// Stores distributed cache entries in Azure Table Storage.
/// </summary>
public sealed class TableStorageDistributedCache : IDistributedCache
{
    private const string PartitionKey = "cache";
    private const string ValuePropertyName = "Value";
    private const string ExpiresUtcPropertyName = "ExpiresUtc";
    private const string AbsoluteExpirationTicksPropertyName = "AbsoluteExpirationTicks";
    private const string SlidingExpirationSecondsPropertyName = "SlidingExpirationSeconds";
    private const string CacheEntryVersionPropertyName = "CacheEntryVersion";
    private const long CurrentCacheEntryVersion = 1;
    private const int MaximumValueLength = 64 * 1024;

    private readonly TableClient _tableClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="TableStorageDistributedCache"/> class.
    /// </summary>
    /// <param name="tableServiceClient">The Azure Table service client.</param>
    /// <param name="tableName">The table used for cache entries.</param>
    public TableStorageDistributedCache(TableServiceClient tableServiceClient, string tableName)
    {
        ArgumentNullException.ThrowIfNull(tableServiceClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        _tableClient = tableServiceClient.GetTableClient(tableName);
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
        await EnsureTableExistsAsync(token).ConfigureAwait(false);

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
        await EnsureTableExistsAsync(token).ConfigureAwait(false);

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
        await EnsureTableExistsAsync(token).ConfigureAwait(false);

        try
        {
            _ = await _tableClient.DeleteEntityAsync(PartitionKey, CreateRowKey(key), ETag.All, token).ConfigureAwait(false);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
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
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value.Length,
                $"Azure Table Storage cache values cannot exceed {MaximumValueLength} bytes.");
        }

        await EnsureTableExistsAsync(token).ConfigureAwait(false);

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

    private async Task EnsureTableExistsAsync(CancellationToken cancellationToken)
    {
        _ = await _tableClient.CreateIfNotExistsAsync(cancellationToken).ConfigureAwait(false);
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
}
