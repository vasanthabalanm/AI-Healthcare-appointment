using Microsoft.Extensions.Caching.Memory;

namespace ClinicalHealthcare.Infrastructure.Cache;

/// <summary>
/// In-process <see cref="ICacheService"/> backed by <see cref="IMemoryCache"/>.
/// Used in development when Redis is not configured — sessions survive the
/// lifetime of the API process.
/// </summary>
public sealed class MemoryCacheService : ICacheService
{
    private readonly IMemoryCache _cache;

    public MemoryCacheService(IMemoryCache cache) => _cache = cache;

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class
        => Task.FromResult(_cache.TryGetValue(key, out T? value) ? value : null);

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
        where T : class
    {
        _cache.Set(key, value, ttl);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        _cache.Remove(key);
        return Task.CompletedTask;
    }
}
