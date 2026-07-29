using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MX.Caching;

internal sealed class MxMemoryDistributedCache(
    IOptions<MemoryDistributedCacheOptions> options) : IDistributedCache
{
    private readonly MemoryDistributedCache _inner = new(options, NullLoggerFactory.Instance);

    public byte[]? Get(string key)
    {
        return _inner.Get(key);
    }

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        return _inner.GetAsync(key, token);
    }

    public void Refresh(string key)
    {
        _inner.Refresh(key);
    }

    public Task RefreshAsync(string key, CancellationToken token = default)
    {
        return _inner.RefreshAsync(key, token);
    }

    public void Remove(string key)
    {
        _inner.Remove(key);
    }

    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        return _inner.RemoveAsync(key, token);
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        _inner.Set(key, value, options);
    }

    public Task SetAsync(
        string key,
        byte[] value,
        DistributedCacheEntryOptions options,
        CancellationToken token = default)
    {
        return _inner.SetAsync(key, value, options, token);
    }
}
