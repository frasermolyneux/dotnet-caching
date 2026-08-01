namespace MX.Caching.Abstractions;

/// <summary>
/// Resolves the effective caching policy for a client operation.
/// </summary>
public interface ICachePolicyResolver
{
    /// <summary>
    /// Resolves a policy using configuration, an optional consumer override, and the library default.
    /// </summary>
    /// <param name="operation">The operation whose policy is being resolved.</param>
    /// <param name="libraryDefault">The policy defined by the client library.</param>
    /// <param name="consumerOverride">An optional policy supplied by the consuming application.</param>
    /// <returns>The effective cache policy.</returns>
    CachePolicy Resolve(
        CacheOperation operation,
        CachePolicy libraryDefault,
        CachePolicy? consumerOverride = null);
}
