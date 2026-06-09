using System.IdentityModel.Tokens.Jwt;
using ClinicalHealthcare.Infrastructure.Auth;
using StackExchange.Redis;

namespace ClinicalHealthcare.Api.Middleware;

/// <summary>
/// Enforces the Redis JWT allowlist on every authenticated request (AC-002).
///
/// Behaviour per request:
/// <list type="bullet">
///   <item>Unauthenticated request → passes through without inspection.</item>
///   <item>
///     Authenticated request (jti present):
///     <list type="bullet">
///       <item>Redis reachable and key <c>session:{jti}</c> exists → reset TTL to 900 s, continue.</item>
///       <item>Redis reachable and key missing → 401 Unauthorized (session expired or logged out).</item>
///       <item>Redis unavailable (RedisException) → log WARNING, continue (signature-only fallback — AC-006).</item>
///     </list>
///   </item>
/// </list>
///
/// Must be registered AFTER <c>UseAuthentication</c> so <c>context.User</c> is populated.
/// Must be registered BEFORE <c>UseAuthorization</c> so an invalid allowlist entry is rejected
/// with 401 rather than being evaluated by policy.
/// </summary>
public sealed class SessionTtlMiddleware
{
    private const string SessionKeyPrefix = "jti:";

    private readonly RequestDelegate         _next;
    private readonly IConnectionMultiplexer? _multiplexer;
    private readonly ILogger<SessionTtlMiddleware> _logger;

    public SessionTtlMiddleware(
        RequestDelegate              next,
        ILogger<SessionTtlMiddleware> logger,
        IConnectionMultiplexer?      multiplexer = null)
    {
        _next        = next;
        _multiplexer = multiplexer;
        _logger      = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only inspect authenticated requests.
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var jti = context.User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value
               ?? context.User.FindFirst("jti")?.Value;

        if (string.IsNullOrEmpty(jti))
        {
            // Token lacks a jti claim — treat as invalid session.
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var key = $"{SessionKeyPrefix}{jti}";

        // When Redis is not configured, fall back to signature-only validation (AC-006).
        if (_multiplexer is null)
        {
            await _next(context);
            return;
        }

        try
        {
            var db     = _multiplexer.GetDatabase();
            var exists = await db.KeyExistsAsync(key).ConfigureAwait(false);

            if (!exists)
            {
                // Session expired or was explicitly revoked via logout.
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            // Reset the TTL so active sessions remain alive (AC-002 rolling window).
            await db.KeyExpireAsync(key, TimeSpan.FromSeconds(JwtTokenService.TokenExpirySeconds))
                    .ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            // AC-006: Redis unavailable → fall back to signature-only JWT validation.
            // Log a warning so operators can investigate, but do not block the request.
            _logger.LogWarning(ex,
                "Redis unavailable during session allowlist check for jti '{Jti}'. " +
                "Falling back to signature-only JWT validation (AC-006).", jti);
        }

        await _next(context);
    }
}
