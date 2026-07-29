namespace MX.Caching.Abstractions;

/// <summary>
/// Represents the outcome of a cache read operation.
/// </summary>
/// <typeparam name="T">The cached value type.</typeparam>
public readonly record struct CacheReadResult<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CacheReadResult{T}"/> struct.
    /// </summary>
    /// <param name="found">Whether the cache contained a value.</param>
    /// <param name="value">The cached value.</param>
    public CacheReadResult(bool found, T? value)
    {
        Found = found;
        Value = value;
    }

    /// <summary>
    /// Gets a value indicating whether the cache contained a value.
    /// </summary>
    public bool Found { get; }

    /// <summary>
    /// Gets the cached value when <see cref="Found"/> is <see langword="true"/>.
    /// </summary>
    public T? Value { get; }
}
