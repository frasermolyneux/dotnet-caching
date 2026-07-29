using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MX.Caching.Abstractions;
using Xunit;

namespace MX.Caching.Tests;

/// <summary>
/// Verifies the HybridCache-backed MX cache facade.
/// </summary>
public sealed class ScaffoldTests
{
    private static readonly int[] FirstArrayArgument = [1, 2];
    private static readonly int[] SecondArrayArgument = [2, 1];

    /// <summary>
    /// Verifies that a cache hit avoids re-running the value factory.
    /// </summary>
    [Fact]
    public async Task GetOrCreateAsyncWhenValueIsCachedDoesNotReRunFactory()
    {
        var services = new ServiceCollection();
        _ = services.AddMxCaching();
        using var serviceProvider = services.BuildServiceProvider();
        var cache = serviceProvider.GetRequiredService<IMxCache>();
        var key = CacheKeyBuilder.Create("v1", "example", "get", "value");
        var calls = 0;

        var first = await cache.GetOrCreateAsync(
            key,
            new CachePolicy { Ttl = TimeSpan.FromMinutes(1) },
            _ => ValueTask.FromResult(++calls));
        var second = await cache.GetOrCreateAsync(
            key,
            new CachePolicy { Ttl = TimeSpan.FromMinutes(1) },
            _ => ValueTask.FromResult(++calls));

        Assert.Equal(1, first);
        Assert.Equal(1, second);
        Assert.Equal(1, calls);
    }

    /// <summary>
    /// Verifies that a cache miss can be checked without storing a sentinel value.
    /// </summary>
    [Fact]
    public async Task TryGetAsyncWhenValueIsAbsentReturnsMiss()
    {
        var services = new ServiceCollection();
        _ = services.AddMxCaching();
        using var serviceProvider = services.BuildServiceProvider();
        var cache = serviceProvider.GetRequiredService<IMxCache>();

        var result = await cache.TryGetAsync<string>(CacheKeyBuilder.Create("v1", "example", "missing"));

        Assert.False(result.Found);
        Assert.Null(result.Value);
    }

    /// <summary>
    /// Verifies that distinct argument types cannot produce the same cache key.
    /// </summary>
    [Fact]
    public void CreateWhenArgumentsHaveDifferentTypesCreatesDistinctKeys()
    {
        var stringKey = CacheKeyBuilder.Create("v1", "example", "get", "1");
        var integerKey = CacheKeyBuilder.Create("v1", "example", "get", 1);
        var nullKey = CacheKeyBuilder.Create("v1", "example", "get", (object?)null);
        var nullStringKey = CacheKeyBuilder.Create("v1", "example", "get", "null");

        Assert.NotEqual(stringKey, integerKey);
        Assert.NotEqual(nullKey, nullStringKey);
    }

    /// <summary>
    /// Verifies that argument values retain their case and structured values retain their content.
    /// </summary>
    [Fact]
    public void CreateWhenArgumentsHaveDistinctValuesCreatesDistinctKeys()
    {
        var uppercaseKey = CacheKeyBuilder.Create("v1", "example", "get", "ABC");
        var lowercaseKey = CacheKeyBuilder.Create("v1", "example", "get", "abc");
        var firstArrayKey = CacheKeyBuilder.Create("v1", "example", "get", FirstArrayArgument);
        var secondArrayKey = CacheKeyBuilder.Create("v1", "example", "get", SecondArrayArgument);
        var firstObjectKey = CacheKeyBuilder.Create("v1", "example", "get", new CacheArgument("first"));
        var secondObjectKey = CacheKeyBuilder.Create("v1", "example", "get", new CacheArgument("second"));

        Assert.NotEqual(uppercaseKey, lowercaseKey);
        Assert.NotEqual(firstArrayKey, secondArrayKey);
        Assert.NotEqual(firstObjectKey, secondObjectKey);
    }

