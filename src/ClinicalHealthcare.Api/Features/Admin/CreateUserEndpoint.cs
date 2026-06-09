using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Email;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClinicalHealthcare.Api.Features.Admin;

/// <summary>
/// Vertical-slice endpoint: POST /admin/users
///
/// Creates a new Admin or Staff user account, stages an AuditLog INSERT entry,
/// and sends a credential-setup email so the new user can set their password.
/// Requires the caller to be authenticated with the "admin" role.
/// </summary>
public sealed class CreateUserEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // IPasswordHasher — guard prevents double-registration when both Admin
        // endpoints and RegisterEndpoint are auto-discovered together.
        if (!services.Any(d => d.ServiceType == typeof(IPasswordHasher<string>)))
            services.AddSingleton<IPasswordHasher<string>, PasswordHasher<string>>();

        if (!services.Any(d => d.ServiceType == typeof(IEmailService)))
            services.AddSingleton<IEmailService, MailKitEmailService>();

        services.AddAuthorization(options =>
        {
            // Idempotent — policy is skipped if already registered by another slice.
            if (options.GetPolicy("AdminOnly") is null)
                options.AddPolicy("AdminOnly", p => p.RequireRole("admin"));
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/admin/users", HandleCreateUser)
           .RequireAuthorization("AdminOnly")
           .WithName("AdminCreateUser")
           .WithTags("Admin")
           .Produces(StatusCodes.Status201Created)
           .Produces(StatusCodes.Status409Conflict)
           .Produces(StatusCodes.Status422UnprocessableEntity);
    }

    // ── POST /admin/users ───────────────────────────────────────────────────

    public static async Task<IResult> HandleCreateUser(
        CreateUserRequest request,
        ApplicationDbContext db,
        IPasswordHasher<string> hasher,
        IEmailService emailService,
        HttpContext httpContext,
        IConfiguration configuration,
        CancellationToken ct)
    {
        // Manual validation (Minimal API does not auto-validate DataAnnotations).
        var errors = ValidateRequest(request);
        if (errors.Count > 0)
            return Results.UnprocessableEntity(new { errors });

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        // AC-001 — duplicate email check (includes soft-deleted accounts).
        var duplicate = await db.UserAccounts
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Email == normalizedEmail, ct);

        if (duplicate)
            return Results.Conflict(new { error = "An account with this email address already exists." });

        // AC-001 — generate a random temporary password; never exposed.
        var tempRaw      = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var passwordHash = hasher.HashPassword(normalizedEmail, tempRaw);

        // AC-005 — one-time credential setup token (48-hour expiry).
        var setupTokenRaw   = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var setupTokenHash  = ComputeSha256Hash(setupTokenRaw);
        var setupTokenExpiry = DateTime.UtcNow.AddHours(48);

        var account = new UserAccount
        {
            Email                   = normalizedEmail,
            PasswordHash            = passwordHash,
            Role                    = request.Role.Trim().ToLowerInvariant(),
            IsActive                = true,   // admin-created accounts are active immediately
            FirstName               = request.FirstName.Trim(),
            LastName                = request.LastName.Trim(),
            VerificationTokenHash   = setupTokenHash,
            VerificationTokenExpiry = setupTokenExpiry
        };

        db.UserAccounts.Add(account);

        // AC-004 — stage AuditLog INSERT entry (committed atomically with the account).
        var actorId = ExtractActorId(httpContext);

        // Two saves needed to get account.Id for the AuditLog EntityId.
        // Wrapped in an explicit transaction so both rows are committed atomically.
        // If the second save fails the entire transaction is rolled back.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        await db.SaveChangesAsync(ct);    // persists account, populates account.Id

        AuditLogHelper.Stage(
            db,
            entityType:    "UserAccount",
            entityId:      account.Id,
            actorId:       actorId,
            action:        "INSERT",
            before:        null,
            after:         Snapshot(account),
            correlationId: httpContext.TraceIdentifier);

        await db.SaveChangesAsync(ct);    // persists AuditLog
        await tx.CommitAsync(ct);         // atomically commits both rows

        // AC-005 — send credential setup email.
        var baseUrl   = configuration["App:BaseUrl"] ?? "https://localhost:7001";
        var setupUrl  = $"{baseUrl}/auth/setup-credentials?token={Uri.EscapeDataString(setupTokenRaw)}";
        var htmlBody  = $"""
            <p>Hi {System.Net.WebUtility.HtmlEncode(account.FirstName)},</p>
            <p>A ClinicalHub account has been created for you with the role <strong>{System.Net.WebUtility.HtmlEncode(account.Role)}</strong>.</p>
            <p>Please set up your credentials by clicking the link below. This link expires in 48 hours.</p>
            <p><a href="{setupUrl}">Set up my credentials</a></p>
            <p>If you did not expect this email, please contact your administrator.</p>
            """;

        await emailService.SendAsync(
            normalizedEmail,
            "Set up your ClinicalHub credentials",
            htmlBody,
            ct);

        return Results.Created(
            $"/admin/users/{account.Id}",
            new { message = "User account created successfully.", userId = account.Id });
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    internal static int? ExtractActorId(HttpContext httpContext)
    {
        var raw = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? httpContext.User.FindFirstValue("sub");
        return int.TryParse(raw, out var id) ? id : null;
    }

    private static string ComputeSha256Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    internal static object Snapshot(UserAccount u) => new
    {
        u.Email, u.FirstName, u.LastName, u.Role, u.IsActive
    };

    private static bool IsValidEmail(string email)
    {
        try { _ = new System.Net.Mail.MailAddress(email); return true; }
        catch { return false; }
    }

    private static Dictionary<string, string[]> ValidateRequest(CreateUserRequest r)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(r.Email))
            errors["email"] = ["Email is required."];
        else if (!IsValidEmail(r.Email))
            errors["email"] = ["Email is not a valid email address."];

        if (string.IsNullOrWhiteSpace(r.FirstName))
            errors["firstName"] = ["First name is required."];

        if (string.IsNullOrWhiteSpace(r.LastName))
            errors["lastName"] = ["Last name is required."];

        if (string.IsNullOrWhiteSpace(r.Role))
            errors["role"] = ["Role is required."];
        else if (r.Role.Trim().ToLowerInvariant() is not ("admin" or "staff"))
            errors["role"] = ["Role must be 'admin' or 'staff'."];

        return errors;
    }
}
