using Microsoft.Extensions.DependencyInjection;
using MX.Caching.Abstractions;
using Xunit;

namespace MX.Caching.Tests;

/// <summary>
/// Verifies cache policy resolution and consumer replacement behavior.
/// </summary>
public sealed class CachePolicyResolverTests
{
    /// <summary>
    /// Verifies configuration has precedence over a consumer override and a library default.
    /// </summary>
    [Fact]
    public void ResolveWhenConfigurationAndOverridesExistUsesConfigurationPrecedence()
    {
        var resolver = CreateResolver(options =>
        {
            options.Policy = new CachePolicyOptions
            {
                Enabled = false,
                Ttl = TimeSpan.FromMinutes(10),
                Tags = ["global"],
            };
            options.OperationPolicies["Client:Get"] = new CachePolicyOptions
            {
                Tier = CacheTier.InProcess,
                Ttl = TimeSpan.FromMinutes(15),
                Tags = ["operation"],
            };
        });
        var libraryDefault = new CachePolicy
        {
            Tier = CacheTier.Distributed,
            Ttl = TimeSpan.FromMinutes(1),
            Tags = ["library"],
        };
        var consumerOverride = new CachePolicy
        {
            Enabled = true,
            Tier = CacheTier.Tiered,
            Ttl = TimeSpan.FromMinutes(5),
            Tags = ["consumer"],
        };

        var policy = resolver.Resolve(new CacheOperation("Client", "Get"), libraryDefault, consumerOverride);

        Assert.False(policy.Enabled);
        Assert.Equal(CacheTier.InProcess, policy.Tier);
        Assert.Equal(TimeSpan.FromMinutes(15), policy.Ttl);
        Assert.Equal(["operation"], policy.Tags);
    }

    /// <summary>
    /// Verifies omitted configuration values retain values from the consumer override.
    /// </summary>
    [Fact]
    public void ResolveWhenConfigurationOmitsValuesRetainsConsumerOverrideValues()
    {
        var resolver = CreateResolver(options =>
        {
            options.Policy = new CachePolicyOptions { Ttl = TimeSpan.FromMinutes(10) };
        });
        var consumerOverride = new CachePolicy
        {
            Enabled = false,
            Tier = CacheTier.InProcess,
            Ttl = TimeSpan.FromMinutes(5),
            L1Ttl = TimeSpan.FromMinutes(2),
            Tags = ["consumer"],
        };

        var policy = resolver.Resolve(new CacheOperation("Client", "Get"), new CachePolicy(), consumerOverride);

        Assert.False(policy.Enabled);
        Assert.Equal(CacheTier.InProcess, policy.Tier);
        Assert.Equal(TimeSpan.FromMinutes(10), policy.Ttl);
        Assert.Equal(TimeSpan.FromMinutes(2), policy.L1Ttl);
        Assert.Equal(["consumer"], policy.Tags);
    }

    /// <summary>
    /// Verifies a library policy that explicitly bypasses caching cannot be enabled by configuration.
    /// </summary>
    [Fact]
    public void ResolveWhenLibraryDefaultIsNotCachedAndNoConsumerOverrideReturnsNotCached()
    {
        var resolver = CreateResolver(options =>
        {
            options.Policy = new CachePolicyOptions { Enabled = true, Tier = CacheTier.Tiered };
            options.OperationPolicies["Client:Get"] = new CachePolicyOptions
            {
                Enabled = true,
                Tier = CacheTier.InProcess,
            };
        });

        var policy = resolver.Resolve(new CacheOperation("Client", "Get"), CachePolicy.NotCached);

        Assert.Equal(CachePolicy.NotCached, policy);
    }

    /// <summary>
    /// Verifies an explicit consumer override may enable a library operation that defaults to uncached.
    /// </summary>
    [Fact]
    public void ResolveWhenLibraryDefaultIsNotCachedAndConsumerOverridesEnablesCaching()
    {
        var resolver = CreateResolver();
        var consumerOverride = new CachePolicy
        {
            Enabled = true,
            Tier = CacheTier.InProcess,
            Ttl = TimeSpan.FromMinutes(3),
        };

        var policy = resolver.Resolve(new CacheOperation("Client", "Get"), CachePolicy.NotCached, consumerOverride);

        Assert.Equal(consumerOverride, policy);
    }

    /// <summary>
    /// Verifies operation policy keys match the client and method name using ordinal casing.
    /// </summary>
    [Fact]
    public void ResolveWhenOperationKeyCasingDiffersDoesNotApplyOperationPolicy()
    {
        var resolver = CreateResolver(options =>
        {
            options.OperationPolicies["Client:Get"] = new CachePolicyOptions { Ttl = TimeSpan.FromMinutes(10) };
        });
        var libraryDefault = new CachePolicy { Ttl = TimeSpan.FromMinutes(1) };

        var policy = resolver.Resolve(new CacheOperation("client", "Get"), libraryDefault);

        Assert.Equal(TimeSpan.FromMinutes(1), policy.Ttl);
    }

    /// <summary>
    /// Verifies resolving a policy leaves supplied policy records unchanged.
    /// </summary>
    [Fact]
    public void ResolveDoesNotMutateSuppliedPolicies()
    {
        var resolver = CreateResolver(options =>
        {
            options.Policy = new CachePolicyOptions { Ttl = TimeSpan.FromMinutes(10), Tags = ["configuration"] };
        });
        var libraryDefault = new CachePolicy { Ttl = TimeSpan.FromMinutes(1), Tags = ["library"] };
        var consumerOverride = new CachePolicy { Ttl = TimeSpan.FromMinutes(5), Tags = ["consumer"] };

        _ = resolver.Resolve(new CacheOperation("Client", "Get"), libraryDefault, consumerOverride);

        Assert.Equal(TimeSpan.FromMinutes(1), libraryDefault.Ttl);
        Assert.Equal(["library"], libraryDefault.Tags);
        Assert.Equal(TimeSpan.FromMinutes(5), consumerOverride.Ttl);
        Assert.Equal(["consumer"], consumerOverride.Tags);
    }

    /// <summary>
    /// Verifies the default resolver does not replace a consumer registration.
    /// </summary>
    [Fact]
    public void AddMxCachingWhenResolverIsAlreadyRegisteredPreservesIt()
    {
        var customResolver = new TestCachePolicyResolver();
        var services = new ServiceCollection();
        _ = services.AddSingleton<ICachePolicyResolver>(customResolver);
        _ = services.AddMxCachingMemory();

        using var serviceProvider = services.BuildServiceProvider();

        Assert.Same(customResolver, serviceProvider.GetRequiredService<ICachePolicyResolver>());
    }

    private static ICachePolicyResolver CreateResolver(Action<MxCacheOptions>? configure = null)
    {
        var services = new ServiceCollection();
        _ = configure is null
            ? services.AddMxCachingMemory()
            : services.AddMxCaching(options =>
            {
                options.Backend = CacheBackend.Memory;
                configure(options);
            });

        using var serviceProvider = services.BuildServiceProvider();
        return serviceProvider.GetRequiredService<ICachePolicyResolver>();
    }

    private sealed class TestCachePolicyResolver : ICachePolicyResolver
    {
        public CachePolicy Resolve(
            CacheOperation operation,
            CachePolicy libraryDefault,
            CachePolicy? consumerOverride = null)
        {
            return consumerOverride ?? libraryDefault;
        }
    }
}
