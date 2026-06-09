using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ClinicalHealthcare.Infrastructure.Cache;

/// <summary>
/// Redis-backed cache-aside implementation.
/// All operations catch <see cref="RedisException"/> and <see cref="JsonException"/> and log a
/// WARNING so that a Redis outage or malformed cache value degrades gracefully without throwing
/// to callers (AC-005).
/// </summary>
public sealed class CacheService : ICacheService
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly ILogger<CacheService> _logger;

    /// <summary>
    /// Resolved TTL constants.  Feature slices can read these directly rather than re-injecting
    /// <see cref="IOptions{CacheSettings}"/>.
    /// </summary>
    public CacheSettings Settings { get; }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CacheService(
        IConnectionMultiplexer multiplexer,
        IOptions<CacheSettings> settings,
        ILogger<CacheService> logger)
    {
        _multiplexer = multiplexer;
        Settings     = settings.Value;
        _logger      = logger;
    }

    /// <inheritdoc/>
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class
    {
        try
        {
            var db    = _multiplexer.GetDatabase();
            var value = await db.StringGetAsync(key).ConfigureAwait(false);

            if (!value.HasValue)
                return null;

            return JsonSerializer.Deserialize<T>(value!, SerializerOptions);
        }
        catch (Exception ex) when (ex is RedisException or JsonException)
        {
            _logger.LogWarning(ex, "Cache GET failed for key '{Key}'. Returning null (cache miss).", key);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
        where T : class
    {
        try
        {
            var db         = _multiplexer.GetDatabase();
            var serialised = JsonSerializer.Serialize(value, SerializerOptions);
            await db.StringSetAsync(key, serialised, ttl).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or JsonException)
        {
            _logger.LogWarning(ex, "Cache SET failed for key '{Key}'. Skipping cache write.", key);
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _multiplexer.GetDatabase();
            await db.KeyDeleteAsync(key).ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Cache DELETE failed for key '{Key}'. Skipping eviction.", key);
        }
    }
}
