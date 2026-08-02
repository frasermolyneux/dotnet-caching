using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MX.Caching.Abstractions;

namespace MX.Caching.TableStorage;

/// <summary>
/// Provides dependency-injection registration for Azure Table Storage caching.
/// </summary>
public static class TableStorageServiceCollectionExtensions
{
    /// <summary>
    /// Registers an Azure Table Storage-backed distributed cache using managed identity credentials.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The Table Storage cache options.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddMxCachingTableStorage(
        this IServiceCollection services,
        TableStorageCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.TableName);

        services.TryAddSingleton(new TableServiceClient(options.Endpoint, new DefaultAzureCredential()));
        services.TryAddSingleton<TableStorageCacheMetrics>();
        services.TryAddSingleton<ICacheTagIndex>(serviceProvider => new TableStorageCacheTagIndex(
            serviceProvider.GetRequiredService<TableServiceClient>(),
            options.TableName,
            serviceProvider.GetRequiredService<TableStorageCacheMetrics>()));
        services.TryAddSingleton<IDistributedCache>(serviceProvider => new TableStorageDistributedCache(
            serviceProvider.GetRequiredService<TableServiceClient>(),
            options.TableName,
            serviceProvider.GetRequiredService<TableStorageCacheMetrics>()));
        return services;
    }
}
