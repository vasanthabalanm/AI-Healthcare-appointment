using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicalHealthcare.Api.Features.Staff;

/// <summary>
/// Vertical-slice endpoint: GET /staff/queue
///
/// Returns today's walk-in queue ordered by Position (AC-001).
///   1. Requires StaffOrAdmin role.
///   2. Filters to today's date (UTC) and Status=Waiting.
///   3. Includes patient full name from the joined UserAccount.
///   4. Returns [{entryId, patientId, patientName, position, isWalkIn, rowVersion}].
/// </summary>
public sealed class GetQueueEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/staff/queue", HandleGetQueue)
           .RequireAuthorization("StaffOrAdmin")
           .WithName("GetQueue")
           .WithTags("Staff")
           .Produces<IReadOnlyList<QueueEntryDto>>(StatusCodes.Status200OK);
    }

    // ── GET /staff/queue ──────────────────────────────────────────────────────

    public static async Task<IResult> HandleGetQueue(
        ApplicationDbContext db,
        CancellationToken    ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now   = DateTime.UtcNow;

        var entries = await db.QueueEntries
            .Where(q => q.QueueDate == today && q.Status != QueueStatus.Removed)
            .OrderBy(q => q.Position)
            .GroupJoin(
                db.IntakeRecords.AsNoTracking(),
                q  => q.PatientId,
                ir => ir.PatientId,
                (q, intakes) => new { q, intakes })
            .SelectMany(
                x => x.intakes.DefaultIfEmpty(),
                (x, ir) => new
                {
                    x.q.Id,
                    x.q.PatientId,
                    PatientName     = x.q.Patient != null ? x.q.Patient.FirstName + " " + x.q.Patient.LastName : "Unknown",
                    x.q.Position,
                    x.q.IsWalkIn,
                    x.q.Status,
                    x.q.CreatedAt,
                    ChiefComplaint  = ir != null ? ir.ChiefComplaint : null,
                    x.q.RowVersion,
                })
            .AsNoTracking()
            .ToListAsync(ct);

        var dtos = entries.Select(e => new QueueEntryDto(
            e.Id,
            e.Position,
            e.PatientId,
            e.PatientName,
            e.IsWalkIn ? "WalkIn" : "Appointment",
            e.CreatedAt.ToString("o"),
            e.Status == QueueStatus.CheckedIn ? "InProgress" : e.Status.ToString(),
            e.ChiefComplaint ?? string.Empty,
            (int)(now - e.CreatedAt).TotalMinutes)).ToList();

        return Results.Ok(dtos);
    }
}

/// <summary>Queue entry DTO returned by GET /staff/queue.</summary>
public sealed record QueueEntryDto(
    int    Id,
    int    Position,
    int    PatientId,
    string PatientName,
    string ArrivalType,
    string ArrivedAt,
    string Status,
    string ChiefComplaint,
    int    WaitMinutes);
