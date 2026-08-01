using Azure.Data.Tables;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using MX.Caching.Abstractions;
using MX.Caching.TableStorage;
using Xunit;

namespace MX.Caching.IntegrationTests;

/// <summary>
/// Verifies the Azure Table Storage adapter against the Azurite development-storage endpoint.
/// </summary>
public sealed class TableStorageDistributedCacheAzuriteTests(
    TableStorageDistributedCacheAzuriteFixture fixture) : IClassFixture<TableStorageDistributedCacheAzuriteFixture>
{
    /// <summary>
    /// Verifies an entry written through the production adapter can be retrieved from Azurite.
    /// </summary>
    [Fact]
    public async Task SetAsyncWhenEntryExistsReturnsStoredValue()
    {
        var cache = fixture.CreateCache();
        var expected = new byte[] { 1, 2, 3 };

        await cache.SetAsync("round-trip", expected, new DistributedCacheEntryOptions());
        var actual = await cache.GetAsync("round-trip");

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies expired entries are not returned from the Azure Table Storage adapter.
    /// </summary>
    [Fact]
    public async Task GetAsyncWhenEntryIsExpiredReturnsNull()
    {
        var cache = fixture.CreateCache();

        await cache.SetAsync(
            "expired-entry",
            [1],
            new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = DateTimeOffset.UtcNow.AddSeconds(-1),
            });

        var actual = await cache.GetAsync("expired-entry");

        Assert.Null(actual);
    }

    /// <summary>
    /// Verifies entries using a relative expiration remain readable before they expire.
    /// </summary>
    [Fact]
    public async Task SetAsyncWhenEntryHasRelativeExpirationReturnsStoredValue()
    {
        var cache = fixture.CreateCache();
        var expected = new byte[] { 1, 2, 3 };

        await cache.SetAsync(
            "relative-expiration",
            expected,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
            });
        var actual = await cache.GetAsync("relative-expiration");

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies refreshing a sliding entry cannot extend it past its absolute expiry cap.
    /// </summary>
    [Fact]
    public async Task RefreshAsyncWhenEntryHasAbsoluteExpirationCapsRefreshedExpiry()
    {
        const string key = "sliding-with-absolute-expiration";
        var cache = fixture.CreateCache();
        var absoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(2);

        await cache.SetAsync(
            key,
            [1],
            new DistributedCacheEntryOptions
            {
                AbsoluteExpiration = absoluteExpiration,
                SlidingExpiration = TimeSpan.FromMinutes(10),
            });

        await fixture.UpdateExpiresUtcAsync(key, absoluteExpiration.AddMinutes(-1));
        var initialEntity = await fixture.GetEntityAsync(key);
        await cache.RefreshAsync(key);
        var entity = await fixture.GetEntityAsync(key);

        Assert.NotNull(initialEntity);
        Assert.NotNull(entity);
        Assert.Equal(1, entity.GetInt64("CacheEntryVersion"));
        Assert.Equal(absoluteExpiration.UtcTicks, entity.GetInt64("AbsoluteExpirationTicks"));
        Assert.True(initialEntity.GetDateTimeOffset("ExpiresUtc") < entity.GetDateTimeOffset("ExpiresUtc"));
        Assert.Equal(absoluteExpiration, entity.GetDateTimeOffset("ExpiresUtc"));
    }

    /// <summary>
    /// Verifies refreshing a current sliding entry without an absolute expiry cap extends its expiry.
    /// </summary>
    [Fact]
    public async Task RefreshAsyncWhenCurrentEntryHasOnlySlidingExpirationExtendsExpiry()
    {
        const string key = "current-sliding-without-absolute-expiration";
        var cache = fixture.CreateCache();

        await cache.SetAsync(
            key,
            [1],
            new DistributedCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(1),
            });

        var initialEntity = await fixture.GetEntityAsync(key);
        await cache.RefreshAsync(key);
        var entity = await fixture.GetEntityAsync(key);

        Assert.NotNull(initialEntity);
        Assert.NotNull(entity);
        Assert.Equal(1, entity.GetInt64("CacheEntryVersion"));
        Assert.Null(entity.GetInt64("AbsoluteExpirationTicks"));
        Assert.Equal(60, entity.GetInt64("SlidingExpirationSeconds"));
        Assert.True(initialEntity.GetDateTimeOffset("ExpiresUtc") < entity.GetDateTimeOffset("ExpiresUtc"));
    }

    /// <summary>
    /// Verifies refreshing a legacy sliding entry without an absolute expiry cap does not change its expiry.
    /// </summary>
    [Fact]
    public async Task RefreshAsyncWhenLegacyEntryHasNoAbsoluteExpirationDoesNotChangeExpiry()
    {
        const string key = "legacy-sliding-without-absolute-expiration";
        var cache = fixture.CreateCache();
        var expiresUtc = DateTimeOffset.UtcNow.AddMinutes(10);

        await fixture.SeedLegacySlidingEntryAsync(key, [1], expiresUtc, TimeSpan.FromHours(1));

        await cache.RefreshAsync(key);
        var entity = await fixture.GetEntityAsync(key);

        Assert.NotNull(entity);
        Assert.Null(entity.GetInt64("CacheEntryVersion"));
        Assert.Null(entity.GetInt64("AbsoluteExpirationTicks"));
        Assert.Equal(expiresUtc, entity.GetDateTimeOffset("ExpiresUtc"));
    }

    /// <summary>
    /// Verifies cache values larger than the Azure Table binary-property limit are rejected before storage.
    /// </summary>
    [Fact]
    public async Task SetAsyncWhenValueExceedsTableStorageLimitThrowsArgumentOutOfRangeException()
    {
        var cache = fixture.CreateCache();
        var value = new byte[(64 * 1024) + 1];

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => cache.SetAsync("oversized-value", value, new DistributedCacheEntryOptions()));

        Assert.Equal("value", exception.ParamName);
    }

    /// <summary>
    /// Verifies a value at the Azure Table binary-property limit remains supported.
    /// </summary>
    [Fact]
    public async Task SetAsyncWhenValueMatchesTableStorageLimitReturnsStoredValue()
    {
        var cache = fixture.CreateCache();
        var expected = new byte[64 * 1024];
        Random.Shared.NextBytes(expected);

        await cache.SetAsync("maximum-value", expected, new DistributedCacheEntryOptions());
        var actual = await cache.GetAsync("maximum-value");

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies Table-backed tag invalidation reaches entries held in independent L1 caches.
    /// </summary>
    [Fact]
    public async Task RemoveByTagAsyncWhenProvidersShareTableStorageInvalidatesBothL1Caches()
    {
        const string tag = "integration-tag";
        var key = new CacheKey($"tagged-entry:{Guid.NewGuid():N}");
        var policy = new CachePolicy
        {
            Tags = [tag],
        };

        var firstDistributedCache = new RecordingDistributedCache(fixture.CreateCache());
        using var firstProvider = CreateProvider(firstDistributedCache, fixture.CreateTagIndex());
        using var secondProvider = CreateProvider(fixture.CreateCache(), fixture.CreateTagIndex());
        var firstCache = firstProvider.GetRequiredService<IMxCache>();
        var secondCache = secondProvider.GetRequiredService<IMxCache>();

        var originalValue = await firstCache.GetOrCreateAsync(
            key,
            policy,
            _ => new ValueTask<string>("original"));

        await firstDistributedCache.WaitForWritesAsync();
        var storedEntity = await fixture.GetEntityAsync(firstDistributedCache.SetKeys.Single());
        var distributedValue = await firstDistributedCache.GetAsync(firstDistributedCache.SetKeys.Single());
        var secondProviderValue = await secondCache.GetOrCreateAsync(
            key,
            policy,
            _ => new ValueTask<string>("unexpected"));

        Assert.Equal("original", originalValue);
        Assert.Contains(
            firstDistributedCache.SetKeys,
            setKey => setKey.StartsWith($"{key.Value}:tag-state:", StringComparison.Ordinal));
        Assert.True(distributedValue is not null, firstDistributedCache.CreateDiagnosticMessage(storedEntity));
        Assert.Equal(originalValue, secondProviderValue);

        await firstCache.RemoveByTagAsync(tag);

        var replacementFactoryCalls = 0;
        var firstProviderReplacement = await firstCache.GetOrCreateAsync(
            key,
            policy,
            _ => new ValueTask<string>(
                Interlocked.Increment(ref replacementFactoryCalls) == 1 ? "replacement" : "unexpected"));
        var secondProviderReplacement = await secondCache.GetOrCreateAsync(
            key,
            policy,
            _ => new ValueTask<string>("unexpected"));

        Assert.Equal("replacement", firstProviderReplacement);
        Assert.Equal(firstProviderReplacement, secondProviderReplacement);
        Assert.Equal(1, replacementFactoryCalls);
    }

    private static ServiceProvider CreateProvider(IDistributedCache distributedCache, ICacheTagIndex tagIndex)
    {
        var services = new ServiceCollection();
        _ = services.AddSingleton(distributedCache);
        _ = services.AddSingleton(tagIndex);
        _ = services.AddMxCaching();
        return services.BuildServiceProvider();
    }
}

