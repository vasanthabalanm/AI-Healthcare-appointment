using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClinicalHealthcare.Api.Features.Coding;

/// <summary>
/// Vertical-slice endpoint: POST /patients/{id}/code-suggestions/accept-all
///
/// Bulk-accepts all Pending <see cref="MedicalCodeSuggestion"/> rows for a patient.
/// Requires <c>verifiedById</c> in the request body → 422 if missing (AC-003 parity).
/// Writes a single <see cref="AuditLog"/> entry on success (AC-006).
///
/// Security: Requires StaffOrAdmin role.
/// </summary>
public sealed class AcceptAllCodeSuggestionsEndpoint : IEndpointDefinition
{
    public sealed record AcceptAllRequest(int? VerifiedById);

    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/patients/{id:int}/code-suggestions/accept-all", HandleAcceptAll)
           .RequireAuthorization("StaffOrAdmin")
           .WithName("AcceptAllCodeSuggestions")
           .WithTags("Coding")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status401Unauthorized)
           .Produces(StatusCodes.Status403Forbidden)
           .Produces(StatusCodes.Status422UnprocessableEntity);
    }

    // ── POST /patients/{id}/code-suggestions/accept-all ───────────────────────

    public static async Task<IResult> HandleAcceptAll(
        int                                              id,
        AcceptAllRequest                                 body,
        ClinicalDbContext                                pgDb,
        ApplicationDbContext                             sqlDb,
        ILogger<AcceptAllCodeSuggestionsEndpoint>        logger,
        CancellationToken                                ct)
    {
        if (body.VerifiedById is null)
            return Results.UnprocessableEntity(
                new { error = "verifiedById is required for code verification" });

        var pending = await pgDb.MedicalCodeSuggestions
            .Where(s => s.PatientId == id && s.Status == SuggestionStatus.Pending)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        foreach (var suggestion in pending)
        {
            suggestion.Status       = SuggestionStatus.Accepted;
            suggestion.VerifiedById = body.VerifiedById;
            suggestion.VerifiedAt   = now;
        }

        await pgDb.SaveChangesAsync(ct);

        // AC-006: single AuditLog entry — best-effort; pgDb commit is already durable.
        sqlDb.Set<AuditLog>().Add(new AuditLog
        {
            EntityType = nameof(MedicalCodeSuggestion),
            EntityId   = id,
            ActorId    = body.VerifiedById,
            Action     = "ACCEPT_ALL",
            AfterValue = $"{{\"acceptedCount\":{pending.Count}}}",
            OccurredAt = now
        });
        try
        {
            await sqlDb.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "AuditLog write failed for ACCEPT_ALL on patient {PatientId} ({AcceptedCount} suggestions). "
                + "Suggestions were committed to pgDb. Manual reconciliation required.",
                id, pending.Count);
        }

        return Results.Ok(new
        {
            message       = $"Accepted {pending.Count} pending code suggestion(s).",
            acceptedCount = pending.Count
        });
    }
}
