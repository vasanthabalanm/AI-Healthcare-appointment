using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.AI;
using ClinicalHealthcare.Infrastructure.Cache;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicalHealthcare.Api.Features.Intake;

/// <summary>
/// Vertical-slice endpoint: POST /intake/ai/switch-to-manual
///
/// Transitions the patient from AI-assisted to manual intake (AC-004):
///   1. Loads <see cref="AiIntakeSession.ConfirmedFields"/> from Redis.
///   2. Returns confirmed fields as pre-filled form fields.
///   3. Deletes the AI session from Redis.
///
/// The patient then uses POST /intake/manual to complete the rest of the form
/// with confirmed fields pre-populated.
///
/// Returns HTTP 404 when the session has expired.
/// </summary>
public sealed class SwitchToManualEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/intake/ai/switch-to-manual", HandleSwitchToManual)
           .RequireAuthorization("PatientOnly")
           .WithName("SwitchAiIntakeToManual")
           .WithTags("Intake")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status404NotFound);
    }

    // ── POST /intake/ai/switch-to-manual ─────────────────────────────────────

    public static async Task<IResult> HandleSwitchToManual(
        SwitchToManualRequest request,
        HttpContext           httpContext,
        ICacheService         cache,
        CancellationToken     ct)
    {
        var patientId = StartAiIntakeEndpoint.ExtractPatientId(httpContext);
        if (patientId is null)
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.SessionId))
            return Results.BadRequest(new { error = "sessionId is required." });

        var cacheKey = StartAiIntakeEndpoint.CacheKey(request.SessionId);
        var session  = await cache.GetAsync<AiIntakeSession>(cacheKey, ct);

        if (session is null)
            return Results.NotFound(new { error = "Session not found or has expired." });

        if (session.PatientId != patientId.Value)
            return Results.Forbid();

        // Capture confirmed fields before deleting the session (AC-004).
        var confirmedFields = new Dictionary<string, string>(session.ConfirmedFields);

        // Delete the AI session — patient continues via POST /intake/manual.
        await cache.DeleteAsync(cacheKey, ct);

        return Results.Ok(new
        {
            message         = "Switched to manual intake. Confirmed fields are pre-filled.",
            confirmedFields,
        });
    }
}

/// <summary>Request body for <see cref="SwitchToManualEndpoint"/>.</summary>
public sealed record SwitchToManualRequest(string SessionId);
