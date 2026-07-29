using Microsoft.Extensions.Caching.Hybrid;
using MX.Caching.Abstractions;

namespace MX.Caching;

/// <summary>
/// Implements <see cref="IMxCache"/> using <see cref="HybridCache"/>.
/// </summary>
public sealed class HybridMxCache(HybridCache cache, IMxCacheMetrics metrics) : IMxCache
{
    private readonly HybridCache _cache = cache;
    private readonly IMxCacheMetrics _metrics = metrics;

    /// <inheritdoc/>
    public async Task<T> GetOrCreateAsync<T>(
        CacheKey key,
        CachePolicy policy,
        Func<CancellationToken, ValueTask<T>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(factory);

        if (!CanCache(policy))
        {
            return await factory(cancellationToken).ConfigureAwait(false);
        }

        var options = CreateEntryOptions(policy);
        var wasFactoryCalled = false;

        var value = await _cache.GetOrCreateAsync(
            key.Value,
            async cancellationToken =>
            {
                wasFactoryCalled = true;
                return await factory(cancellationToken).ConfigureAwait(false);
            },
            options,
            policy.Tags,
            cancellationToken).ConfigureAwait(false);

        if (wasFactoryCalled)
        {
            _metrics.RecordMiss(key);
        }
        else
        {
            _metrics.RecordHit(key);
        }

        return value;
    }

    /// <inheritdoc/>
    public async Task<CacheReadResult<T>> TryGetAsync<T>(CacheKey key, CancellationToken cancellationToken = default)
    {
        try
        {
            var value = await _cache.GetOrCreateAsync(
                key.Value,
                static _ => ValueTask.FromException<T>(new CacheMissException()),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            _metrics.RecordHit(key);
            return new CacheReadResult<T>(true, value);
        }
        catch (CacheMissException)
        {
            _metrics.RecordMiss(key);
            return new CacheReadResult<T>(false, default);
        }
    }

    /// <inheritdoc/>
    public async Task SetAsync<T>(
        CacheKey key,
        T value,
        CachePolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (!CanCache(policy))
        {
            return;
        }

        await _cache.SetAsync(
            key.Value,
            value,
            CreateEntryOptions(policy),
            policy.Tags,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(CacheKey key, CancellationToken cancellationToken = default)
    {
        await _cache.RemoveAsync(key.Value, cancellationToken).ConfigureAwait(false);
        _metrics.RecordEviction(key);
    }

    /// <inheritdoc/>
    public Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        return _cache.RemoveByTagAsync(tag, cancellationToken).AsTask();
    }

    private static bool CanCache(CachePolicy policy)
    {
        return policy.Enabled && policy.Tier != CacheTier.None && policy.Ttl > TimeSpan.Zero;
    }

    private static HybridCacheEntryOptions CreateEntryOptions(CachePolicy policy)
    {
        var flags = policy.Tier switch
        {
            CacheTier.InProcess => HybridCacheEntryFlags.DisableDistributedCache,
            CacheTier.Distributed => HybridCacheEntryFlags.DisableLocalCache,
            CacheTier.Tiered => HybridCacheEntryFlags.None,
            CacheTier.None => HybridCacheEntryFlags.None,
            _ => throw new ArgumentOutOfRangeException(nameof(policy)),
        };

        return new HybridCacheEntryOptions
        {
            Expiration = policy.L2Ttl ?? policy.Ttl,
            LocalCacheExpiration = policy.L1Ttl ?? policy.Ttl,
            Flags = flags,
        };
    }

    private sealed class CacheMissException : Exception;
}
