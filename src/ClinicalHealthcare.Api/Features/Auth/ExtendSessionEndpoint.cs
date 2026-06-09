using System.IdentityModel.Tokens.Jwt;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ClinicalHealthcare.Api.Features.Auth;

/// <summary>
/// Vertical-slice definition for session extension.
///
/// Endpoint:
///   POST /auth/extend-session — issues a new JWT and rotates the Redis allowlist entry (AC-003)
/// </summary>
public sealed class ExtendSessionEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // IJwtTokenService registered by LoginEndpoint.AddServices — no duplicate needed.
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/extend-session", HandleExtendSession)
           .RequireAuthorization("AnyAuthenticated")
           .WithName("ExtendSession")
           .WithTags("Auth")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status401Unauthorized);
    }

    // ─────────────────────────────────────────────────────────────────────────
    private sealed record ExtendSessionResponse(string AccessToken, string TokenType, int ExpiresIn);

    private static async Task<IResult> HandleExtendSession(
        HttpContext                         httpContext,
        [FromServices] IJwtTokenService       tokenService,
        [FromServices] IConnectionMultiplexer? multiplexer,
        [FromServices] ILoggerFactory          loggerFactory,
        CancellationToken                   ct)
    {
        // Extract subject (userId) and jti from the current token's claims.
        var subClaim = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                    ?? httpContext.User.FindFirst("sub")?.Value;

        var oldJti = httpContext.User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value
                  ?? httpContext.User.FindFirst("jti")?.Value;

        var roleClaim      = httpContext.User.FindFirst("role")?.Value
                    ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        var firstNameClaim = httpContext.User.FindFirst(JwtRegisteredClaimNames.GivenName)?.Value  ?? "";
        var lastNameClaim  = httpContext.User.FindFirst(JwtRegisteredClaimNames.FamilyName)?.Value ?? "";

        if (!int.TryParse(subClaim, out var userId) || string.IsNullOrEmpty(oldJti) || string.IsNullOrEmpty(roleClaim))
            return Results.Unauthorized();

        var logger = loggerFactory.CreateLogger<ExtendSessionEndpoint>();

        // Issue a new token.
        var (newToken, newJti) = tokenService.GenerateToken(userId, roleClaim, firstNameClaim, lastNameClaim);

        // Rotate the Redis allowlist: add new jti, remove old jti (AC-003).
        if (multiplexer is not null)
        try
        {
            var db = multiplexer.GetDatabase();

            await db.StringSetAsync(
                $"{LoginEndpoint.SessionKeyPrefix}{newJti}",
                userId.ToString(),
                TimeSpan.FromSeconds(JwtTokenService.TokenExpirySeconds)).ConfigureAwait(false);

            await db.KeyDeleteAsync($"{LoginEndpoint.SessionKeyPrefix}{oldJti}").ConfigureAwait(false);

            // TASK_017 (AC-004): rotate the JTI in the user-sessions set so the
            // bulk-revocation at password-reset always sees the current active JTI.
            var userSessionsKey = $"{ResetPasswordEndpoint.UserSessionsKeyPrefix}{userId}";
            await db.SetRemoveAsync(userSessionsKey, oldJti).ConfigureAwait(false);
            await db.SetAddAsync(userSessionsKey, newJti).ConfigureAwait(false);
            await db.KeyExpireAsync(userSessionsKey,
                TimeSpan.FromSeconds(JwtTokenService.TokenExpirySeconds)).ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            // AC-006: Redis unavailable — new token is still valid via signature-only validation.
            logger.LogWarning(ex,
                "Redis unavailable at session extension for user {UserId}. Allowlist not rotated. " +
                "Falling back to signature-only JWT validation (AC-006).", userId);
        }

        return Results.Ok(new ExtendSessionResponse(newToken, "Bearer", JwtTokenService.TokenExpirySeconds));
    }
}
