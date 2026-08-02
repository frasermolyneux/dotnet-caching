using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using MX.Caching.Abstractions;

namespace MX.Caching.TableStorage;

/// <summary>
/// Stores shared cache-tag generations and side-index entries in Azure Table Storage.
/// </summary>
/// <remarks>
/// The backing table is created lazily on first use with double-checked locking. A transient
/// failure during creation does not permanently poison the instance; subsequent calls retry.
/// </remarks>
public sealed class TableStorageCacheTagIndex : ICacheTagIndex, IDisposable
{
    private const string GenerationPartitionKey = "cache-tag-generation";
    private const string EntryPartitionKey = "cache-tag-entry";
    private const string TagPartitionKeyPrefix = "cache-tag-index-";
    private readonly TableClient _tableClient;
    private readonly TableStorageCacheMetrics? _metrics;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile bool _initialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="TableStorageCacheTagIndex"/> class.
    /// </summary>
    /// <param name="tableServiceClient">The Azure Table service client.</param>
    /// <param name="tableName">The table used for cache metadata.</param>
    public TableStorageCacheTagIndex(TableServiceClient tableServiceClient, string tableName)
        : this(tableServiceClient, tableName, null)
    {
    }

    internal TableStorageCacheTagIndex(
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
    public async Task<IReadOnlyDictionary<string, long>> GetGenerationsAsync(
        IReadOnlyCollection<string> tags,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var start = Stopwatch.GetTimestamp();
        var hasError = false;
        try
        {
            var generations = await Task.WhenAll(tags.Select(async tag =>
            {
                try
                {
                    var response = await _tableClient.GetEntityAsync<TableEntity>(
                        GenerationPartitionKey,
                        CreateRowKey(tag),
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    return new KeyValuePair<string, long>(tag, response.Value.GetInt64("Generation") ?? 0);
                }
                catch (RequestFailedException exception) when (exception.Status == 404)
                {
                    return new KeyValuePair<string, long>(tag, 0);
                }
            })).ConfigureAwait(false);

            return generations.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
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
                _metrics.RecordDuration("get_generations", Stopwatch.GetElapsedTime(start).TotalSeconds);
                if (hasError)
                {
                    _metrics.RecordError("get_generations");
                }
            }
        }
    }

    /// <inheritdoc/>
    public async Task RegisterAsync(
        string key,
        CacheTagEntry entry,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var start = Stopwatch.GetTimestamp();
        var hasError = false;
        try
        {
            var mappingEntity = new TableEntity(EntryPartitionKey, CreateRowKey(key))
            {
                ["EffectiveKey"] = entry.EffectiveKey,
                ["TagGenerations"] = JsonSerializer.Serialize(entry.TagGenerations),
                ["ExpiresAt"] = entry.ExpiresAt,
            };
            _ = await _tableClient.UpsertEntityAsync(mappingEntity, TableUpdateMode.Replace, cancellationToken).ConfigureAwait(false);

            _ = await Task.WhenAll(entry.TagGenerations.Keys.Select(tag =>
            {
                var tagEntry = new TableEntity(CreateTagPartitionKey(tag), CreateRowKey(entry.EffectiveKey))
                {
                    ["EffectiveKey"] = entry.EffectiveKey,
                    ["ExpiresAt"] = entry.ExpiresAt,
                };
                return _tableClient.UpsertEntityAsync(tagEntry, TableUpdateMode.Replace, cancellationToken);
            })).ConfigureAwait(false);
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
                _metrics.RecordDuration("register", Stopwatch.GetElapsedTime(start).TotalSeconds);
                if (hasError)
                {
                    _metrics.RecordError("register");
                }
            }
        }
    }

