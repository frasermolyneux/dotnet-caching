using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MX.Caching.Abstractions;
using MX.Caching.TableStorage;

namespace MX.Caching;

/// <summary>
/// Provides dependency-injection registration for MX caching.
/// </summary>
public static class MxCachingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the MX cache facade and its HybridCache dependency.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddMxCaching(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return AddMxCachingMemory(services);
    }

    /// <summary>
    /// Registers the MX cache facade using the in-memory distributed cache backend.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddMxCachingMemory(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return AddMxCaching(services, new MxCacheOptions { Backend = CacheBackend.Memory });
    }

    /// <summary>
    /// Registers the MX cache facade using the configured caching backend.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddMxCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new MxCacheOptions();
        configuration.GetSection(MxCacheOptions.SectionName).Bind(options);

        return AddMxCaching(services, options);
    }

    /// <summary>
    /// Registers the MX cache facade with programmatically configured options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures cache options.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddMxCaching(
        this IServiceCollection services,
        Action<MxCacheOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new MxCacheOptions();
        configure(options);
        return AddMxCaching(services, options);
    }

    private static IServiceCollection AddMxCaching(
        IServiceCollection services,
        MxCacheOptions options)
    {
        _ = options.Backend switch
        {
            CacheBackend.Memory => AddMxMemoryDistributedCache(services),
            CacheBackend.TableStorage => services.AddMxCachingTableStorage(options.TableStorage),
            CacheBackend.Redis or CacheBackend.Cosmos => throw new NotSupportedException(
                $"The configured MX cache backend '{options.Backend}' is not implemented."),
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };

        _ = services.AddHybridCache();
        services.TryAddSingleton<ICachePolicyResolver>(new CachePolicyResolver(options));
        services.TryAddSingleton<ICacheTagIndex, MemoryCacheTagIndex>();
        services.TryAddSingleton<IMxCacheMetrics, MxCacheMetrics>();
        services.TryAddSingleton<IMxCache, HybridMxCache>();
        return services;
    }

    private static IServiceCollection AddMxMemoryDistributedCache(IServiceCollection services)
    {
        services.TryAddSingleton<IDistributedCache, MxMemoryDistributedCache>();
        return services;
    }
}
