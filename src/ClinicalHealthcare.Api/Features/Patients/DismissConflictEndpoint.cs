using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicalHealthcare.Api.Features.Patients;

/// <summary>
/// Vertical-slice endpoint: PATCH /conflicts/{id}/dismiss
///
/// Marks an Unresolved <see cref="ConflictFlag"/> as Dismissed (AC-003).
/// Only Unresolved flags may be dismissed; already-resolved or already-dismissed
/// flags return 409.
///
/// Security: Requires StaffOrAdmin role (OWASP A01).
/// </summary>
public sealed class DismissConflictEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPatch("/conflicts/{id:int}/dismiss", HandleDismissConflict)
           .RequireAuthorization("StaffOrAdmin")
           .WithName("DismissConflict")
           .WithTags("Patients")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status401Unauthorized)
           .Produces(StatusCodes.Status403Forbidden)
           .Produces(StatusCodes.Status404NotFound)
           .Produces(StatusCodes.Status409Conflict);
    }

    // ── PATCH /conflicts/{id}/dismiss ─────────────────────────────────────────

    public static async Task<IResult> HandleDismissConflict(
        int               id,
        ClinicalDbContext  pgDb,
        CancellationToken ct)
    {
        var flag = await pgDb.ConflictFlags.FindAsync([id], ct);
        if (flag is null)
            return Results.NotFound(new { error = "Conflict flag not found." });

        if (flag.Status != ConflictFlagStatus.Unresolved)
            return Results.Conflict(new { error = "Only Unresolved conflict flags can be dismissed." });

        flag.Status = ConflictFlagStatus.Dismissed;

        try
        {
            await pgDb.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { error = "Conflict flag was modified concurrently. Please retry." });
        }

        return Results.Ok(new { message = "Conflict dismissed." });
    }
}
