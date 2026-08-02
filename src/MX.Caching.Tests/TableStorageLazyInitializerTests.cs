using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Moq;
using MX.Caching.TableStorage;
using Xunit;

namespace MX.Caching.Tests;

/// <summary>
/// Verifies lazy initialization behavior of <see cref="TableStorageDistributedCache"/>.
/// </summary>
public sealed class TableStorageLazyInitializerTests
{
    /// <summary>
    /// Verifies that concurrent operations trigger table creation exactly once.
    /// </summary>
    [Fact]
    public async Task GetAsyncWhenMultipleConcurrentCallsAreInFlightCreatesTableOnlyOnce()
    {
        var (serviceClient, tableClient) = CreateMocks();
        var createTableGate = new TaskCompletionSource<Response<TableItem>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var createCallCount = 0;

        _ = tableClient
            .Setup(c => c.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                _ = Interlocked.Increment(ref createCallCount);
                return createTableGate.Task;
            });

        _ = tableClient
            .Setup(c => c.GetEntityAsync<TableEntity>(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not Found", "ResourceNotFound", null));

        var cache = new TableStorageDistributedCache(serviceClient.Object, "test");

        var task1 = cache.GetAsync("key1");
        var task2 = cache.GetAsync("key2");
        var task3 = cache.GetAsync("key3");

        createTableGate.SetResult(null);
        _ = await Task.WhenAll(task1, task2, task3);

        Assert.Equal(1, createCallCount);
    }

    /// <summary>
    /// Verifies that a transient init failure does not permanently poison the cache;
    /// subsequent operations reattempt initialization.
    /// </summary>
    [Fact]
    public async Task GetAsyncWhenInitFailsOnFirstCallRetrySucceedsOnSubsequentCall()
    {
        var (serviceClient, tableClient) = CreateMocks();

        _ = tableClient
            .SetupSequence(c => c.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(503, "Service Unavailable"))
            .Returns(Task.FromResult<Response<TableItem>?>(null));

        _ = tableClient
            .Setup(c => c.GetEntityAsync<TableEntity>(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not Found", "ResourceNotFound", null));

        var cache = new TableStorageDistributedCache(serviceClient.Object, "test");

        _ = await Assert.ThrowsAsync<RequestFailedException>(() => cache.GetAsync("key"));

        var result = await cache.GetAsync("key");
        Assert.Null(result);

        tableClient.Verify(
            c => c.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    /// <summary>
    /// Verifies that once successfully initialized, subsequent calls skip table creation.
    /// </summary>
    [Fact]
    public async Task GetAsyncWhenAlreadyInitializedSkipsTableCreationOnSubsequentCalls()
    {
        var (serviceClient, tableClient) = CreateMocks();

        _ = tableClient
            .Setup(c => c.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<Response<TableItem>?>(null));

        _ = tableClient
            .Setup(c => c.GetEntityAsync<TableEntity>(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not Found", "ResourceNotFound", null));

        var cache = new TableStorageDistributedCache(serviceClient.Object, "test");

        _ = await cache.GetAsync("key1");
        _ = await cache.GetAsync("key2");
        _ = await cache.GetAsync("key3");

        tableClient.Verify(
            c => c.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static (Mock<TableServiceClient> ServiceClient, Mock<TableClient> TableClient) CreateMocks()
    {
        var mockTableClient = new Mock<TableClient>();
        var mockServiceClient = new Mock<TableServiceClient>();
        _ = mockServiceClient
            .Setup(s => s.GetTableClient(It.IsAny<string>()))
            .Returns(mockTableClient.Object);
        return (mockServiceClient, mockTableClient);
    }
}

/// <summary>
/// Verifies lazy initialization behavior of <see cref="TableStorageCacheTagIndex"/>.
/// </summary>
public sealed class TableStorageCacheTagIndexLazyInitTests
{
    /// <summary>
    /// Verifies that concurrent GetGenerationsAsync calls trigger table creation exactly once.
    /// </summary>
    [Fact]
    public async Task GetGenerationsAsyncWhenMultipleConcurrentCallsAreInFlightCreatesTableOnlyOnce()
    {
        var (serviceClient, tableClient) = CreateMocks();
        var createTableGate = new TaskCompletionSource<Response<TableItem>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var createCallCount = 0;

        _ = tableClient
            .Setup(c => c.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                _ = Interlocked.Increment(ref createCallCount);
                return createTableGate.Task;
            });

        _ = tableClient
            .Setup(c => c.GetEntityAsync<TableEntity>(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not Found", "ResourceNotFound", null));

        var index = new TableStorageCacheTagIndex(serviceClient.Object, "test");

        var task1 = index.GetGenerationsAsync(["tag1"]);
        var task2 = index.GetGenerationsAsync(["tag2"]);

        createTableGate.SetResult(null);
        _ = await Task.WhenAll(task1, task2);

        Assert.Equal(1, createCallCount);
    }

    /// <summary>
    /// Verifies that a transient init failure does not permanently poison the tag index.
    /// </summary>
    [Fact]
    public async Task GetGenerationsAsyncWhenInitFailsOnFirstCallRetrySucceedsOnSubsequentCall()
    {
        var (serviceClient, tableClient) = CreateMocks();

        _ = tableClient
            .SetupSequence(c => c.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(503, "Service Unavailable"))
            .Returns(Task.FromResult<Response<TableItem>?>(null));

        _ = tableClient
            .Setup(c => c.GetEntityAsync<TableEntity>(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not Found", "ResourceNotFound", null));

        var index = new TableStorageCacheTagIndex(serviceClient.Object, "test");

        _ = await Assert.ThrowsAsync<RequestFailedException>(() => index.GetGenerationsAsync(["tag"]));

        var result = await index.GetGenerationsAsync(["tag"]);
        Assert.Equal(0, result["tag"]);

        tableClient.Verify(
            c => c.CreateIfNotExistsAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    private static (Mock<TableServiceClient> ServiceClient, Mock<TableClient> TableClient) CreateMocks()
    {
        var mockTableClient = new Mock<TableClient>();
        var mockServiceClient = new Mock<TableServiceClient>();
        _ = mockServiceClient
            .Setup(s => s.GetTableClient(It.IsAny<string>()))
            .Returns(mockTableClient.Object);
        return (mockServiceClient, mockTableClient);
    }
}
