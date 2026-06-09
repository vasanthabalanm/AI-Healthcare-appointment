using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicalHealthcare.Api.Features.Patients;

/// <summary>
/// Vertical-slice endpoint: GET /patients/{id}/conflicts
///
/// Returns all <see cref="ConflictFlag"/> rows for the given patient,
/// regardless of status (Unresolved, Resolved, Dismissed) — AC-001.
///
/// Security: Requires StaffOrAdmin role (OWASP A01).
/// </summary>
public sealed class GetConflictsEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/patients/{id:int}/conflicts", HandleGetConflicts)
           .RequireAuthorization("StaffOrAdmin")
           .WithName("GetPatientConflicts")
           .WithTags("Patients")
           .Produces<List<ConflictFlag>>(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status401Unauthorized)
           .Produces(StatusCodes.Status403Forbidden);
    }

    // ── GET /patients/{id}/conflicts ──────────────────────────────────────────

    public static async Task<IResult> HandleGetConflicts(
        int               id,
        ClinicalDbContext  pgDb,
        CancellationToken ct)
    {
        var flags = await pgDb.ConflictFlags
            .Where(c => c.PatientId == id)
            .OrderBy(c => c.Id)
            .ToListAsync(ct);

        return Results.Ok(flags);
    }
}
