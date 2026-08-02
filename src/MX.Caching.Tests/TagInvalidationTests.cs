using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MX.Caching.Abstractions;
using MX.Caching.TableStorage;
using Xunit;

namespace MX.Caching.Tests;

/// <summary>
/// Verifies tag invalidation behavior across multiple cache instances sharing the same tag index.
/// </summary>
public sealed class TagInvalidationCrossInstanceTests
{
    /// <summary>
    /// Verifies that invalidating a tag from one cache instance causes a miss in a second
    /// instance that shares the same in-memory tag index, simulating cross-instance invalidation.
    /// </summary>
    [Fact]
    public async Task RemoveByTagAsyncWhenCalledOnFirstInstanceCausesMissInSecondInstance()
    {
        var sharedTagIndex = new MemoryTagIndexSpy();

        using var firstProvider = CreateProvider(sharedTagIndex);
        using var secondProvider = CreateProvider(sharedTagIndex);
        var firstCache = firstProvider.GetRequiredService<IMxCache>();
        var secondCache = secondProvider.GetRequiredService<IMxCache>();

        var key = new CacheKey("cross-instance:1");
        var policy = new CachePolicy { Tags = ["cross-instance-tag"], Ttl = TimeSpan.FromMinutes(5) };

        var original = await firstCache.GetOrCreateAsync(key, policy, _ => new ValueTask<string>("original"));
        Assert.Equal("original", original);

        await firstCache.RemoveByTagAsync("cross-instance-tag");

        var factoryCalled = false;
        var afterInvalidation = await secondCache.GetOrCreateAsync(
            key,
            policy,
            _ =>
            {
                factoryCalled = true;
                return new ValueTask<string>("refreshed");
            });

        Assert.True(factoryCalled, "Factory should be invoked because the tag was invalidated.");
        Assert.Equal("refreshed", afterInvalidation);
    }

    /// <summary>
    /// Verifies that tag invalidation works for the Distributed cache tier.
    /// </summary>
    [Fact]
    public async Task RemoveByTagAsyncWhenDistributedTierCausesMissOnNextGet()
    {
        var sharedTagIndex = new MemoryTagIndexSpy();

        using var provider = CreateProvider(sharedTagIndex);
        var cache = provider.GetRequiredService<IMxCache>();

        var key = new CacheKey("distributed-tag-invalidation:1");
        var policy = new CachePolicy
        {
            Tags = ["distributed-tag"],
            Tier = CacheTier.Distributed,
            Ttl = TimeSpan.FromMinutes(5),
        };

        _ = await cache.GetOrCreateAsync(key, policy, _ => new ValueTask<string>("cached"));

        await cache.RemoveByTagAsync("distributed-tag");

        var calls = 0;
        _ = await cache.GetOrCreateAsync(key, policy, _ =>
        {
            calls++;
            return new ValueTask<string>("refreshed");
        });

        Assert.Equal(1, calls);
    }

    /// <summary>
    /// Verifies that tag invalidation works for the Tiered cache tier.
    /// </summary>
    [Fact]
    public async Task RemoveByTagAsyncWhenTieredTierCausesMissOnNextGet()
    {
        var sharedTagIndex = new MemoryTagIndexSpy();

        using var provider = CreateProvider(sharedTagIndex);
        var cache = provider.GetRequiredService<IMxCache>();

        var key = new CacheKey("tiered-tag-invalidation:1");
        var policy = new CachePolicy
        {
            Tags = ["tiered-tag"],
            Tier = CacheTier.Tiered,
            Ttl = TimeSpan.FromMinutes(5),
        };

        _ = await cache.GetOrCreateAsync(key, policy, _ => new ValueTask<string>("cached"));

        await cache.RemoveByTagAsync("tiered-tag");

        var calls = 0;
        _ = await cache.GetOrCreateAsync(key, policy, _ =>
        {
            calls++;
            return new ValueTask<string>("refreshed");
        });

        Assert.Equal(1, calls);
    }

    /// <summary>
    /// Verifies that entries without tags are not affected when an unrelated tag is invalidated.
    /// </summary>
    [Fact]
    public async Task RemoveByTagAsyncWhenEntryHasNoTagsIsNotEvicted()
    {
        var sharedTagIndex = new MemoryTagIndexSpy();

        using var provider = CreateProvider(sharedTagIndex);
        var cache = provider.GetRequiredService<IMxCache>();

        var untaggedKey = new CacheKey("untagged:1");
        var taggedKey = new CacheKey("tagged:1");

        _ = await cache.GetOrCreateAsync(untaggedKey, new CachePolicy { Ttl = TimeSpan.FromMinutes(5) },
            _ => new ValueTask<string>("untagged-value"));
        _ = await cache.GetOrCreateAsync(
            taggedKey, new CachePolicy { Tags = ["evicted-tag"], Ttl = TimeSpan.FromMinutes(5) },
            _ => new ValueTask<string>("tagged-value"));

        await cache.RemoveByTagAsync("evicted-tag");

        var untaggedCalls = 0;
        var untaggedResult = await cache.GetOrCreateAsync(
            untaggedKey, new CachePolicy { Ttl = TimeSpan.FromMinutes(5) },
            _ =>
            {
                untaggedCalls++;
                return new ValueTask<string>("unexpected");
            });

        Assert.Equal("untagged-value", untaggedResult);
        Assert.Equal(0, untaggedCalls);
    }

    private static ServiceProvider CreateProvider(ICacheTagIndex tagIndex)
    {
        var services = new ServiceCollection();
        _ = services.AddSingleton(tagIndex);
        _ = services.AddMxCaching();
        return services.BuildServiceProvider();
    }
}

