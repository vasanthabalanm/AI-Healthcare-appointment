using System.IdentityModel.Tokens.Jwt;
using ClinicalHealthcare.Api.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ClinicalHealthcare.Api.Features.Auth;

/// <summary>
/// Vertical-slice definition for session termination.
///
/// Endpoint:
///   POST /auth/logout — removes the jti from the Redis allowlist, invalidating the session (AC-004)
/// </summary>
public sealed class LogoutEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // No additional services required.
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/logout", HandleLogout)
           .RequireAuthorization("AnyAuthenticated")
           .WithName("Logout")
           .WithTags("Auth")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status401Unauthorized);
    }

    // ─────────────────────────────────────────────────────────────────────────
    private static async Task<IResult> HandleLogout(
        HttpContext                         httpContext,
        [FromServices] IConnectionMultiplexer? multiplexer,
        [FromServices] ILoggerFactory          loggerFactory,
        CancellationToken                   ct)
    {
        var jti = httpContext.User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value
               ?? httpContext.User.FindFirst("jti")?.Value;

        if (string.IsNullOrEmpty(jti))
            return Results.Unauthorized();

        var logger = loggerFactory.CreateLogger<LogoutEndpoint>();

        try
        {
            if (multiplexer is null) return Results.Ok();
            var db = multiplexer.GetDatabase();
            await db.KeyDeleteAsync($"{LoginEndpoint.SessionKeyPrefix}{jti}").ConfigureAwait(false);

            // TASK_017 (AC-004): remove this JTI from the user-sessions set so that
            // a subsequent password-reset revocation does not try to delete an already-gone key.
            var userIdStr = httpContext.User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                         ?? httpContext.User.FindFirst("sub")?.Value;
            if (int.TryParse(userIdStr, out var userId))
            {
                await db.SetRemoveAsync(
                    $"{ResetPasswordEndpoint.UserSessionsKeyPrefix}{userId}",
                    jti).ConfigureAwait(false);
            }
        }
        catch (RedisException ex)
        {
            // AC-006: Redis unavailable — key deletion is best-effort; session expires
            // naturally when the JWT TTL lapses.
            logger.LogWarning(ex,
                "Redis unavailable at logout for jti '{Jti}'. Allowlist entry not deleted. " +
                "Session will expire when JWT TTL lapses (AC-006).", jti);
        }

        return Results.Ok();
    }
}
