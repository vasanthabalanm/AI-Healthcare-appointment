using System.Security.Cryptography;
using System.Text;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;

namespace ClinicalHealthcare.Api.Features.Auth;

/// <summary>
/// Vertical-slice endpoint: POST /auth/forgot-password
///
/// Initiates the password reset flow. Always returns 200 OK regardless of
/// whether the email exists — this prevents account enumeration (AC-001).
///
/// When the email is found:
///   1. Generates a cryptographically random 32-byte token.
///   2. Stores the PBKDF2 SHA-256 hash of the token in <c>UserAccount</c>
///      with a 60-minute expiry (AC-002).
///   3. Sends a reset-password link via <see cref="IEmailService"/>.
/// </summary>
public sealed class ForgotPasswordEndpoint : IEndpointDefinition
{
    /// <summary>Token validity window in minutes (AC-002).</summary>
    public const int TokenExpiryMinutes = 60;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // IEmailService and IPasswordHasher registered by RegisterEndpoint; guard prevents double-registration.
        if (!services.Any(d => d.ServiceType == typeof(IEmailService)))
            services.AddSingleton<IEmailService, MailKitEmailService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/forgot-password", HandleForgotPassword)
           .AllowAnonymous()
           .WithName("ForgotPassword")
           .WithTags("Auth")
           .Produces(StatusCodes.Status200OK);
    }

    // ── POST /auth/forgot-password ────────────────────────────────────────────

    public sealed record ForgotPasswordRequest(string Email);

    public static async Task<IResult> HandleForgotPassword(
        ForgotPasswordRequest         request,
        ApplicationDbContext          db,
        [Microsoft.AspNetCore.Mvc.FromServices] IEmailService emailService,
        IConfiguration                configuration,
        [Microsoft.AspNetCore.Mvc.FromServices] ILoggerFactory loggerFactory,
        CancellationToken             ct)
    {
        // AC-001: always 200 — do not short-circuit with an error for unknown emails.
        if (string.IsNullOrWhiteSpace(request.Email))
            return Results.Ok(new { message = "If that email is registered you will receive a reset link." });

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var account = await db.UserAccounts
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail && u.IsActive, ct);

        if (account is null)
        {
            // AC-001: no email sent, no log, just return 200 silently.
            return Results.Ok(new { message = "If that email is registered you will receive a reset link." });
        }

        // F3: per-email cooldown — reject a new token request if one was issued in the last 5 minutes.
        // Always return 200 (no enumeration).
        if (account.PasswordResetTokenIssuedAt.HasValue
            && account.PasswordResetTokenIssuedAt.Value > DateTime.UtcNow.AddMinutes(-5))
        {
            return Results.Ok(new { message = "If that email is registered you will receive a reset link." });
        }

        // Generate token: 32 random bytes → base64url raw token for the link,
        // PBKDF2 SHA-256 hash stored in the database (AC-002).
        var rawTokenBytes = RandomNumberGenerator.GetBytes(32);
        var rawToken      = Convert.ToBase64String(rawTokenBytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');  // base64url safe

        account.PasswordResetTokenHash      = ComputePbkdf2Hash(rawToken);
        account.PasswordResetTokenExpiry    = DateTime.UtcNow.AddMinutes(TokenExpiryMinutes);
        account.PasswordResetTokenUsed      = false;
        account.PasswordResetTokenIssuedAt  = DateTime.UtcNow;  // F3: cooldown timestamp

        await db.SaveChangesAsync(ct);

        // Build reset link using the frontend client URL — the reset-password page is an Angular
        // route served at ClientBaseUrl, NOT the API base URL (App:BaseUrl).
        var clientBaseUrl = configuration["App:ClientBaseUrl"] ?? configuration["App:BaseUrl"] ?? "https://localhost";
        var resetLink     = $"{clientBaseUrl}/reset-password?email={Uri.EscapeDataString(account.Email)}&token={Uri.EscapeDataString(rawToken)}";
        var htmlBody   = BuildEmailHtml(account.FirstName, resetLink, TokenExpiryMinutes);

        // P3: wrap email send so a transient SMTP failure returns 200 rather than 500.
        try
        {
            await emailService.SendAsync(
                account.Email,
                "Reset your ClinicalHub password",
                htmlBody,
                ct);
        }
        catch (Exception ex)
        {
            // Token is already persisted — user can retry the flow. Log but do not expose error.
            loggerFactory.CreateLogger<ForgotPasswordEndpoint>()
                .LogWarning(ex, "ForgotPasswordEndpoint: failed to send reset email to {Email}.", account.Email);
        }

        return Results.Ok(new { message = "If that email is registered you will receive a reset link." });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes a PBKDF2 SHA-256 hash of <paramref name="rawToken"/>.
    /// Stored as hex so it is safe to persist as a VARCHAR column.
    /// </summary>
    public static string ComputePbkdf2Hash(string rawToken)
    {
        // Salt is a fixed application-level salt derived from the token itself —
        // the token is already a high-entropy random value; a separate per-row
        // random salt would require an extra column. The salt here prevents a
        // generic rainbow-table attack against the stored hash.
        var tokenBytes = Encoding.UTF8.GetBytes(rawToken);
        var saltBytes  = SHA256.HashData(tokenBytes); // deterministic salt from token

        using var pbkdf2 = new Rfc2898DeriveBytes(
            tokenBytes,
            saltBytes,
            iterations: 100_000,
            HashAlgorithmName.SHA256);

        return Convert.ToHexString(pbkdf2.GetBytes(32));
    }

    private static string BuildEmailHtml(string firstName, string resetLink, int expiryMinutes) =>
        $"""
        <p>Hi {System.Net.WebUtility.HtmlEncode(firstName)},</p>
        <p>We received a request to reset your ClinicalHub password.</p>
        <p><a href="{resetLink}">Click here to reset your password</a></p>
        <p>This link expires in {expiryMinutes} minutes. If you did not request a reset, ignore this email.</p>
        """;
}
