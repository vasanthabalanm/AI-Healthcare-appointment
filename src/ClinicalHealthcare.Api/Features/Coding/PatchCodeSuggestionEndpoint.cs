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
/// Vertical-slice endpoint: PATCH /code-suggestions/{id}
///
/// Accepts or rejects a single <see cref="MedicalCodeSuggestion"/>.
///
/// Guards:
///   1. <c>verifiedById</c> required for Accepted status → 422 (AC-003).
///   2. Suggestion not found → 404.
///   3. Already Accepted → 409 (AC-002 edge case).
///
/// If <c>committedCode</c> differs from <c>suggestedCode</c> → Status = Modified.
/// Writes an <see cref="AuditLog"/> entry on success (AC-006).
///
/// Security: Requires StaffOrAdmin role.
/// </summary>
public sealed class PatchCodeSuggestionEndpoint : IEndpointDefinition
{
    public sealed record PatchCodeSuggestionRequest(
        string  Status,
        int?    VerifiedById,
        string? CommittedCode);

    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPatch("/code-suggestions/{id:int}", HandlePatchCodeSuggestion)
           .RequireAuthorization("StaffOrAdmin")
           .WithName("PatchCodeSuggestion")
           .WithTags("Coding")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status401Unauthorized)
           .Produces(StatusCodes.Status403Forbidden)
           .Produces(StatusCodes.Status404NotFound)
           .Produces(StatusCodes.Status409Conflict)
           .Produces(StatusCodes.Status422UnprocessableEntity);
    }

    // ── PATCH /code-suggestions/{id} ──────────────────────────────────────────

    public static async Task<IResult> HandlePatchCodeSuggestion(
        int                                  id,
        PatchCodeSuggestionRequest           body,
        ClinicalDbContext                     pgDb,
        ApplicationDbContext                  sqlDb,
        ILogger<PatchCodeSuggestionEndpoint> logger,
        CancellationToken                    ct)
    {
        var normalizedStatus = body.Status?.ToUpperInvariant();

        // M-001: reject unknown/null status before touching DB.
        if (normalizedStatus is not ("ACCEPTED" or "REJECTED"))
            return Results.BadRequest(new { error = "status must be 'Accepted' or 'Rejected'." });

        // AC-003: verifiedById required when accepting.
        if (normalizedStatus == "ACCEPTED" && body.VerifiedById is null)
            return Results.UnprocessableEntity(
                new { error = "verifiedById is required for code verification" });

        var suggestion = await pgDb.MedicalCodeSuggestions.FindAsync([id], ct);
        if (suggestion is null)
            return Results.NotFound(new { error = $"Code suggestion {id} not found." });

        // AC-002 edge case: already Accepted → 409.
        if (suggestion.Status == SuggestionStatus.Accepted)
            return Results.Conflict(new { error = "Code suggestion is already accepted." });

        var now = DateTime.UtcNow;

        // Determine final status.
        var newStatus = normalizedStatus switch
        {
            "ACCEPTED" when body.CommittedCode is not null
                         && body.CommittedCode != suggestion.SuggestedCode
                         => SuggestionStatus.Modified,
            "ACCEPTED" => SuggestionStatus.Accepted,
            "REJECTED" => SuggestionStatus.Rejected,
            _          => suggestion.Status
        };

        suggestion.Status       = newStatus;
        suggestion.VerifiedById = body.VerifiedById;
        suggestion.VerifiedAt   = now;

        if (body.CommittedCode is not null)
            suggestion.CommittedCode = body.CommittedCode;

        await pgDb.SaveChangesAsync(ct);

        // AC-006: AuditLog entry — best-effort; pgDb commit is already durable.
        sqlDb.Set<AuditLog>().Add(new AuditLog
        {
            EntityType = nameof(MedicalCodeSuggestion),
            EntityId   = suggestion.Id,
            ActorId    = body.VerifiedById,
            Action     = newStatus.ToString().ToUpperInvariant(),
            OccurredAt = now
        });
        try
        {
            await sqlDb.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "AuditLog write failed for MedicalCodeSuggestion {SuggestionId} Action={Action}. "
                + "Suggestion status was committed to pgDb. Manual reconciliation required.",
                suggestion.Id, newStatus);
        }

        return Results.Ok(new
        {
            message    = $"Code suggestion {id} updated to {newStatus}.",
            status     = newStatus.ToString(),
            verifiedAt = now
        });
    }
}
