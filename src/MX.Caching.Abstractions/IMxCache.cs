namespace MX.Caching.Abstractions;

/// <summary>
/// Provides cache operations using MX cache policies and normalized keys.
/// </summary>
public interface IMxCache
{
    /// <summary>
    /// Gets a cached value or obtains and caches it using <paramref name="factory"/>.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="policy">The cache policy.</param>
    /// <param name="factory">The asynchronous value factory used on a cache miss.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cached or newly created value.</returns>
    Task<T> GetOrCreateAsync<T>(
        CacheKey key,
        CachePolicy policy,
        Func<CancellationToken, ValueTask<T>> factory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to read a value from the cache.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cache read result.</returns>
    Task<CacheReadResult<T>> TryGetAsync<T>(CacheKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a value in the cache.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The value to cache.</param>
    /// <param name="policy">The cache policy.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task SetAsync<T>(
        CacheKey key,
        T value,
        CachePolicy policy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a value from the cache.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task RemoveAsync(CacheKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all values associated with a tag.
    /// </summary>
    /// <param name="tag">The cache tag.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default);
}
