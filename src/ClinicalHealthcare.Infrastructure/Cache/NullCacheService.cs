namespace ClinicalHealthcare.Infrastructure.Cache;

/// <summary>
/// No-op <see cref="ICacheService"/> used when Redis is not configured.
/// All reads return null (cache miss); all writes and deletes are silent no-ops.
/// Enables the application to run without a Redis instance in development.
/// </summary>
public sealed class NullCacheService : ICacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class => Task.FromResult<T?>(null);

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
        where T : class => Task.CompletedTask;

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
