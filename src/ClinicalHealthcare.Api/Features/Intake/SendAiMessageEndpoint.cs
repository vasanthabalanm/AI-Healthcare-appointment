using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.AI;
using ClinicalHealthcare.Infrastructure.Cache;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicalHealthcare.Api.Features.Intake;

/// <summary>
/// Vertical-slice endpoint: POST /intake/ai/message
///
/// Proxies a patient message to Rasa and enforces the confidence threshold (AC-002 / AC-003):
///   - confidence ≥ 0.70 → field committed to <see cref="AiIntakeSession.ConfirmedFields"/>
///     in Redis; session TTL reset.
///   - confidence &lt; 0.70 → clarification text returned; no field committed.
///
/// Returns HTTP 404 when the session has expired or was not started.
/// Returns HTTP 503 when Rasa is unavailable (AC-005).
/// </summary>
public sealed class SendAiMessageEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/intake/ai/message", HandleSendAiMessage)
           .RequireAuthorization("PatientOnly")
           .WithName("SendAiMessage")
           .WithTags("Intake")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status404NotFound)
           .Produces(StatusCodes.Status503ServiceUnavailable);
    }

    // ── POST /intake/ai/message ───────────────────────────────────────────────

    /// <summary>
    /// Fixed sequence of intake fields committed in conversation order.
    /// The AI asks about each in turn; we auto-advance when confidence is sufficient.
    /// </summary>
    private static readonly string[] FieldSequence =
        ["chiefComplaint", "currentMeds", "allergies", "medicalHistory"];

    public static async Task<IResult> HandleSendAiMessage(
        SendAiMessageRequest request,
        HttpContext          httpContext,
        ICacheService        cache,
        IRasaIntakeService    rasa,
        CancellationToken    ct)
    {
        var patientId = StartAiIntakeEndpoint.ExtractPatientId(httpContext);
        if (patientId is null)
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.SessionId))
            return Results.BadRequest(new { error = "sessionId is required." });

        if (string.IsNullOrWhiteSpace(request.Message))
            return Results.BadRequest(new { error = "message is required." });

        var cacheKey = StartAiIntakeEndpoint.CacheKey(request.SessionId);
        var session  = await cache.GetAsync<AiIntakeSession>(cacheKey, ct);

        if (session is null)
            return Results.NotFound(new { error = "Session not found or has expired." });

        // Ownership guard — prevent cross-patient session access (OWASP A01).
        if (session.PatientId != patientId.Value)
            return Results.Forbid();

        RasaMessage rasaReply;
        try
        {
            rasaReply = await rasa.SendMessageAsync(request.SessionId, request.Message, ct);
        }
        catch (RasaUnavailableException)
        {
            return Results.Json(
                new { error = "AI intake service unavailable" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var sufficientConfidence = IRasaIntakeService.IsSufficientConfidence(rasaReply.Confidence);

        // Determine which field to commit:
        //   1. Use the explicit fieldName from the request if provided (backward compat).
        //   2. Otherwise auto-advance by session.CurrentStep so the frontend never needs
        //      to track conversation state.
        var resolvedFieldName = !string.IsNullOrWhiteSpace(request.FieldName)
            ? request.FieldName
            : session.CurrentStep < FieldSequence.Length
                ? FieldSequence[session.CurrentStep]
                : null;

        var fieldCommitted = false;
        if (sufficientConfidence && resolvedFieldName is not null
            && !session.ConfirmedFields.ContainsKey(resolvedFieldName))
        {
            session.ConfirmedFields[resolvedFieldName] = request.Message;
            session.CurrentStep = Math.Min(session.CurrentStep + 1, FieldSequence.Length);
            fieldCommitted = true;
        }

        // Reset TTL on every message (AC-002).
        await cache.SetAsync(
            cacheKey, session, StartAiIntakeEndpoint.SessionTtl, ct);

        return Results.Ok(new
        {
            text               = rasaReply.Text,
            confidence         = rasaReply.Confidence,
            fieldCommitted,
            confirmedFields    = session.ConfirmedFields,
            requiresClarification = !sufficientConfidence,
        });
    }
}

/// <summary>Request body for <see cref="SendAiMessageEndpoint"/>.</summary>
public sealed record SendAiMessageRequest(
    string  SessionId,
    string  Message,
    /// <summary>Optional: if provided and confidence ≥ 0.70, this key is stored in confirmedFields.</summary>
    string? FieldName = null);
