namespace MX.Caching.Abstractions;

/// <summary>
/// Identifies a cacheable client operation without requiring policy resolution to parse a cache key.
/// </summary>
/// <param name="Client">The logical client or service name.</param>
/// <param name="Method">The logical operation name.</param>
public sealed record CacheOperation(string Client, string Method)
{
    /// <summary>
    /// Gets the stable configuration key for the operation.
    /// </summary>
    public string Key => $"{Client}:{Method}";
}
