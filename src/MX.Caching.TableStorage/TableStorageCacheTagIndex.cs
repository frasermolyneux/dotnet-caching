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
public sealed class TableStorageCacheTagIndex : ICacheTagIndex
{
    private const string GenerationPartitionKey = "cache-tag-generation";
    private const string EntryPartitionKey = "cache-tag-entry";
    private const string TagPartitionKeyPrefix = "cache-tag-index-";
    private readonly TableClient _tableClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="TableStorageCacheTagIndex"/> class.
    /// </summary>
    /// <param name="tableServiceClient">The Azure Table service client.</param>
    /// <param name="tableName">The table used for cache metadata.</param>
    public TableStorageCacheTagIndex(TableServiceClient tableServiceClient, string tableName)
    {
        ArgumentNullException.ThrowIfNull(tableServiceClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        _tableClient = tableServiceClient.GetTableClient(tableName);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, long>> GetGenerationsAsync(
        IReadOnlyCollection<string> tags,
        CancellationToken cancellationToken = default)
    {
        await EnsureTableExistsAsync(cancellationToken).ConfigureAwait(false);

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

    /// <inheritdoc/>
    public async Task RegisterAsync(
        string key,
        CacheTagEntry entry,
        CancellationToken cancellationToken = default)
    {
        await EnsureTableExistsAsync(cancellationToken).ConfigureAwait(false);

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

    /// <inheritdoc/>
    public async Task<CacheTagEntry?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await EnsureTableExistsAsync(cancellationToken).ConfigureAwait(false);

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
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await EnsureTableExistsAsync(cancellationToken).ConfigureAwait(false);

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
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyCollection<string>> InvalidateAsync(string tag, CancellationToken cancellationToken = default)
    {
        await EnsureTableExistsAsync(cancellationToken).ConfigureAwait(false);
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
                }
            }
            catch (RequestFailedException exception) when (exception.Status == 412)
            {
            }
        }

        throw new InvalidOperationException($"Unable to advance cache tag generation for '{tag}'.");
    }

    private async Task EnsureTableExistsAsync(CancellationToken cancellationToken)
    {
        _ = await _tableClient.CreateIfNotExistsAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string CreateTagPartitionKey(string tag)
    {
        return TagPartitionKeyPrefix + CreateRowKey(tag);
    }

    private static string CreateRowKey(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