    /// <inheritdoc/>
    public async Task<CacheTagEntry?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var start = Stopwatch.GetTimestamp();
        var hasError = false;
        try
        {
            var response = await _tableClient.GetEntityAsync<TableEntity>(
                EntryPartitionKey,
                CreateRowKey(key),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var entity = response.Value;
            var expiresAt = entity.GetDateTimeOffset("ExpiresAt");
            if (expiresAt is null || expiresAt <= DateTimeOffset.UtcNow)
            {
                _ = await _tableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, ETag.All, cancellationToken).ConfigureAwait(false);
                return null;
            }

            var effectiveKey = entity.GetString("EffectiveKey");
            var serializedGenerations = entity.GetString("TagGenerations");
            if (effectiveKey is null || serializedGenerations is null)
            {
                return null;
            }

            var generations = JsonSerializer.Deserialize<Dictionary<string, long>>(serializedGenerations);
            return generations is null ? null : new CacheTagEntry(effectiveKey, generations, expiresAt.Value);
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
                _metrics.RecordDuration("get_entry", Stopwatch.GetElapsedTime(start).TotalSeconds);
                if (hasError)
                {
                    _metrics.RecordError("get_entry");
                }
            }
        }
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var start = Stopwatch.GetTimestamp();
        var hasError = false;
        try
        {
            _ = await _tableClient.DeleteEntityAsync(
                EntryPartitionKey,
                CreateRowKey(key),
                ETag.All,
                cancellationToken).ConfigureAwait(false);
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
                _metrics.RecordDuration("remove_entry", Stopwatch.GetElapsedTime(start).TotalSeconds);
                if (hasError)
                {
                    _metrics.RecordError("remove_entry");
                }
            }
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyCollection<string>> InvalidateAsync(string tag, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var start = Stopwatch.GetTimestamp();
        var hasError = false;
        try
        {
            await IncrementGenerationAsync(tag, cancellationToken).ConfigureAwait(false);

            var tagPartitionKey = CreateTagPartitionKey(tag);
            var effectiveKeys = new List<string>();
            await foreach (var entity in _tableClient.QueryAsync<TableEntity>(
                filter: $"PartitionKey eq '{tagPartitionKey}'",
                cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                var expiresAt = entity.GetDateTimeOffset("ExpiresAt");
                if (expiresAt is not null && expiresAt <= DateTimeOffset.UtcNow)
                {
                    _ = await _tableClient.DeleteEntityAsync(
                        entity.PartitionKey,
                        entity.RowKey,
                        ETag.All,
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var effectiveKey = entity.GetString("EffectiveKey");
                if (effectiveKey is not null)
                {
                    effectiveKeys.Add(effectiveKey);
                }

                _ = await _tableClient.DeleteEntityAsync(
                    entity.PartitionKey,
                    entity.RowKey,
                    ETag.All,
                    cancellationToken).ConfigureAwait(false);
            }

            return [.. effectiveKeys.Distinct(StringComparer.Ordinal)];
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
                _metrics.RecordDuration("invalidate", Stopwatch.GetElapsedTime(start).TotalSeconds);
                if (hasError)
                {
                    _metrics.RecordError("invalidate");
                }
            }
        }
    }

    private async Task IncrementGenerationAsync(string tag, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<TableEntity>(
                    GenerationPartitionKey,
                    CreateRowKey(tag),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                var entity = response.Value;
                entity["Generation"] = checked((entity.GetInt64("Generation") ?? 0) + 1);
                _ = await _tableClient.UpdateEntityAsync(
                    entity,
                    entity.ETag,
                    TableUpdateMode.Replace,
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                try
                {
                    var entity = new TableEntity(GenerationPartitionKey, CreateRowKey(tag))
                    {
                        ["Generation"] = 1L,
                    };
                    _ = await _tableClient.AddEntityAsync(entity, cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (RequestFailedException addException) when (addException.Status == 409)
                {
                    _metrics?.RecordGenerationRetry();
                }
            }
            catch (RequestFailedException exception) when (exception.Status == 412)
            {
                _metrics?.RecordGenerationRetry();
            }
        }

        throw new InvalidOperationException($"Unable to advance cache tag generation for '{tag}'.");
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

    private static string CreateTagPartitionKey(string tag)
    {
        return TagPartitionKeyPrefix + CreateRowKey(tag);
    }

    private static string CreateRowKey(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _initLock.Dispose();
    }
}