internal sealed class RecordingDistributedCache(IDistributedCache inner) : IDistributedCache
{
    private readonly TaskCompletionSource<Task> _firstSetTask = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ConcurrentQueue<string> GetKeys { get; } = new();

    public ConcurrentQueue<string> RemoveKeys { get; } = new();

    public ConcurrentQueue<string> SetKeys { get; } = new();

    public ConcurrentQueue<DistributedCacheEntryOptions> SetOptions { get; } = new();

    public ConcurrentBag<Task> SetTasks { get; } = [];

    public byte[]? Get(string key)
    {
        GetKeys.Enqueue(key);
        return inner.Get(key);
    }

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        GetKeys.Enqueue(key);
        return inner.GetAsync(key, token);
    }

    public void Refresh(string key)
    {
        inner.Refresh(key);
    }

    public Task RefreshAsync(string key, CancellationToken token = default)
    {
        return inner.RefreshAsync(key, token);
    }

    public void Remove(string key)
    {
        RemoveKeys.Enqueue(key);
        inner.Remove(key);
    }

    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        RemoveKeys.Enqueue(key);
        return inner.RemoveAsync(key, token);
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        SetKeys.Enqueue(key);
        SetOptions.Enqueue(options);
        inner.Set(key, value, options);
    }

    public Task SetAsync(
        string key,
        byte[] value,
        DistributedCacheEntryOptions options,
        CancellationToken token = default)
    {
        SetKeys.Enqueue(key);
        SetOptions.Enqueue(options);
        var setTask = inner.SetAsync(key, value, options, token);
        SetTasks.Add(setTask);
        _ = _firstSetTask.TrySetResult(setTask);
        return setTask;
    }

    public async Task WaitForWritesAsync()
    {
        _ = await _firstSetTask.Task;
        await Task.WhenAll(SetTasks);
    }

    public string CreateDiagnosticMessage(TableEntity? storedEntity)
    {
        var options = SetOptions.Select(entry =>
            $"absolute={entry.AbsoluteExpiration:O}, relative={entry.AbsoluteExpirationRelativeToNow}, sliding={entry.SlidingExpiration}");
        var storedValueLength = storedEntity?.GetBinary("Value")?.Length;
        return $"Gets: {string.Join(", ", GetKeys)}; Removes: {string.Join(", ", RemoveKeys)}; Writes: {string.Join(" | ", options)}; " +
            $"Stored: {storedEntity is not null}; Stored value length: {storedValueLength}; " +
            $"Stored expiry: {storedEntity?.GetDateTimeOffset("ExpiresUtc"):O}; " +
            $"Stored sliding expiry: {storedEntity?.GetInt64("SlidingExpirationSeconds")}";
    }
}

