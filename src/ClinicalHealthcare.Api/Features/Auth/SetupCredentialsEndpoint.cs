using System.Security.Cryptography;
using System.Text;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClinicalHealthcare.Api.Features.Auth;

/// <summary>
/// Vertical-slice endpoint: POST /auth/setup-credentials
///
/// Consumed by Staff and Admin users who click the credential-setup link in their
/// welcome email (issued by <see cref="Admin.CreateUserEndpoint"/> — TASK_013 AC-005).
///
/// Validates the one-time setup token stored in <c>UserAccount.VerificationTokenHash</c>,
/// hashes and persists the new password, invalidates the token, and writes an audit entry.
/// </summary>
public sealed class SetupCredentialsEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // IPasswordHasher registered by RegisterEndpoint; guard prevents double-registration.
        if (!services.Any(d => d.ServiceType == typeof(IPasswordHasher<string>)))
            services.AddSingleton<IPasswordHasher<string>, PasswordHasher<string>>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/setup-credentials", HandleSetupCredentials)
           .AllowAnonymous()
           .WithName("SetupCredentials")
           .WithTags("Auth")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status422UnprocessableEntity);
    }

    // ── POST /auth/setup-credentials ─────────────────────────────────────────

    public sealed record SetupCredentialsRequest(string Token, string NewPassword);

    public static async Task<IResult> HandleSetupCredentials(
        SetupCredentialsRequest   request,
        ApplicationDbContext      db,
        IPasswordHasher<string>   hasher,
        CancellationToken         ct)
    {
        // AC-004 — input validation before any DB query.
        if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.NewPassword))
            return Results.UnprocessableEntity(new { error = "Token and newPassword are required." });

        if (request.NewPassword.Length < 8)
            return Results.UnprocessableEntity(new { error = "Password must be at least 8 characters." });

        // Uniform 400 for all invalid/expired/consumed token states — no enumeration.
        static IResult InvalidLink() =>
            Results.BadRequest(new { error = "Invalid or expired setup link." });

        // Look up the account by the SHA-256 hash of the raw token.
        // CreateUserEndpoint stores ComputeSha256Hash(rawToken) in VerificationTokenHash.
        var tokenHash = ComputeSha256Hash(request.Token);
        var account = await db.UserAccounts
            .IgnoreQueryFilters()  // account may be IsActive=false at setup time — still valid
            .FirstOrDefaultAsync(u => u.VerificationTokenHash == tokenHash, ct);

        // AC-002 / AC-003 / Edge: token not found in DB (includes already-consumed tokens,
        // which are unfindable because VerificationTokenHash was cleared on first use).
        if (account is null)
            return InvalidLink();

        if (account.VerificationTokenExpiry is null
            || account.VerificationTokenExpiry.Value < DateTime.UtcNow)
        {
            return InvalidLink();
        }

        // AC-001 — hash new password and persist.
        account.PasswordHash = hasher.HashPassword(account.Email, request.NewPassword);

        // AC-002 — single-use: clear the token fields so replay returns 404 on next lookup.
        account.VerificationTokenHash   = null;
        account.VerificationTokenExpiry = null;

        // AC-005 — audit entry. ActorId = the account being set up (no caller JWT available).
        var after = new { account.Email, account.Role, account.IsActive };
        AuditLogHelper.Stage(
            db,
            entityType: "UserAccount",
            entityId:   account.Id,
            actorId:    account.Id,
            action:     "CREDENTIALS-SET",
            before:     null,
            after:      after);

        await db.SaveChangesAsync(ct);

        return Results.Ok(new { message = "Credentials set successfully. You can now log in." });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public static string ComputeSha256Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
