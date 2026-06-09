using System.Security.Cryptography;
using System.Text;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Email;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging;

namespace ClinicalHealthcare.Api.Features.Auth;

/// <summary>
/// Vertical-slice definition for patient self-registration.
///
/// Endpoints:
///   POST /auth/register       — AC-001 to AC-005
///   GET  /auth/verify-email   — activates account via token
/// </summary>
public sealed class RegisterEndpoint : IEndpointDefinition
{
    /// <summary>Rate-limit policy name applied to POST /auth/register (AC-004).</summary>
    public const string RegistrationRateLimitPolicy = "registration";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // PasswordHasher is stateless — safe as singleton for the string key type.
        services.AddSingleton<IPasswordHasher<string>, PasswordHasher<string>>();

        // IEmailService — only register if not already registered (avoids duplicate
        // registration if tests override it with a fake before AddServices runs).
        if (!services.Any(d => d.ServiceType == typeof(IEmailService)))
        {
            services.AddSingleton<IEmailService, MailKitEmailService>();
        }

        // Rate limiting — per-IP fixed window, 10 requests per IP per hour (AC-004).
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(RegistrationRateLimitPolicy, context =>
                System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                    {
                        Window      = TimeSpan.FromHours(1),
                        PermitLimit = 10,
                        QueueLimit  = 0
                    }));

            // Return 429 with a JSON body when the rate limit is exceeded.
            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.StatusCode  = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsync(
                    "{\"error\":\"Too many registration attempts. Try again later.\"}", ct);
            };
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/register", HandleRegister)
           .AllowAnonymous()
           .RequireRateLimiting(RegistrationRateLimitPolicy)
           .WithName("RegisterPatient")
           .WithTags("Auth")
           .Produces(StatusCodes.Status201Created)
           .Produces(StatusCodes.Status409Conflict)
           .Produces(StatusCodes.Status422UnprocessableEntity)
           .Produces(StatusCodes.Status429TooManyRequests);

        app.MapGet("/auth/verify-email", HandleVerifyEmail)
           .AllowAnonymous()
           .WithName("VerifyEmail")
           .WithTags("Auth")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status400BadRequest);
    }

    // ── POST /auth/register ─────────────────────────────────────────────────

    public static async Task<IResult> HandleRegister(
        RegisterRequest request,
        ApplicationDbContext db,
        IPasswordHasher<string> hasher,
        IEmailService emailService,
        HttpContext httpContext,
        IConfiguration configuration,
        CancellationToken cancellationToken,
        ILoggerFactory? loggerFactory = null)
    {
        // Manual model validation (Minimal API does not auto-validate DataAnnotations).
        var validationErrors = ValidateRequest(request);
        if (validationErrors.Count > 0)
            return Results.UnprocessableEntity(new { errors = validationErrors });

        // AC-003 — duplicate email check (case-insensitive; unique index as safety net).
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var existing = await db.UserAccounts
            .IgnoreQueryFilters()  // include soft-deleted accounts — email is permanently taken
            .AnyAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (existing)
            return Results.Conflict(new { error = "An account with this email address already exists." });

        // AC-005 — hash password; create account with Role=Patient, IsActive=false.
        var passwordHash = hasher.HashPassword(normalizedEmail, request.Password);

        // AC-002 — generate cryptographically random 32-byte verification token.
        var rawToken   = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var tokenHash  = ComputeSha256Hash(rawToken);
        var tokenExpiry = DateTime.UtcNow.AddHours(24);

        var account = new UserAccount
        {
            Email                   = normalizedEmail,
            PasswordHash            = passwordHash,
            Role                    = "patient",
            IsActive                = false,  // AC-005: inactive until email verified
            FirstName               = request.FirstName.Trim(),
            LastName                = request.LastName.Trim(),
            VerificationTokenHash   = tokenHash,
            VerificationTokenExpiry = tokenExpiry
        };

        db.UserAccounts.Add(account);
        // SaveChangesAsync is deferred until after email delivery: if SMTP fails the entity
        // is still in the Added state and is never written to the database, so no PHI row
        // is created and the email address remains available for a retry.

        // AC-002 — send verification email via MailKit.
        var baseUrl = configuration["App:BaseUrl"] ?? "https://localhost:7001";
        var verifyUrl = $"{baseUrl}/auth/verify-email?token={Uri.EscapeDataString(rawToken)}";

        var htmlBody = $"""
            <p>Welcome to ClinicalHub, {account.FirstName}!</p>
            <p>Please verify your email address by clicking the link below. This link expires in 24 hours.</p>
            <p><a href="{verifyUrl}">Verify my email address</a></p>
            <p>If you did not register, ignore this email.</p>
            """;

        try
        {
            await emailService.SendAsync(
                normalizedEmail,
                "Verify your ClinicalHub account",
                htmlBody,
                cancellationToken);
        }
        catch (Exception ex)
        {
            loggerFactory?.CreateLogger<RegisterEndpoint>()
                .LogError(ex, "Registration email failed for {Email}. SMTP connection or auth error.", normalizedEmail);
            // Email failed — detach the unsaved entity; no row was ever written so the
            // user can retry with the same address without hitting a 409 conflict.
            db.Entry(account).State = EntityState.Detached;
            return Results.Json(
                new { error = "Email service is temporarily unavailable. Please try again later." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        // Email delivered — now persist the account.
        await db.SaveChangesAsync(cancellationToken);

        // AC-001 — 201 on success.
        return Results.Created(
            $"/auth/register",
            new { message = "Registration successful. Check your email to verify your account." });
    }

    // ── GET /auth/verify-email?token=... ────────────────────────────────────

    public static async Task<IResult> HandleVerifyEmail(
        string token,
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            return Results.BadRequest(new { error = "Verification token is required." });

        var tokenHash = ComputeSha256Hash(token);

        // IgnoreQueryFilters: account is IsActive=false and IsDeleted=false — still within filter,
        // but using IgnoreQueryFilters defensively so the query is filter-independent.
        var account = await db.UserAccounts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.VerificationTokenHash == tokenHash, cancellationToken);

        if (account is null)
            return Results.BadRequest(new { error = "Invalid or already-used verification token." });

        if (account.VerificationTokenExpiry < DateTime.UtcNow)
            return Results.BadRequest(new { error = "Verification token has expired. Please register again." });

        account.IsActive                = true;
        account.VerificationTokenHash   = null;   // consume token — one-time use
        account.VerificationTokenExpiry = null;
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new { message = "Email verified successfully. You can now log in." });
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string ComputeSha256Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static Dictionary<string, string[]> ValidateRequest(RegisterRequest r)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(r.Email))
            errors["email"] = ["Email is required."];
        else if (!r.Email.Contains('@'))
            errors["email"] = ["Email is not a valid email address."];

        if (string.IsNullOrWhiteSpace(r.Password) || r.Password.Length < 8)
            errors["password"] = ["Password must be at least 8 characters."];

        if (string.IsNullOrWhiteSpace(r.FirstName))
            errors["firstName"] = ["First name is required."];

        if (string.IsNullOrWhiteSpace(r.LastName))
            errors["lastName"] = ["Last name is required."];

        return errors;
    }
}
