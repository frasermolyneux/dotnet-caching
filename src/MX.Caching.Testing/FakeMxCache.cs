using MX.Caching.Abstractions;

namespace MX.Caching.Testing;

/// <summary>
/// Describes an operation invoked on <see cref="FakeMxCache"/>.
/// </summary>
public enum FakeMxCacheOperation
{
    /// <summary>
    /// Gets or creates a cached value.
    /// </summary>
    GetOrCreate,

    /// <summary>
    /// Attempts to get a cached value.
    /// </summary>
    TryGet,

    /// <summary>
    /// Stores a cached value.
    /// </summary>
    Set,

    /// <summary>
    /// Removes a cached value by key.
    /// </summary>
    Remove,

    /// <summary>
    /// Removes cached values associated with a tag.
    /// </summary>
    RemoveByTag,
}

/// <summary>
/// Records an operation invoked on <see cref="FakeMxCache"/>.
/// </summary>
/// <param name="Operation">The cache operation invoked.</param>
/// <param name="Key">The cache key, when the operation addresses a key.</param>
/// <param name="ValueType">The requested or supplied value type, when applicable.</param>
/// <param name="Policy">The policy supplied to a read-through or write operation.</param>
/// <param name="Tag">The tag supplied to a tag removal operation.</param>
public sealed record FakeMxCacheInvocation(
    FakeMxCacheOperation Operation,
    CacheKey? Key,
    Type? ValueType,
    CachePolicy? Policy,
    string? Tag);

/// <summary>
/// Provides a deterministic, in-memory <see cref="IMxCache"/> implementation for consumer tests.
/// </summary>
/// <remarks>
/// Values are cached only when the supplied policy is enabled, has an active tier, and has a positive TTL.
/// This fake does not simulate expiry or cache-tier distribution.
/// </remarks>
public sealed class FakeMxCache : IMxCache
{
    private readonly Lock _syncRoot = new();
    private readonly Dictionary<CacheKey, CacheEntry> _entries = [];
    private readonly List<FakeMxCacheInvocation> _invocations = [];

    /// <summary>
    /// Gets the cache operations in invocation order.
    /// </summary>
    public IReadOnlyList<FakeMxCacheInvocation> Invocations
    {
        get
        {
            lock (_syncRoot)
            {
                return [.. _invocations];
            }
        }
    }

    /// <inheritdoc/>
    public async Task<T> GetOrCreateAsync<T>(
        CacheKey key,
        CachePolicy policy,
        Func<CancellationToken, ValueTask<T>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(factory);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _invocations.Add(new(FakeMxCacheOperation.GetOrCreate, key, typeof(T), policy, null));

            if (CanCache(policy) && TryGetValue(key, out T? cachedValue))
            {
                return cachedValue!;
            }
        }

        var value = await factory(cancellationToken).ConfigureAwait(false);

        if (CanCache(policy))
        {
            lock (_syncRoot)
            {
                _entries[key] = new CacheEntry(value, typeof(T), [.. policy.Tags]);
            }
        }

        return value;
    }

    /// <inheritdoc/>
    public Task<CacheReadResult<T>> TryGetAsync<T>(CacheKey key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _invocations.Add(new(FakeMxCacheOperation.TryGet, key, typeof(T), null, null));

            return Task.FromResult(TryGetValue(key, out T? value)
                ? new CacheReadResult<T>(true, value)
                : new CacheReadResult<T>(false, default));
        }
    }

    /// <inheritdoc/>
    public Task SetAsync<T>(
        CacheKey key,
        T value,
        CachePolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _invocations.Add(new(FakeMxCacheOperation.Set, key, typeof(T), policy, null));

            if (CanCache(policy))
            {
                _entries[key] = new CacheEntry(value, typeof(T), [.. policy.Tags]);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RemoveAsync(CacheKey key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _invocations.Add(new(FakeMxCacheOperation.Remove, key, null, null, null));
            _ = _entries.Remove(key);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            _invocations.Add(new(FakeMxCacheOperation.RemoveByTag, null, null, null, tag));

            foreach (var key in _entries
                .Where(entry => entry.Value.Tags.Contains(tag))
                .Select(entry => entry.Key)
                .ToArray())
            {
                _ = _entries.Remove(key);
            }
        }

        return Task.CompletedTask;
    }

    private static bool CanCache(CachePolicy policy)
    {
        return policy.Enabled && policy.Tier != CacheTier.None && policy.Ttl > TimeSpan.Zero;
    }

    private bool TryGetValue<T>(CacheKey key, out T? value)
    {
        if (_entries.TryGetValue(key, out var entry) && entry.ValueType == typeof(T))
        {
            value = entry.Value is null ? default : (T)entry.Value;
            return true;
        }

        value = default;
        return false;
    }

    private sealed record CacheEntry(object? Value, Type ValueType, HashSet<string> Tags);
}
