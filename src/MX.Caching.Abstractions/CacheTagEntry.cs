namespace MX.Caching.Abstractions;

/// <summary>
/// Describes the effective cache key and tag state for a tagged logical entry.
/// </summary>
/// <param name="EffectiveKey">The key supplied to the underlying cache.</param>
/// <param name="TagGenerations">The normalized tags and generations used to create the effective key.</param>
/// <param name="ExpiresAt">The point at which the cache entry and its metadata expire.</param>
public sealed record CacheTagEntry(
    string EffectiveKey,
    IReadOnlyDictionary<string, long> TagGenerations,
    DateTimeOffset ExpiresAt);
