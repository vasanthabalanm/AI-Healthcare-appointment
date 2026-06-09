using System.IdentityModel.Tokens.Jwt;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicalHealthcare.Api.Features.Patients;

/// <summary>
/// Vertical-slice endpoint: PATCH /patients/{id}/verify
///
/// Transitions a patient's clinical record to <see cref="VerificationStatus.Verified"/>
/// (Trust-First pattern — AIR-006, AC-003).
///
/// Guards:
///   1. Any Unresolved <see cref="ConflictFlag"/> → 409 with unresolved count (AC-004).
///   2. Already-Verified patient → 409.
///
/// On success: sets VerificationStatus=Verified, VerifiedById=JWT.sub, VerifiedAt=UtcNow;
/// invalidates 360° cache key.
///
/// Security:
///   1. Requires StaffOrAdmin role.
///   2. Staff ID sourced from JWT sub claim — never from request body (OWASP A01).
/// </summary>
public sealed class VerifyPatientEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPatch("/patients/{id:int}/verify", HandleVerifyPatient)
           .RequireAuthorization("StaffOrAdmin")
           .WithName("VerifyPatient")
           .WithTags("Patients")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status401Unauthorized)
           .Produces(StatusCodes.Status403Forbidden)
           .Produces(StatusCodes.Status404NotFound)
           .Produces(StatusCodes.Status409Conflict);
    }

    // ── PATCH /patients/{id}/verify ───────────────────────────────────────────

    public static async Task<IResult> HandleVerifyPatient(
        int                 id,
        HttpContext          httpContext,
        ApplicationDbContext sqlDb,
        IConflictService     conflictService,
        ICacheService        cache,
        CancellationToken    ct)
    {
        // Staff ID from JWT — never from request body (OWASP A01).
        var sub = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
               ?? httpContext.User.FindFirst("sub")?.Value;
        if (!int.TryParse(sub, out var staffId))
            return Results.Unauthorized();

        var patient = await sqlDb.UserAccounts
            .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted, ct);
        if (patient is null)
            return Results.NotFound(new { error = $"Patient {id} not found." });

        // AC-004: Block if any Unresolved ConflictFlags exist.
        var unresolvedCount = await conflictService.GetUnresolvedCountAsync(id, ct);
        if (unresolvedCount > 0)
            return Results.Conflict(new
            {
                error           = "Resolve all clinical conflicts first",
                unresolvedCount = unresolvedCount
            });

        // Guard: already verified.
        if (patient.VerificationStatus == VerificationStatus.Verified)
            return Results.Conflict(new { error = "Patient record is already verified." });

        // AC-003: Set verification fields.
        patient.VerificationStatus = VerificationStatus.Verified;
        patient.VerifiedById       = staffId;
        patient.VerifiedAt         = DateTime.UtcNow;

        await sqlDb.SaveChangesAsync(ct);

        // Invalidate 360° view cache so the next GET reflects the new status.
        await cache.DeleteAsync($"{Get360ViewEndpoint.CacheKeyPrefix}{id}", ct);

        return Results.Ok(new { message = "Patient record verified." });
    }
}
