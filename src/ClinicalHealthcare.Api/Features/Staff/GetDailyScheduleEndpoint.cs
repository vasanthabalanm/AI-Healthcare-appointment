using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicalHealthcare.Api.Features.Staff;

/// <summary>
/// Vertical-slice endpoint: GET /schedule/today[?date=][&amp;page=]
///
/// Returns the daily appointment schedule for staff (AC-001 through AC-005).
///   1. Requires StaffOrAdmin role.
///   2. Defaults to today (UTC date); <c>?date=</c> overrides to any date (AC-004).
///   3. Excludes Cancelled appointments.
///   4. Left-joins IntakeRecord (latest, non-deleted — honoured by global query filter)
///      to compute <c>intakeStatus</c>: Submitted | Pending (AC-002).
///   5. Includes <c>riskFlag</c> from <c>Appointment.IsHighRisk</c> (AC-003).
///   6. Orders by <c>Slot.SlotTime ASC</c> (AC-001).
///   7. Paginates at 50 per page (AC-005).
/// </summary>
public sealed class GetDailyScheduleEndpoint : IEndpointDefinition
{
    public const int PageSize = 50;

    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/schedule/today", HandleGetDailySchedule)
           .RequireAuthorization("StaffOrAdmin")
           .WithName("GetDailySchedule")
           .WithTags("Staff")
           .Produces<SchedulePageResponse>(StatusCodes.Status200OK);
    }

    // ── GET /schedule/today[?date=][&page=] ───────────────────────────────────

    public static async Task<IResult> HandleGetDailySchedule(
        ApplicationDbContext db,
        DateOnly?            date = null,
        int                  page = 1,
        CancellationToken    ct   = default)
    {
        page = Math.Max(1, page);

        var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // Base query: appointments whose slot falls on targetDate, excluding Cancelled.
        // IntakeRecord global query filter (IsLatest && !IsDeleted) is respected by default.
        var query = db.Appointments
            .AsNoTracking()
            .Where(a => a.Status != AppointmentStatus.Cancelled
                     && DateOnly.FromDateTime(a.Slot!.SlotTime) == targetDate)
            .OrderBy(a => a.Slot!.SlotTime);

        var totalCount = await query.CountAsync(ct);
        var pageCount  = totalCount == 0 ? 0 : (int)Math.Ceiling((double)totalCount / PageSize);

        // Left-join IntakeRecord by PatientId.
        // GroupJoin produces a left outer join: appointments without an intake get an empty list.
        var data = await query
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .GroupJoin(
                db.IntakeRecords.AsNoTracking(),
                a  => a.PatientId,
                ir => ir.PatientId,
                (a, intakes) => new { a, intakes })
            .SelectMany(
                x => x.intakes.DefaultIfEmpty(),
                (x, ir) => new ScheduleEntryDto(
                    x.a.Id,
                    x.a.PatientId,
                    x.a.Patient != null
                        ? x.a.Patient.FirstName + " " + x.a.Patient.LastName
                        : "Unknown",
                    x.a.Slot!.SlotTime,
                    x.a.Status.ToString(),
                    ir != null ? "Submitted"
                               : (x.a.Patient != null && x.a.Patient.WalkIn) ? "NA"
                               : "Pending",
                    x.a.IsHighRisk))
            .ToListAsync(ct);

        return Results.Ok(new SchedulePageResponse(page, PageSize, totalCount, pageCount, data));
    }
}

/// <summary>Response envelope for GET /schedule/today.</summary>
public sealed record SchedulePageResponse(
    int                          Page,
    int                          PageSize,
    int                          TotalCount,
    int                          PageCount,
    IReadOnlyList<ScheduleEntryDto> Data);

/// <summary>Single appointment row in the daily schedule.</summary>
public sealed record ScheduleEntryDto(
    int      AppointmentId,
    int      PatientId,
    string   PatientName,
    DateTime SlotTime,
    string   Status,
    string   IntakeStatus,   // "Submitted" | "Pending"
    bool     RiskFlag);
