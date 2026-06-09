using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Auth;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace ClinicalHealthcare.Api.Features.Auth;

/// <summary>
/// Vertical-slice definition for user authentication.
///
/// Endpoints:
///   POST /auth/login  -- validates credentials, issues JWT, stores jti in Redis allowlist (AC-001..AC-005)
/// </summary>
public sealed class LoginEndpoint : IEndpointDefinition
{
    /// <summary>Redis key prefix for the JWT allowlist (AC-002). Matches spec pattern <c>jti:{guid}</c>.</summary>
    public const string SessionKeyPrefix = "jti:";

    /// <summary>Maximum consecutive failed logins before lockout (AC-005).</summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>Lockout duration in minutes (AC-005).</summary>
    public const int LockoutMinutes = 15;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // IJwtTokenService -- stateless singleton; constructor reads JWT_SECRET at startup.
        if (!services.Any(d => d.ServiceType == typeof(IJwtTokenService)))
        {
            services.AddSingleton<IJwtTokenService, JwtTokenService>();
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/login", HandleLogin)
           .AllowAnonymous()
           .WithName("Login")
           .WithTags("Auth")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status401Unauthorized)
           .Produces(StatusCodes.Status422UnprocessableEntity)
           .Produces(StatusCodes.Status423Locked);
    }

    // -------------------------------------------------------------------------
    private sealed record LoginRequest(string Email, string Password);

    private sealed record LoginResponse(string AccessToken, string TokenType, int ExpiresIn);

    private static async Task<IResult> HandleLogin(
        LoginRequest                       request,
        ApplicationDbContext               db,
        IPasswordHasher<string>            hasher,
        [FromServices] IJwtTokenService      tokenService,
        [FromServices] IConnectionMultiplexer? multiplexer,
        [FromServices] ILoggerFactory        loggerFactory,
        CancellationToken                  ct)
    {
        var logger = loggerFactory.CreateLogger<LoginEndpoint>();

        // 1. Basic input validation
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return Results.UnprocessableEntity(new { error = "Email and password are required." });

        // 2. Load account
        var account = await db.UserAccounts
            .FirstOrDefaultAsync(u => u.Email == request.Email.Trim().ToLowerInvariant(), ct);

        if (account is null || !account.IsActive)
            return Results.Unauthorized();

        // 3. Lockout check (AC-005)
        if (account.LockoutEnd.HasValue && account.LockoutEnd.Value > DateTimeOffset.UtcNow)
            return Results.StatusCode(StatusCodes.Status423Locked);

        // 4. Verify password
        var verifyResult = hasher.VerifyHashedPassword(
            account.Email, account.PasswordHash, request.Password);

        if (verifyResult == PasswordVerificationResult.Failed)
        {
            account.FailedLoginAttempts++;

            if (account.FailedLoginAttempts >= MaxFailedAttempts)
            {
                account.LockoutEnd          = DateTimeOffset.UtcNow.AddMinutes(LockoutMinutes);
                account.FailedLoginAttempts = 0;

                // F7: audit log for account lockout (security-critical state change)
                AuditLogHelper.Stage(db, "UserAccount", account.Id, actorId: null,
                    "LOCKOUT",
                    before: null,
                    after: new { reason = "5 consecutive failed login attempts" });
            }

            await db.SaveChangesAsync(ct);
            return Results.Unauthorized();
        }

        // 5. Handle rehash (F5: SuccessRehashNeeded -- update stored hash to latest algorithm)
        if (verifyResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            account.PasswordHash = hasher.HashPassword(account.Email, request.Password);
        }

        // 6. Reset failed-attempt counter on successful authentication
        if (account.FailedLoginAttempts > 0 || account.LockoutEnd.HasValue || verifyResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            account.FailedLoginAttempts = 0;
            account.LockoutEnd          = null;
            await db.SaveChangesAsync(ct);
        }

        // 7. Issue JWT and store jti in Redis allowlist (AC-001 + AC-002)
        var (token, jti) = tokenService.GenerateToken(account.Id, account.Role, account.FirstName, account.LastName);

        if (multiplexer is not null)
        try
        {
            var redisDb = multiplexer.GetDatabase();
            await redisDb.StringSetAsync(
                $"{SessionKeyPrefix}{jti}",
                account.Id.ToString(),
                TimeSpan.FromSeconds(JwtTokenService.TokenExpirySeconds)).ConfigureAwait(false);

            // TASK_017 (AC-004): maintain a per-user set of active JTIs so that
            // ResetPasswordEndpoint can revoke all sessions in one Redis operation.
            await redisDb.SetAddAsync(
                $"{ResetPasswordEndpoint.UserSessionsKeyPrefix}{account.Id}",
                jti).ConfigureAwait(false);

            // TTL on the set matches the token TTL to prevent unbounded growth.
            await redisDb.KeyExpireAsync(
                $"{ResetPasswordEndpoint.UserSessionsKeyPrefix}{account.Id}",
                TimeSpan.FromSeconds(JwtTokenService.TokenExpirySeconds)).ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            // AC-006: Redis unavailable -- token is still valid via signature-only validation.
            // SessionTtlMiddleware will detect the missing key and apply fallback behaviour.
            logger.LogWarning(ex,
                "Redis unavailable at login for user {UserId}. Allowlist entry not written. " +
                "Falling back to signature-only JWT validation (AC-006).", account.Id);
        }

        return Results.Ok(new LoginResponse(token, "Bearer", JwtTokenService.TokenExpirySeconds));
    }
}
