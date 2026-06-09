namespace ClinicalHealthcare.Infrastructure.Cache;

/// <summary>
/// Cache-aside abstraction over Redis.
/// All operations are non-throwing: a Redis outage must never crash the request.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, or <c>null</c> if absent or Redis
    /// is unavailable.
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Stores <paramref name="value"/> under <paramref name="key"/> with the given
    /// <paramref name="ttl"/>.  Silently no-ops when Redis is unavailable.
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Removes <paramref name="key"/> from the cache.  Silently no-ops when Redis is unavailable.
    /// </summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
