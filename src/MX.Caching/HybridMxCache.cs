using Microsoft.Extensions.Caching.Hybrid;
using MX.Caching.Abstractions;
using System.Security.Cryptography;
using System.Text;

namespace MX.Caching;

/// <summary>
/// Implements <see cref="IMxCache"/> using <see cref="HybridCache"/>.
/// </summary>
public sealed class HybridMxCache(HybridCache cache, IMxCacheMetrics metrics, ICacheTagIndex tagIndex) : IMxCache
{
    private readonly HybridCache _cache = cache;
    private readonly IMxCacheMetrics _metrics = metrics;
    private readonly ICacheTagIndex _tagIndex = tagIndex;

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
        var tags = NormalizeTags(policy.Tags);
        var taggedKey = await CreateTaggedKeyAsync(key.Value, tags, cancellationToken).ConfigureAwait(false);
        var wasFactoryCalled = false;

        var value = await _cache.GetOrCreateAsync(
            taggedKey.EffectiveKey,
            async cancellationToken =>
            {
                wasFactoryCalled = true;
                return await factory(cancellationToken).ConfigureAwait(false);
            },
            options,
            tags,
            cancellationToken).ConfigureAwait(false);

        if (tags.Length > 0)
        {
            await _tagIndex.RegisterAsync(
                key.Value,
                new CacheTagEntry(
                    taggedKey.EffectiveKey,
                    taggedKey.Generations,
                    DateTimeOffset.UtcNow.Add(GetMetadataLifetime(policy))),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _tagIndex.RemoveAsync(key.Value, cancellationToken).ConfigureAwait(false);
        }

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
        var entry = await _tagIndex.GetAsync(key.Value, cancellationToken).ConfigureAwait(false);
        var effectiveKey = key.Value;

        if (entry is not null)
        {
            var currentGenerations = await _tagIndex
                .GetGenerationsAsync([.. entry.TagGenerations.Keys], cancellationToken)
                .ConfigureAwait(false);

            if (!HaveSameGenerations(entry.TagGenerations, currentGenerations))
            {
                _metrics.RecordMiss(key);
                return new CacheReadResult<T>(false, default);
            }

            effectiveKey = entry.EffectiveKey;
        }

        try
        {
            var value = await _cache.GetOrCreateAsync(
                effectiveKey,
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

        var tags = NormalizeTags(policy.Tags);
        var taggedKey = await CreateTaggedKeyAsync(key.Value, tags, cancellationToken).ConfigureAwait(false);

        await _cache.SetAsync(
            taggedKey.EffectiveKey,
            value,
            CreateEntryOptions(policy),
            tags,
            cancellationToken).ConfigureAwait(false);

        if (tags.Length > 0)
        {
            await _tagIndex.RegisterAsync(
                key.Value,
                new CacheTagEntry(
                    taggedKey.EffectiveKey,
                    taggedKey.Generations,
                    DateTimeOffset.UtcNow.Add(GetMetadataLifetime(policy))),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _tagIndex.RemoveAsync(key.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(CacheKey key, CancellationToken cancellationToken = default)
    {
        var effectiveKey = (await _tagIndex.GetAsync(key.Value, cancellationToken).ConfigureAwait(false))?.EffectiveKey ?? key.Value;
        await _cache.RemoveAsync(effectiveKey, cancellationToken).ConfigureAwait(false);
        await _tagIndex.RemoveAsync(key.Value, cancellationToken).ConfigureAwait(false);
        _metrics.RecordEviction(key);
    }

    /// <inheritdoc/>
    public async Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        var normalizedTag = NormalizeTag(tag);
        var effectiveKeys = await _tagIndex.InvalidateAsync(normalizedTag, cancellationToken).ConfigureAwait(false);

        await Task.WhenAll(effectiveKeys.Select(effectiveKey =>
            _cache.RemoveAsync(effectiveKey, cancellationToken).AsTask())).ConfigureAwait(false);
        await _cache.RemoveByTagAsync(normalizedTag, cancellationToken).ConfigureAwait(false);
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

    private async Task<TaggedCacheKey> CreateTaggedKeyAsync(
        string key,
        string[] tags,
        CancellationToken cancellationToken)
    {
        if (tags.Length == 0)
        {
            return new TaggedCacheKey(key, new Dictionary<string, long>(StringComparer.Ordinal));
        }

        var generations = await _tagIndex.GetGenerationsAsync(tags, cancellationToken).ConfigureAwait(false);
        var versionVector = string.Join(
            "|",
            tags.Select(tag => $"{tag}={generations[tag]}"));
        var versionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(versionVector)));
        return new TaggedCacheKey($"{key}:tag-state:{versionHash}", generations);
    }

    private static TimeSpan GetMetadataLifetime(CachePolicy policy)
    {
        return new[] { policy.Ttl, policy.L1Ttl ?? policy.Ttl, policy.L2Ttl ?? policy.Ttl }.Max();
    }

    private static bool HaveSameGenerations(
        IReadOnlyDictionary<string, long> expected,
        IReadOnlyDictionary<string, long> actual)
    {
        return expected.Count == actual.Count && expected.All(pair =>
            actual.TryGetValue(pair.Key, out var generation) && generation == pair.Value);
    }

    private static string[] NormalizeTags(IReadOnlyCollection<string> tags)
    {
        return [.. tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(NormalizeTag)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    private static string NormalizeTag(string tag)
    {
        return tag.Trim();
    }

    private sealed class CacheMissException : Exception;

    private sealed record TaggedCacheKey(string EffectiveKey, IReadOnlyDictionary<string, long> Generations);
}
