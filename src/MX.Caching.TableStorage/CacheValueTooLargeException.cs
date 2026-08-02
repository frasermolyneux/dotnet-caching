namespace MX.Caching.TableStorage;

/// <summary>
/// Thrown when a value written to the Azure Table Storage cache exceeds the maximum allowed size.
/// Inherits <see cref="ArgumentOutOfRangeException"/> so existing catch blocks that handle
/// the predecessor exception type continue to work.
/// </summary>
public sealed class CacheValueTooLargeException(int valueLength, int maximumLength)
    : ArgumentOutOfRangeException(
        "value",
        valueLength,
        $"Cache value of {valueLength} bytes exceeds the Azure Table Storage binary-property limit of {maximumLength} bytes.")
{
    /// <summary>
    /// Gets the byte length of the value that was rejected.
    /// </summary>
    public int ValueLength => (int)ActualValue!;

    /// <summary>
    /// Gets the maximum permitted byte length for a cache entry value.
    /// </summary>
    public int MaximumLength { get; } = maximumLength;
}
