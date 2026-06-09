using System.Security.Claims;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Helpers;
using Microsoft.Extensions.Logging;

namespace ClinicalHealthcare.Api.Features.Admin;

/// <summary>
/// Vertical-slice endpoint: explicit 405 guards for <c>DELETE /audit</c> and <c>PATCH /audit</c>.
///
/// AuditLog rows are INSERT-only at the DB level (REVOKE from us_011 migration). These
/// endpoint handlers exist to return a standards-compliant 405 Method Not Allowed and to
/// produce an AuditLog entry so every tampering attempt is itself audit-logged (AC-003).
/// Admin role is required so that unauthenticated probes receive 401 instead of 405 (AC-005).
/// </summary>
public sealed class AuditLogGuardEndpoints : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // Authorization is centralised in Program.cs; idempotent guard prevents duplicate
        // policy registration when multiple slices call AddAuthorization.
        services.AddAuthorization(options =>
        {
            if (options.GetPolicy("AdminOnly") is null)
                options.AddPolicy("AdminOnly", p => p.RequireRole("admin"));
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        // Explicit route handlers are required because ASP.NET Core Minimal APIs do not
        // automatically return 405 for unregistered verbs on a known path.
        app.MapDelete("/audit", HandleDeleteAudit)
           .RequireAuthorization("AdminOnly")
           .WithName("AuditDeleteGuard")
           .WithTags("Audit")
           .Produces(StatusCodes.Status405MethodNotAllowed)
           .Produces(StatusCodes.Status401Unauthorized)
           .Produces(StatusCodes.Status403Forbidden);

        app.MapMethods("/audit", ["PATCH"], HandlePatchAudit)
           .RequireAuthorization("AdminOnly")
           .WithName("AuditPatchGuard")
           .WithTags("Audit")
           .Produces(StatusCodes.Status405MethodNotAllowed)
           .Produces(StatusCodes.Status401Unauthorized)
           .Produces(StatusCodes.Status403Forbidden);
    }

    // ── DELETE /audit ─────────────────────────────────────────────────────────

    public static async Task<IResult> HandleDeleteAudit(
        ApplicationDbContext db,
        HttpContext httpContext,
        [Microsoft.AspNetCore.Mvc.FromServices] ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        await LogAttemptAsync(db, httpContext, "DELETE", loggerFactory, ct);
        return MethodNotAllowedResult("DELETE");
    }

    // ── PATCH /audit ──────────────────────────────────────────────────────────

    public static async Task<IResult> HandlePatchAudit(
        ApplicationDbContext db,
        HttpContext httpContext,
        [Microsoft.AspNetCore.Mvc.FromServices] ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        await LogAttemptAsync(db, httpContext, "PATCH", loggerFactory, ct);
        return MethodNotAllowedResult("PATCH");
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes an AuditLog INSERT recording the forbidden mutation attempt.
    /// EntityId = 0 (no specific row targeted), ActorId from JWT sub claim.
    /// The save is wrapped in try/catch so DB failures never prevent the 405
    /// response from being returned to the caller (F1 fix).
    /// </summary>
    private static async Task LogAttemptAsync(
        ApplicationDbContext db,
        HttpContext httpContext,
        string method,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var actorIdStr = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        int? actorId   = int.TryParse(actorIdStr, out var parsed) ? parsed : null;

        var correlationId = httpContext.Request.Headers["X-Correlation-ID"].FirstOrDefault();

        var logger = loggerFactory.CreateLogger<AuditLogGuardEndpoints>();

        try
        {
            AuditLogHelper.Stage(
                db,
                entityType:    "AuditLog",
                entityId:      0,
                actorId:       actorId,
                action:        "HTTP-405-Attempt",
                before:        null,
                after:         new { method = $"{method} /audit" },
                correlationId: correlationId);

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // A DB failure must not change the response from 405 to 500.
            // Log the failure so operators can investigate, then continue.
            logger.LogWarning(
                ex,
                "AuditLogGuardEndpoints: failed to persist 405-attempt audit log for {Method} /audit.",
                method);
        }
    }

    private static IResult MethodNotAllowedResult(string method)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status405MethodNotAllowed,
            title:      "Method Not Allowed",
            detail:     $"The {method} method is not permitted on the /audit resource. Audit logs are immutable.",
            type:       "https://tools.ietf.org/html/rfc9110#section-15.5.6");
    }
}
