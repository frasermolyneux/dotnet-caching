namespace MX.Caching.Abstractions;

/// <summary>
/// Tracks tag generations and the effective keys associated with tagged cache entries.
/// </summary>
public interface ICacheTagIndex
{
    /// <summary>
    /// Gets the current generation for each normalized tag.
    /// </summary>
    /// <param name="tags">The normalized tags to resolve.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The tag generations keyed by tag.</returns>
    Task<IReadOnlyDictionary<string, long>> GetGenerationsAsync(
        IReadOnlyCollection<string> tags,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the current effective key and tag state for a tagged logical cache key.
    /// </summary>
    /// <param name="key">The logical cache key.</param>
    /// <param name="entry">The effective key, tag generations, and expiry associated with the entry.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task RegisterAsync(
        string key,
        CacheTagEntry entry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the tagged entry currently associated with a logical cache key.
    /// </summary>
    /// <param name="key">The logical cache key.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The tagged entry, or <see langword="null"/> when none is registered.</returns>
    Task<CacheTagEntry?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the effective-key association for a logical cache key.
    /// </summary>
    /// <param name="key">The logical cache key.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Advances a tag generation and returns the effective keys associated with its prior generation.
    /// </summary>
    /// <param name="tag">The normalized tag to invalidate.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The effective keys that can be removed from the distributed cache.</returns>
    Task<IReadOnlyCollection<string>> InvalidateAsync(string tag, CancellationToken cancellationToken = default);
}
