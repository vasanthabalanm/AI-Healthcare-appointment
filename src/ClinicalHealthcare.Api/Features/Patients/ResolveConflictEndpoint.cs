using System.IdentityModel.Tokens.Jwt;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicalHealthcare.Api.Features.Patients;

/// <summary>
/// Vertical-slice endpoint: PATCH /conflicts/{id}/resolve
///
/// Marks an Unresolved <see cref="ConflictFlag"/> as Resolved and records
/// the staff member who performed the resolution (AC-002).
///
/// Security:
///   1. Requires StaffOrAdmin role.
///   2. Staff ID sourced from JWT sub claim — never from request body (OWASP A01).
/// </summary>
public sealed class ResolveConflictEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPatch("/conflicts/{id:int}/resolve", HandleResolveConflict)
           .RequireAuthorization("StaffOrAdmin")
           .WithName("ResolveConflict")
           .WithTags("Patients")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status401Unauthorized)
           .Produces(StatusCodes.Status403Forbidden)
           .Produces(StatusCodes.Status404NotFound)
           .Produces(StatusCodes.Status409Conflict);
    }

    // ── PATCH /conflicts/{id}/resolve ─────────────────────────────────────────

    public static async Task<IResult> HandleResolveConflict(
        int               id,
        HttpContext        httpContext,
        ClinicalDbContext  pgDb,
        CancellationToken ct)
    {
        // Staff ID from JWT — never from request body (OWASP A01).
        var sub = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
               ?? httpContext.User.FindFirst("sub")?.Value;
        if (!int.TryParse(sub, out var staffId))
            return Results.Unauthorized();

        var flag = await pgDb.ConflictFlags.FindAsync([id], ct);
        if (flag is null)
            return Results.NotFound(new { error = "Conflict flag not found." });

        if (flag.Status is ConflictFlagStatus.Resolved or ConflictFlagStatus.Dismissed)
            return Results.Conflict(new { error = "Conflict flag is already in a terminal state." });

        flag.Status             = ConflictFlagStatus.Resolved;
        flag.ResolvedByStaffId  = staffId;

        try
        {
            await pgDb.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { error = "Conflict flag was modified concurrently. Please retry." });
        }

        return Results.Ok(new { message = "Conflict resolved." });
    }
}
