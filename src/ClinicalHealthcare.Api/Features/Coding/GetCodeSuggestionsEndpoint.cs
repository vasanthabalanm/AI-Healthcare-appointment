using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicalHealthcare.Api.Features.Coding;

/// <summary>
/// Vertical-slice endpoint: GET /patients/{id}/code-suggestions
///
/// Returns all Pending <see cref="MedicalCodeSuggestion"/> rows for the given
/// patient, grouped by <see cref="CodeType"/> (ICD10 / CPT).
///
/// Security: Requires StaffOrAdmin role.
/// </summary>
public sealed class GetCodeSuggestionsEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/patients/{id:int}/code-suggestions", HandleGetCodeSuggestions)
           .RequireAuthorization("StaffOrAdmin")
           .WithName("GetCodeSuggestions")
           .WithTags("Coding")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status401Unauthorized)
           .Produces(StatusCodes.Status403Forbidden);
    }

    // ── GET /patients/{id}/code-suggestions ───────────────────────────────────

    public static async Task<IResult> HandleGetCodeSuggestions(
        int               id,
        ClinicalDbContext  pgDb,
        CancellationToken ct)
    {
        var rows = await pgDb.MedicalCodeSuggestions
            .Where(s => s.PatientId == id)
            .OrderBy(s => s.CodeType)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);

        // Return a flat array matching the Angular CodeSuggestion interface:
        //   id, codeType ("ICD10"|"CPT"), code, description,
        //   confidenceScore (0-100 int), status ("Pending"|"Accepted"|"Modified"|"Rejected")
        var result = rows.Select(s => new
        {
            id              = s.Id,
            codeType        = s.CodeType.ToString(),
            code            = s.CommittedCode ?? s.SuggestedCode,
            description     = s.CodeDescription,
            confidenceScore = (int)Math.Round(s.ConfidenceScore * 100),
            status          = s.Status.ToString()
        });

        return Results.Ok(result);
    }
}
