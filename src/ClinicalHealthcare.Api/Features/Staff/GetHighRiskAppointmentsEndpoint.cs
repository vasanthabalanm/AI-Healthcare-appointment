using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicalHealthcare.Api.Features.Staff;

/// <summary>
/// Vertical-slice endpoint: GET /schedule/high-risk[?date=]
///
/// Returns today's (or the requested date's) appointments where IsHighRisk=true (AC-001).
///   1. Requires StaffOrAdmin role.
///   2. Defaults to today (UTC date); <c>?date=</c> overrides.
///   3. Excludes Cancelled appointments.
/// </summary>
public sealed class GetHighRiskAppointmentsEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/schedule/high-risk", HandleGetHighRisk)
           .RequireAuthorization("StaffOrAdmin")
           .WithName("GetHighRiskAppointments")
           .WithTags("Staff")
           .Produces<IReadOnlyList<HighRiskEntryDto>>(StatusCodes.Status200OK);
    }

    // ── GET /schedule/high-risk[?date=] ───────────────────────────────────────

    public static async Task<IResult> HandleGetHighRisk(
        ApplicationDbContext db,
        DateOnly?            date = null,
        CancellationToken    ct   = default)
    {
        var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var entries = await db.Appointments
            .AsNoTracking()
            .Where(a => a.IsHighRisk
                     && a.Status != AppointmentStatus.Cancelled
                     && DateOnly.FromDateTime(a.Slot!.SlotTime) == targetDate)
            .OrderBy(a => a.Slot!.SlotTime)
            .Select(a => new HighRiskEntryDto(
                a.Id,
                a.PatientId,
                a.Patient != null ? a.Patient.FirstName + " " + a.Patient.LastName : "Unknown",
                a.Slot!.SlotTime,
                a.NoShowRiskScore))
            .ToListAsync(ct);

        return Results.Ok(entries);
    }
}

/// <summary>Single high-risk appointment entry.</summary>
public sealed record HighRiskEntryDto(
    int      AppointmentId,
    int      PatientId,
    string   PatientName,
    DateTime SlotTime,
    int      RiskScore);
