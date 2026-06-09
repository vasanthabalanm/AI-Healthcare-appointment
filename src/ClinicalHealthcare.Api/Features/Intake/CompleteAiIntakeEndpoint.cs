using System.IdentityModel.Tokens.Jwt;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.AI;
using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicalHealthcare.Api.Features.Intake;

/// <summary>
/// Vertical-slice endpoint: POST /intake/ai/complete
///
/// Finalises an AI intake session (AC-006):
///   1. Loads <see cref="AiIntakeSession.ConfirmedFields"/> from Redis.
///   2. Creates an <see cref="IntakeRecord"/> with <see cref="IntakeSource.AI"/>.
///   3. Deletes the Redis session.
///   4. Returns HTTP 201 with the new IntakeRecord ID.
///
/// Returns HTTP 404 when the session has expired.
/// </summary>
public sealed class CompleteAiIntakeEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/intake/ai/complete", HandleCompleteAiIntake)
           .RequireAuthorization("PatientOnly")
           .WithName("CompleteAiIntake")
           .WithTags("Intake")
           .Produces(StatusCodes.Status201Created)
           .Produces(StatusCodes.Status404NotFound);
    }

    // ── POST /intake/ai/complete ──────────────────────────────────────────────

    public static async Task<IResult> HandleCompleteAiIntake(
        CompleteAiIntakeRequest request,
        HttpContext             httpContext,
        ApplicationDbContext    db,
        ICacheService           cache,
        CancellationToken       ct)
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

        // Idempotency guard: if this session already completed, return the existing record (AC-006).
        if (session.CompletedIntakeRecordId is int existingId)
            return Results.Created($"/intake/{existingId}", new { intakeRecordId = existingId });
        var fields  = session.ConfirmedFields;
        var record  = new IntakeRecord
        {
            PatientId      = patientId.Value,
            Source         = IntakeSource.AI,
            IntakeGroupId  = Guid.NewGuid(),
            Version        = 1,
            IsLatest       = true,
            SubmittedAt    = DateTime.UtcNow,
            ChiefComplaint = fields.GetValueOrDefault("chiefComplaint"),
            CurrentMeds    = fields.GetValueOrDefault("currentMeds"),
            Allergies      = fields.GetValueOrDefault("allergies"),
            MedicalHistory = fields.GetValueOrDefault("medicalHistory"),
        };

        db.IntakeRecords.Add(record);
        await db.SaveChangesAsync(ct);

        // Persist the record ID back into the session BEFORE deleting it.
        // If DeleteAsync fails on the next line, a retry will see CompletedIntakeRecordId
        // and return the same record instead of creating a duplicate.
        session.CompletedIntakeRecordId = record.Id;
        await cache.SetAsync(cacheKey, session, StartAiIntakeEndpoint.SessionTtl, ct);

        // Delete the Redis session (AC-002 — no orphaned sessions).
        await cache.DeleteAsync(cacheKey, ct);

        return Results.Created($"/intake/{record.Id}", new { intakeRecordId = record.Id });
    }
}

/// <summary>Request body for <see cref="CompleteAiIntakeEndpoint"/>.</summary>
public sealed record CompleteAiIntakeRequest(string SessionId);
