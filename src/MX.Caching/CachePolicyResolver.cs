using MX.Caching.Abstractions;

namespace MX.Caching;

internal sealed class CachePolicyResolver(MxCacheOptions options) : ICachePolicyResolver
{
    private readonly MxCacheOptions _options = options;

    public CachePolicy Resolve(
        CacheOperation operation,
        CachePolicy libraryDefault,
        CachePolicy? consumerOverride = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(libraryDefault);

        if (libraryDefault == CachePolicy.NotCached && consumerOverride is null)
        {
            return CachePolicy.NotCached;
        }

        var policy = consumerOverride ?? libraryDefault;
        policy = Apply(policy, _options.Policy);

        return _options.OperationPolicies.TryGetValue(operation.Key, out var operationPolicy)
            ? Apply(policy, operationPolicy)
            : policy;
    }

    private static CachePolicy Apply(CachePolicy policy, CachePolicyOptions overlay)
    {
        return policy with
        {
            Enabled = overlay.Enabled ?? policy.Enabled,
            Tier = overlay.Tier ?? policy.Tier,
            Ttl = overlay.Ttl ?? policy.Ttl,
            L1Ttl = overlay.L1Ttl ?? policy.L1Ttl,
            L2Ttl = overlay.L2Ttl ?? policy.L2Ttl,
            Tags = overlay.Tags ?? policy.Tags,
        };
    }
}