    /// <summary>
    /// Verifies that dictionaries with the same entries produce the same key regardless of insertion order.
    /// </summary>
    [Fact]
    public void CreateWhenDictionaryOrderDiffersCreatesTheSameKey()
    {
        var first = new Dictionary<string, int>
        {
            ["first"] = 1,
            ["second"] = 2,
        };
        var second = new Dictionary<string, int>
        {
            ["second"] = 2,
            ["first"] = 1,
        };

        var firstKey = CacheKeyBuilder.Create("v1", "example", "get", first);
        var secondKey = CacheKeyBuilder.Create("v1", "example", "get", second);

        Assert.Equal(firstKey, secondKey);
    }

    /// <summary>
    /// Verifies that a null argument array behaves as an empty argument list.
    /// </summary>
    [Fact]
    public void CreateWhenArgumentArrayIsNullTreatsItAsEmpty()
    {
        var key = CacheKeyBuilder.Create("v1", "example", "get", null!);

        Assert.Equal(CacheKeyBuilder.Create("v1", "example", "get"), key);
    }

    /// <summary>
    /// Verifies that configuration selects the supported memory backend.
    /// </summary>
    [Fact]
    public void AddMxCachingWhenMemoryBackendIsConfiguredRegistersTheFacade()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("MxCaching:Backend", "Memory"),
            ])
            .Build();
        var services = new ServiceCollection();

        _ = services.AddMxCaching(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        _ = serviceProvider.GetRequiredService<IMxCache>();
    }

    /// <summary>
    /// Verifies that the default registration supports the distributed cache tier.
    /// </summary>
    [Fact]
    public async Task AddMxCachingWhenUsingTheDistributedTierUsesTheDefaultDistributedCache()
    {
        var services = new ServiceCollection();
        _ = services.AddMxCaching();

        using var serviceProvider = services.BuildServiceProvider();
        var distributedCache = serviceProvider.GetRequiredService<IDistributedCache>();
        await distributedCache.SetStringAsync("default-distributed-cache", "available");
        Assert.Equal("available", await distributedCache.GetStringAsync("default-distributed-cache"));

        var cache = serviceProvider.GetRequiredService<IMxCache>();
        var calls = 0;
        var policy = new CachePolicy { Tier = CacheTier.Distributed, Ttl = TimeSpan.FromMinutes(1) };
        var key = CacheKeyBuilder.Create("v1", "example", "default-distributed-tier");

        _ = await cache.GetOrCreateAsync(key, policy, _ => ValueTask.FromResult(++calls));
        _ = await cache.GetOrCreateAsync(key, policy, _ => ValueTask.FromResult(++calls));

        Assert.Equal(1, calls);
    }

    /// <summary>
    /// Verifies that configuration does not silently ignore an unavailable backend.
    /// </summary>
    [Fact]
    public void AddMxCachingWhenBackendIsNotImplementedThrows()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("MxCaching:Backend", "TableStorage"),
            ])
            .Build();
        var services = new ServiceCollection();

        _ = Assert.Throws<NotSupportedException>(() => services.AddMxCaching(configuration));
    }

    /// <summary>
    /// Verifies that the no-cache tier invokes the value factory for each request.
    /// </summary>
    [Fact]
    public async Task GetOrCreateAsyncWhenTierIsNoneDoesNotCacheTheValue()
    {
        var services = new ServiceCollection();
        _ = services.AddMxCaching();

        using var serviceProvider = services.BuildServiceProvider();
        var cache = serviceProvider.GetRequiredService<IMxCache>();
        var calls = 0;
        var policy = new CachePolicy { Tier = CacheTier.None };

        _ = await cache.GetOrCreateAsync(
            CacheKeyBuilder.Create("v1", "example", "none-tier"),
            policy,
            _ => ValueTask.FromResult(++calls));
        _ = await cache.GetOrCreateAsync(
            CacheKeyBuilder.Create("v1", "example", "none-tier"),
            policy,
            _ => ValueTask.FromResult(++calls));

        Assert.Equal(2, calls);
    }

    /// <summary>
    /// Verifies that each cache tier uses the local and distributed caches as configured.
    /// </summary>
    [Theory]
    [InlineData(CacheTier.InProcess, 2, 0, 1, 0)]
    [InlineData(CacheTier.Distributed, 1, 1, 0, 1)]
    [InlineData(CacheTier.Tiered, 1, 0, 1, 1)]
    public async Task GetOrCreateAsyncWhenTierIsConfiguredUsesExpectedCacheTiers(
        CacheTier tier,
        int expectedFactoryCalls,
        int expectedRepeatedRequestDistributedReads,
        int expectedRepeatedRequestLocalReads,
        int expectedDistributedWrites)
    {
        var distributedCache = new CountingDistributedCache();
        var localCache = new CountingMemoryCache();
        var calls = 0;
        var policy = new CachePolicy { Tier = tier };

        using (var firstServiceProvider = CreateServiceProvider(distributedCache, localCache))
        {
            var cache = firstServiceProvider.GetRequiredService<IMxCache>();
            _ = await cache.GetOrCreateAsync(
                CacheKeyBuilder.Create("v1", "example", "tier-read"),
                policy,
                _ => ValueTask.FromResult(++calls));
            var readCountAfterFirstRequest = distributedCache.ReadCount;
            var localReadCountAfterFirstRequest = localCache.ReadCount;
            _ = await cache.GetOrCreateAsync(
                CacheKeyBuilder.Create("v1", "example", "tier-read"),
                policy,
                _ => ValueTask.FromResult(++calls));

            Assert.Equal(
                expectedRepeatedRequestDistributedReads,
                distributedCache.ReadCount - readCountAfterFirstRequest);
            Assert.Equal(
                expectedRepeatedRequestLocalReads,
                localCache.ReadCount - localReadCountAfterFirstRequest);
        }

        using (var secondServiceProvider = CreateServiceProvider(distributedCache, new CountingMemoryCache()))
        {
            var cache = secondServiceProvider.GetRequiredService<IMxCache>();
            _ = await cache.GetOrCreateAsync(
                CacheKeyBuilder.Create("v1", "example", "tier-read"),
                policy,
                _ => ValueTask.FromResult(++calls));
        }

        Assert.Equal(expectedFactoryCalls, calls);
        Assert.Equal(expectedDistributedWrites, distributedCache.WriteCount);
    }

    /// <summary>
    /// Verifies that active cache policies reject unknown cache tiers.
    /// </summary>
    [Fact]
    public async Task GetOrCreateAsyncWhenTierIsInvalidThrows()
    {
        var services = new ServiceCollection();
        _ = services.AddMxCaching();

        using var serviceProvider = services.BuildServiceProvider();
        var cache = serviceProvider.GetRequiredService<IMxCache>();
        var policy = new CachePolicy { Tier = (CacheTier)999 };

        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => cache.GetOrCreateAsync(
            CacheKeyBuilder.Create("v1", "example", "invalid-tier"),
            policy,
            _ => ValueTask.FromResult(1)));
    }

    private static ServiceProvider CreateServiceProvider(
        IDistributedCache distributedCache,
        IMemoryCache localCache)
    {
        var services = new ServiceCollection();
        _ = services.AddSingleton(distributedCache);
        _ = services.AddSingleton(localCache);
        _ = services.AddMxCaching();

        return services.BuildServiceProvider();
    }

    private sealed class CountingDistributedCache : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, byte[]> _entries = new();

        public int ReadCount { get; private set; }

        public int WriteCount { get; private set; }

        public byte[]? Get(string key)
        {
            ReadCount++;
            return _entries.TryGetValue(key, out var value) ? value : null;
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            return Task.FromResult(Get(key));
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        public void Remove(string key)
        {
            _ = _entries.TryRemove(key, out _);
        }

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            WriteCount++;
            _entries[key] = value;
        }

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }
    }

    private sealed class CountingMemoryCache : IMemoryCache
    {
        private readonly MemoryCache _inner = new(Options.Create(new MemoryCacheOptions()));

        public int ReadCount { get; private set; }

        public ICacheEntry CreateEntry(object key)
        {
            return _inner.CreateEntry(key);
        }

        public void Dispose()
        {
            _inner.Dispose();
        }

        public void Remove(object key)
        {
            _inner.Remove(key);
        }

        public bool TryGetValue(object key, out object? value)
        {
            ReadCount++;
            return _inner.TryGetValue(key, out value);
        }
    }

    private sealed record CacheArgument(string Value);
}
