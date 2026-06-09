using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Auth;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace ClinicalHealthcare.Api.Features.Auth;

/// <summary>
/// Vertical-slice endpoint: POST /auth/reset-password
///
/// Validates a single-use password-reset token (AC-002/AC-005) and:
///   1. Re-hashes the new password using PBKDF2 SHA-256 (100k iterations, AC-003).
///   2. Marks the reset token as used (AC-005).
///   3. Revokes all active Redis sessions for the user by deleting every entry
///      in the <c>user-sessions:{userId}</c> Redis set (AC-004).
///   4. Writes an AuditLog entry for the password change.
/// </summary>
public sealed class ResetPasswordEndpoint : IEndpointDefinition
{
    /// <summary>Redis key prefix for the per-user active session set (AC-004).</summary>
    public const string UserSessionsKeyPrefix = "user-sessions:";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // IPasswordHasher registered by RegisterEndpoint; guard prevents double-registration.
        if (!services.Any(d => d.ServiceType == typeof(IPasswordHasher<string>)))
            services.AddSingleton<IPasswordHasher<string>, PasswordHasher<string>>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/reset-password", HandleResetPassword)
           .AllowAnonymous()
           .WithName("ResetPassword")
           .WithTags("Auth")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status422UnprocessableEntity);
    }

    // ── POST /auth/reset-password ─────────────────────────────────────────────

    public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

    public static async Task<IResult> HandleResetPassword(
        ResetPasswordRequest              request,
        ApplicationDbContext              db,
        IPasswordHasher<string>           hasher,
        [FromServices] IConnectionMultiplexer? multiplexer,
        [FromServices] ILoggerFactory         loggerFactory,
        CancellationToken                 ct)
    {
        // 1. Basic input validation
        if (string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrWhiteSpace(request.Token)
            || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return Results.UnprocessableEntity(new { error = "Email, token, and newPassword are required." });
        }

        // F2: password complexity — must match the 8-char rule enforced by RegisterEndpoint.
        if (request.NewPassword.Length < 8)
            return Results.UnprocessableEntity(new { error = "Password must be at least 8 characters." });

        var logger = loggerFactory.CreateLogger<ResetPasswordEndpoint>();

        // 2. Load account
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var account = await db.UserAccounts
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail && u.IsActive, ct);

        if (account is null)
            return Results.BadRequest(new { error = "Reset token is invalid or has expired." });

        // 3. Validate token: not used, not expired, hash matches (AC-002 / AC-005)
        var invalidTokenResult = Results.BadRequest(new { error = "Reset token is invalid or has expired." });

        if (account.PasswordResetTokenUsed
            || account.PasswordResetTokenHash is null
            || account.PasswordResetTokenExpiry is null
            || account.PasswordResetTokenExpiry.Value < DateTime.UtcNow)
        {
            return invalidTokenResult;
        }

        var computedHash = ForgotPasswordEndpoint.ComputePbkdf2Hash(request.Token);
        if (!string.Equals(computedHash, account.PasswordResetTokenHash, StringComparison.OrdinalIgnoreCase))
            return invalidTokenResult;

        // 4. Re-hash new password with PBKDF2 SHA-256 (100k iterations via ASP.NET Core Identity V3, AC-003)
        account.PasswordHash           = hasher.HashPassword(account.Email, request.NewPassword);

        // 5. Invalidate reset token immediately — single-use (AC-005)
        account.PasswordResetTokenUsed   = true;
        account.PasswordResetTokenExpiry = null;   // explicit null to prevent reuse after reset

        // 6. Reset login-failure counters (a successful reset is a valid identity proof)
        account.FailedLoginAttempts = 0;
        account.LockoutEnd          = null;

        // 7. Audit log — password change (INSERT-only pattern)
        AuditLogHelper.Stage(
            db,
            entityType:    "UserAccount",
            entityId:      account.Id,
            actorId:       account.Id,
            action:        "PASSWORD-RESET",
            before:        null,
            after:         new { reason = "User-initiated password reset via email token" });

        await db.SaveChangesAsync(ct);

        // 8. Revoke all active Redis sessions for this user (AC-004)
        if (multiplexer is not null)
            await RevokeAllSessionsAsync(multiplexer, account.Id, logger, ct);

        return Results.Ok(new { message = "Password reset successfully. Please sign in with your new password." });
    }

    // ── Session revocation ────────────────────────────────────────────────────

    /// <summary>
    /// Deletes all JTI allowlist keys in the <c>user-sessions:{userId}</c> Redis set
    /// then deletes the set itself, effectively invalidating every active session (AC-004).
    /// </summary>
    private static async Task RevokeAllSessionsAsync(
        IConnectionMultiplexer multiplexer,
        int userId,
        Microsoft.Extensions.Logging.ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var redisDb      = multiplexer.GetDatabase();
            var userSetKey   = $"{UserSessionsKeyPrefix}{userId}";

            // Retrieve all JTIs registered for this user.
            var jtis = await redisDb.SetMembersAsync(userSetKey).ConfigureAwait(false);

            if (jtis.Length > 0)
            {
                // Build an array of all jti:* keys + the set key itself.
                var keysToDelete = jtis
                    .Select(jti => (RedisKey)$"{LoginEndpoint.SessionKeyPrefix}{jti}")
                    .Append((RedisKey)userSetKey)
                    .ToArray();

                await redisDb.KeyDeleteAsync(keysToDelete).ConfigureAwait(false);

                logger.LogInformation(
                    "ResetPasswordEndpoint: revoked {Count} Redis session(s) for user {UserId}.",
                    jtis.Length, userId);
            }
        }
        catch (RedisException ex)
        {
            // AC-006: Redis unavailable — log the failure; JWTs will expire naturally.
            logger.LogWarning(
                ex,
                "ResetPasswordEndpoint: Redis unavailable — could not revoke sessions for user {UserId}. " +
                "Existing JWTs will expire when their TTL lapses (AC-006).", userId);
        }
    }
}
