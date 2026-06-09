using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.IdentityModel.Tokens.Jwt;

namespace ClinicalHealthcare.Api.Features.Coding;

/// <summary>
/// Vertical-slice endpoint: POST /patients/{id}/coding-complete
///
/// Gates coding completion: 409 if any Pending <see cref="MedicalCodeSuggestion"/> remain (AC-005).
/// On success: sets patient <see cref="CodingStatus"/> = Complete and writes an
/// <see cref="AuditLog"/> entry (AC-006).
///
/// Security: Requires StaffOrAdmin role.
/// </summary>
public sealed class CodingCompleteEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/patients/{id:int}/coding-complete", HandleCodingComplete)
           .RequireAuthorization("StaffOrAdmin")
           .WithName("CodingComplete")
           .WithTags("Coding")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status401Unauthorized)
           .Produces(StatusCodes.Status403Forbidden)
           .Produces(StatusCodes.Status404NotFound)
           .Produces(StatusCodes.Status409Conflict);
    }

    // ── POST /patients/{id}/coding-complete ───────────────────────────────────

    public static async Task<IResult> HandleCodingComplete(
        int                  id,
        HttpContext          httpContext,
        ClinicalDbContext     pgDb,
        ApplicationDbContext  sqlDb,
        CancellationToken    ct)
    {
        // M-003: capture actor from JWT sub (nullable — system actions have no actor).
        var sub = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
               ?? httpContext.User.FindFirst("sub")?.Value;
        int? actorId = int.TryParse(sub, out var parsed) ? parsed : (int?)null;

        // AC-005: block if any Pending suggestions remain.
        var pendingCount = await pgDb.MedicalCodeSuggestions
            .CountAsync(s => s.PatientId == id && s.Status == SuggestionStatus.Pending, ct);

        if (pendingCount > 0)
            return Results.Conflict(new
            {
                error        = "All code suggestions must be reviewed before coding-complete",
                pendingCount = pendingCount
            });

        var patient = await sqlDb.UserAccounts
            .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted, ct);
        if (patient is null)
            return Results.NotFound(new { error = $"Patient {id} not found." });

        patient.CodingStatus = CodingStatus.Complete;

        var now = DateTime.UtcNow;

        sqlDb.Set<AuditLog>().Add(new AuditLog
        {
            EntityType = nameof(UserAccount),
            EntityId   = id,
            ActorId    = actorId,
            Action     = "CODING_COMPLETE",
            AfterValue = $"{{\"codingStatus\":\"Complete\"}}",
            OccurredAt = now
        });

        await sqlDb.SaveChangesAsync(ct);

        return Results.Ok(new { message = $"Coding marked complete for patient {id}." });
    }
}
