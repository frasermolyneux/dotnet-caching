using System.Collections.Concurrent;
using MX.Caching.Abstractions;

namespace MX.Caching;

internal sealed class MemoryCacheTagIndex : ICacheTagIndex
{
    private readonly ConcurrentDictionary<string, long> _generations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CacheTagEntry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _tagKeys = new(StringComparer.Ordinal);

    public Task<IReadOnlyDictionary<string, long>> GetGenerationsAsync(
        IReadOnlyCollection<string> tags,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyDictionary<string, long> generations = tags.ToDictionary(
            tag => tag,
            tag => _generations.GetValueOrDefault(tag),
            StringComparer.Ordinal);
        return Task.FromResult(generations);
    }

    public Task RegisterAsync(
        string key,
        CacheTagEntry entry,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _entries[key] = entry;

        foreach (var tag in entry.TagGenerations.Keys)
        {
            var keys = _tagKeys.GetOrAdd(tag, static _ => new(StringComparer.Ordinal));
            keys[entry.EffectiveKey] = 0;
        }

        return Task.CompletedTask;
    }

    public Task<CacheTagEntry?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_entries.TryGetValue(key, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return Task.FromResult<CacheTagEntry?>(entry);
        }

        _ = _entries.TryRemove(key, out _);
        return Task.FromResult<CacheTagEntry?>(null);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = _entries.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<string>> InvalidateAsync(string tag, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = _generations.AddOrUpdate(tag, 1, static (_, generation) => checked(generation + 1));

        return Task.FromResult<IReadOnlyCollection<string>>(
            _tagKeys.TryRemove(tag, out var keys) ? [.. keys.Keys] : []);
    }
}