/// <summary>
/// Verifies generation-retry behavior in <see cref="TableStorageCacheTagIndex"/>.
/// </summary>
public sealed class TableStorageCacheTagIndexRetryTests
{
    /// <summary>
    /// Verifies that an ETag conflict (412) during generation increment is retried until success.
    /// </summary>
    [Fact]
    public async Task InvalidateAsyncWhenETagConflictOccursRetriesUntilSuccess()
    {
        var (serviceClient, tableClient) = CreateMocks();

        var generationRowKey = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("retry-tag")));

        var existingEntity = new TableEntity("cache-tag-generation", generationRowKey)
        {
            ["Generation"] = 1L,
            ETag = new ETag("\"old-etag\""),
        };

        _ = tableClient
            .Setup(c => c.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Response<TableItem>?>(null));

        var entityResponse = new Mock<Response<TableEntity>>();
        _ = entityResponse.Setup(r => r.Value).Returns(existingEntity);

        _ = tableClient
            .Setup(c => c.GetEntityAsync<TableEntity>(
                "cache-tag-generation", generationRowKey,
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entityResponse.Object);

        _ = tableClient
            .SetupSequence(c => c.UpdateEntityAsync(
                It.IsAny<TableEntity>(), It.IsAny<ETag>(), It.IsAny<TableUpdateMode>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(412, "Precondition Failed"))
            .ThrowsAsync(new RequestFailedException(412, "Precondition Failed"))
            .ReturnsAsync(Mock.Of<Response>());

        _ = tableClient
            .Setup(c => c.QueryAsync<TableEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(AsyncPageable<TableEntity>.FromPages([]));

        var index = new TableStorageCacheTagIndex(serviceClient.Object, "test");

        var result = await index.InvalidateAsync("retry-tag");

        Assert.Empty(result);
        tableClient.Verify(
            c => c.UpdateEntityAsync(
                It.IsAny<TableEntity>(), It.IsAny<ETag>(), It.IsAny<TableUpdateMode>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    /// <summary>
    /// Verifies that an ETag conflict retry counter is incremented during generation increment.
    /// </summary>
    [Fact]
    public async Task InvalidateAsyncWhenETagConflictOccursRecordsGenerationRetryMetric()
    {
        var (serviceClient, tableClient) = CreateMocks();
        using var metrics = new TableStorageCacheMetrics();

        var generationRowKey = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("metric-retry-tag")));

        var entity = new TableEntity("cache-tag-generation", generationRowKey)
        {
            ["Generation"] = 0L,
            ETag = new ETag("\"etag\""),
        };
        var entityResponse = new Mock<Response<TableEntity>>();
        _ = entityResponse.Setup(r => r.Value).Returns(entity);

        _ = tableClient
            .Setup(c => c.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Response<TableItem>?>(null));

        _ = tableClient
            .Setup(c => c.GetEntityAsync<TableEntity>(
                "cache-tag-generation", generationRowKey,
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entityResponse.Object);

        _ = tableClient
            .SetupSequence(c => c.UpdateEntityAsync(
                It.IsAny<TableEntity>(), It.IsAny<ETag>(), It.IsAny<TableUpdateMode>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(412, "Precondition Failed"))
            .ReturnsAsync(Mock.Of<Response>());

        _ = tableClient
            .Setup(c => c.QueryAsync<TableEntity>(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(AsyncPageable<TableEntity>.FromPages([]));

        var retryCount = 0L;
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "MX.Caching" &&
                instrument.Name == "mx.cache.storage.generation_retries")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
        {
            _ = Interlocked.Add(ref retryCount, measurement);
        });
        listener.Start();

        var index = new TableStorageCacheTagIndex(serviceClient.Object, "test", metrics);

        _ = await index.InvalidateAsync("metric-retry-tag");

        Assert.Equal(1L, Interlocked.Read(ref retryCount));
    }

    private static (Mock<TableServiceClient> ServiceClient, Mock<TableClient> TableClient) CreateMocks()
    {
        var mockTableClient = new Mock<TableClient>();
        var mockServiceClient = new Mock<TableServiceClient>();
        _ = mockServiceClient
            .Setup(s => s.GetTableClient(It.IsAny<string>()))
            .Returns(mockTableClient.Object);
        return (mockServiceClient, mockTableClient);
    }
}

/// <summary>
/// Spy around the in-memory cache tag index to verify generation tracking.
/// Wraps the internal implementation via the public <see cref="ICacheTagIndex"/> interface.
/// </summary>
internal sealed class MemoryTagIndexSpy : ICacheTagIndex
{
    private readonly ICacheTagIndex _inner;

    public MemoryTagIndexSpy()
    {
        var services = new ServiceCollection();
        _ = services.AddMxCachingMemory();
        using var sp = services.BuildServiceProvider();
        _inner = sp.GetRequiredService<ICacheTagIndex>();
    }

    public Task<IReadOnlyDictionary<string, long>> GetGenerationsAsync(
        IReadOnlyCollection<string> tags,
        CancellationToken cancellationToken = default)
    {
        return _inner.GetGenerationsAsync(tags, cancellationToken);
    }

    public Task RegisterAsync(
        string key,
        CacheTagEntry entry,
        CancellationToken cancellationToken = default)
    {
        return _inner.RegisterAsync(key, entry, cancellationToken);
    }

    public Task<CacheTagEntry?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        return _inner.GetAsync(key, cancellationToken);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        return _inner.RemoveAsync(key, cancellationToken);
    }

    public Task<IReadOnlyCollection<string>> InvalidateAsync(string tag, CancellationToken cancellationToken = default)
    {
        return _inner.InvalidateAsync(tag, cancellationToken);
    }
}
