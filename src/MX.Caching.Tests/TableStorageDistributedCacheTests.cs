using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Microsoft.Extensions.Caching.Distributed;
using Moq;
using MX.Caching.TableStorage;
using Xunit;

namespace MX.Caching.Tests;

/// <summary>
/// Verifies oversize-value rejection and metrics behavior of <see cref="TableStorageDistributedCache"/>.
/// </summary>
public sealed class TableStorageDistributedCacheTests
{
    /// <summary>
    /// Verifies that a value exactly at the size limit is accepted.
    /// </summary>
    [Fact]
    public async Task SetAsyncWhenValueIsAtMaximumSizeSucceeds()
    {
        var (serviceClient, tableClient) = CreateInitializedMocks();
        _ = tableClient
            .Setup(c => c.UpsertEntityAsync(
                It.IsAny<TableEntity>(), It.IsAny<TableUpdateMode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response>());

        var cache = new TableStorageDistributedCache(serviceClient.Object, "test");
        var value = new byte[TableStorageDistributedCache.MaximumValueLength];

        await cache.SetAsync("key", value, new DistributedCacheEntryOptions());
    }

    /// <summary>
    /// Verifies that a value one byte over the size limit throws <see cref="CacheValueTooLargeException"/>.
    /// </summary>
    [Fact]
    public async Task SetAsyncWhenValueExceedsMaximumSizeThrowsCacheValueTooLargeException()
    {
        var cache = new TableStorageDistributedCache(
            Mock.Of<TableServiceClient>(), "test");
        var value = new byte[TableStorageDistributedCache.MaximumValueLength + 1];

        var exception = await Assert.ThrowsAsync<CacheValueTooLargeException>(
            () => cache.SetAsync("key", value, new DistributedCacheEntryOptions()));

        Assert.Equal(value.Length, exception.ValueLength);
        Assert.Equal(TableStorageDistributedCache.MaximumValueLength, exception.MaximumLength);
        Assert.Equal("value", exception.ParamName);
    }

    /// <summary>
    /// Verifies that <see cref="CacheValueTooLargeException"/> is also catchable as
    /// <see cref="ArgumentOutOfRangeException"/> for backward-compatible error handling.
    /// </summary>
    [Fact]
    public async Task SetAsyncWhenValueExceedsMaximumSizeIsCatchableAsArgumentOutOfRangeException()
    {
        var cache = new TableStorageDistributedCache(
            Mock.Of<TableServiceClient>(), "test");
        var value = new byte[TableStorageDistributedCache.MaximumValueLength + 1];

        ArgumentOutOfRangeException? caught = null;
        try
        {
            await cache.SetAsync("key", value, new DistributedCacheEntryOptions());
        }
        catch (ArgumentOutOfRangeException ex)
        {
            caught = ex;
        }

        Assert.NotNull(caught);
        _ = Assert.IsType<CacheValueTooLargeException>(caught);
    }

    /// <summary>
    /// Verifies that an oversize-value rejection increments the oversize-rejection counter.
    /// </summary>
    [Fact]
    public async Task SetAsyncWhenValueExceedsMaximumSizeRecordsOversizeRejectionMetric()
    {
        using var metrics = new TableStorageCacheMetrics();
        var (serviceClient, _) = CreateInitializedMocks();
        var cache = new TableStorageDistributedCache(serviceClient.Object, "test", metrics);
        var value = new byte[TableStorageDistributedCache.MaximumValueLength + 1];

        var rejectionCount = 0L;
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "MX.Caching" &&
                instrument.Name == "mx.cache.storage.oversize_rejections")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
        {
            _ = Interlocked.Add(ref rejectionCount, measurement);
        });
        listener.Start();

        _ = await Assert.ThrowsAsync<CacheValueTooLargeException>(
            () => cache.SetAsync("key", value, new DistributedCacheEntryOptions()));

        Assert.Equal(1L, Interlocked.Read(ref rejectionCount));
    }

    /// <summary>
    /// Verifies that a non-404 storage error is propagated to the caller without swallowing.
    /// </summary>
    [Fact]
    public async Task GetAsyncWhenStorageThrowsNon404ExceptionPropagatesException()
    {
        var (serviceClient, tableClient) = CreateInitializedMocks();
        _ = tableClient
            .Setup(c => c.GetEntityAsync<TableEntity>(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(503, "Service Unavailable"));

        var cache = new TableStorageDistributedCache(serviceClient.Object, "test");

        _ = await Assert.ThrowsAsync<RequestFailedException>(() => cache.GetAsync("key"));
    }

    private static (Mock<TableServiceClient> ServiceClient, Mock<TableClient> TableClient) CreateInitializedMocks()
    {
        var mockTableClient = new Mock<TableClient>();
        var mockServiceClient = new Mock<TableServiceClient>();

        _ = mockServiceClient
            .Setup(s => s.GetTableClient(It.IsAny<string>()))
            .Returns(mockTableClient.Object);

        _ = mockTableClient
            .Setup(c => c.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Response<TableItem>?>(null));

        return (mockServiceClient, mockTableClient);
    }
}