/// <summary>
/// Creates an isolated Azurite table for each integration-test class.
/// </summary>
public sealed class TableStorageDistributedCacheAzuriteFixture : IAsyncLifetime
{
    private const string ConnectionString = "UseDevelopmentStorage=true";
    private readonly TableServiceClient _tableServiceClient = new(ConnectionString);

    /// <summary>
    /// Gets the table name dedicated to the current test class.
    /// </summary>
    public string TableName { get; } = $"mxcaching{Guid.NewGuid():N}";

    /// <inheritdoc/>
    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        _ = await _tableServiceClient.DeleteTableAsync(TableName);
    }

    /// <summary>
    /// Creates a cache adapter that uses the fixture's dedicated Azurite table.
    /// </summary>
    /// <returns>A production Table Storage cache adapter connected to Azurite.</returns>
    public TableStorageDistributedCache CreateCache()
    {
        return new TableStorageDistributedCache(_tableServiceClient, TableName);
    }

    /// <summary>
    /// Creates a production tag index that uses the fixture's dedicated Azurite table.
    /// </summary>
    /// <returns>A Table Storage tag index connected to Azurite.</returns>
    public TableStorageCacheTagIndex CreateTagIndex()
    {
        return new TableStorageCacheTagIndex(_tableServiceClient, TableName);
    }

    /// <summary>
    /// Reads a physical cache row for focused interoperability diagnostics.
    /// </summary>
    public async Task<TableEntity?> GetEntityAsync(string key)
    {
        var response = await _tableServiceClient
            .GetTableClient(TableName)
            .GetEntityIfExistsAsync<TableEntity>("cache", CreateRowKey(key));

        return response.HasValue ? response.Value : null;
    }

    /// <summary>
    /// Updates a physical cache row's expiry to arrange a focused refresh scenario.
    /// </summary>
    /// <param name="key">The logical cache key.</param>
    /// <param name="expiresUtc">The expiry timestamp to persist.</param>
    public async Task UpdateExpiresUtcAsync(string key, DateTimeOffset expiresUtc)
    {
        var tableClient = _tableServiceClient.GetTableClient(TableName);
        var entity = await tableClient.GetEntityAsync<TableEntity>("cache", CreateRowKey(key));
        entity.Value["ExpiresUtc"] = expiresUtc;

        _ = await tableClient.UpdateEntityAsync(entity.Value, entity.Value.ETag, TableUpdateMode.Replace);
    }

    /// <summary>
    /// Inserts a legacy sliding cache row that does not persist an absolute expiry cap.
    /// </summary>
    /// <param name="key">The logical cache key.</param>
    /// <param name="value">The cached binary payload.</param>
    /// <param name="expiresUtc">The stored expiration timestamp.</param>
    /// <param name="slidingExpiration">The legacy sliding-expiration interval.</param>
    public async Task SeedLegacySlidingEntryAsync(string key, byte[] value, DateTimeOffset expiresUtc, TimeSpan slidingExpiration)
    {
        var tableClient = _tableServiceClient.GetTableClient(TableName);
        _ = await tableClient.CreateIfNotExistsAsync();

        var entity = new TableEntity("cache", CreateRowKey(key))
        {
            ["Value"] = value,
            ["ExpiresUtc"] = expiresUtc,
            ["SlidingExpirationSeconds"] = Convert.ToInt64(slidingExpiration.TotalSeconds),
        };

        _ = await tableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
    }

    private static string CreateRowKey(string key)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }
}
