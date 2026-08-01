using MX.Caching.Abstractions;
using MX.Caching.Testing;
using Xunit;

namespace MX.Caching.Tests;

/// <summary>
/// Tests for <see cref="FakeMxCache"/>.
/// </summary>
public sealed class FakeMxCacheTests
{
    /// <summary>
    /// Verifies a cached value prevents subsequent factory execution.
    /// </summary>
    [Fact]
    public async Task GetOrCreateAsyncWhenValueIsCachedRunsFactoryOnlyOnTheFirstCall()
    {
        var cache = new FakeMxCache();
        var key = new CacheKey("games:1");
        var factoryCallCount = 0;

        var first = await cache.GetOrCreateAsync(key, new CachePolicy(), _ => ValueTask.FromResult(++factoryCallCount));
        var second = await cache.GetOrCreateAsync(key, new CachePolicy(), _ => ValueTask.FromResult(++factoryCallCount));

        Assert.Equal(1, first);
        Assert.Equal(1, second);
        Assert.Equal(1, factoryCallCount);
        Assert.Equal(
            [FakeMxCacheOperation.GetOrCreate, FakeMxCacheOperation.GetOrCreate],
            cache.Invocations.Select(invocation => invocation.Operation));
    }

    /// <summary>
    /// Verifies an uncached policy prevents a value from being stored.
    /// </summary>
    [Fact]
    public async Task SetAsyncWhenPolicyIsNotCachedDoesNotStoreValue()
    {
        var cache = new FakeMxCache();
        var key = new CacheKey("games:1");

        await cache.SetAsync(key, "value", CachePolicy.NotCached);
        var result = await cache.TryGetAsync<string>(key);

        Assert.False(result.Found);
        Assert.Null(result.Value);
    }

    /// <summary>
    /// Verifies removing by tag affects only entries with the matching tag.
    /// </summary>
    [Fact]
    public async Task RemoveByTagAsyncWhenEntriesMatchTagRemovesThem()
    {
        var cache = new FakeMxCache();
        var taggedKey = new CacheKey("games:1");
        var untaggedKey = new CacheKey("games:2");
        var taggedPolicy = new CachePolicy { Tags = ["game:1"] };

        await cache.SetAsync(taggedKey, "tagged", taggedPolicy);
        await cache.SetAsync(untaggedKey, "untagged", new CachePolicy());
        await cache.RemoveByTagAsync("game:1");

        var taggedResult = await cache.TryGetAsync<string>(taggedKey);
        var untaggedResult = await cache.TryGetAsync<string>(untaggedKey);

        Assert.False(taggedResult.Found);
        Assert.True(untaggedResult.Found);
        Assert.Equal("untagged", untaggedResult.Value);
    }

    /// <summary>
    /// Verifies a cancelled write does not alter the fake's observable state.
    /// </summary>
    [Fact]
    public async Task SetAsyncWhenCancellationIsRequestedThrowsAndDoesNotRecordOperation()
    {
        var cache = new FakeMxCache();
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => cache.SetAsync(new CacheKey("games:1"), "value", new CachePolicy(), cancellationTokenSource.Token));

        Assert.Empty(cache.Invocations);
    }
}
