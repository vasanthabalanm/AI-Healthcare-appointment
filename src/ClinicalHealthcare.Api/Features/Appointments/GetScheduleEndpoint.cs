using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicalHealthcare.Api.Features.Appointments;

/// <summary>
/// Vertical-slice endpoint: GET /appointments/schedule?date=yyyy-MM-dd
///
/// Returns all appointments (with patient name, slot time, and no-show risk score)
/// for the given date. Scoped to Staff and Admin only (US_022 staff view).
/// </summary>
public sealed class GetScheduleEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthorization(options =>
        {
            if (options.GetPolicy("StaffOrAdmin") is null)
                options.AddPolicy("StaffOrAdmin", p => p.RequireRole("staff", "admin"));
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/appointments/schedule", HandleGetSchedule)
           .RequireAuthorization("StaffOrAdmin")
           .WithName("GetSchedule")
           .WithTags("Appointments")
           .Produces<IEnumerable<ScheduleItemResponse>>(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status400BadRequest);
    }

    // ── GET /appointments/schedule?date=yyyy-MM-dd ────────────────────────────

    public static async Task<IResult> HandleGetSchedule(
        string?              date,
        ApplicationDbContext db,
        CancellationToken    ct)
    {
        if (!DateOnly.TryParse(date, out var parsed))
            return Results.BadRequest(new { error = "date query parameter must be in yyyy-MM-dd format." });

        var dayStart = parsed.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dayEnd   = parsed.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var items = await db.Appointments
            .Include(a => a.Slot)
            .Include(a => a.Patient)
            .Where(a => a.Slot != null &&
                        a.Slot.SlotTime >= dayStart &&
                        a.Slot.SlotTime <= dayEnd)
            .OrderBy(a => a.Slot!.SlotTime)
            .Select(a => new ScheduleItemResponse(
                a.Id,
                a.Patient!.FirstName + " " + a.Patient.LastName,
                a.Slot!.SlotTime,
                a.Slot.DurationMinutes,
                a.Status.ToString(),
                a.NoShowRiskScore,
                a.IsHighRisk))
            .ToListAsync(ct);

        return Results.Ok(items);
    }
}

public sealed record ScheduleItemResponse(
    int    AppointmentId,
    string PatientName,
    DateTime SlotTime,
    int    DurationMinutes,
    string Status,
    int    NoShowRiskScore,
    bool   IsHighRisk);
