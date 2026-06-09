using System.IdentityModel.Tokens.Jwt;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicalHealthcare.Api.Features.Appointments;

/// <summary>
/// Vertical-slice endpoint: GET /appointments/mine
///
/// Returns all appointments for the authenticated patient ordered by slot time descending.
/// Used by the patient dashboard and My Appointments page.
/// </summary>
public sealed class GetMyAppointmentsEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthorization(options =>
        {
            if (options.GetPolicy("PatientOnly") is null)
                options.AddPolicy("PatientOnly", p => p.RequireRole("patient"));
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/appointments/mine", HandleGetMyAppointments)
           .RequireAuthorization("PatientOnly")
           .WithName("GetMyAppointments")
           .WithTags("Appointments")
           .Produces<IEnumerable<MyAppointmentResponse>>(StatusCodes.Status200OK);
    }

    // ── GET /appointments/mine ────────────────────────────────────────────────

    public static async Task<IResult> HandleGetMyAppointments(
        HttpContext          httpContext,
        ApplicationDbContext db,
        CancellationToken    ct)
    {
        var subClaim = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                    ?? httpContext.User.FindFirst("sub")?.Value;

        if (!int.TryParse(subClaim, out var patientId))
            return Results.Unauthorized();

        var items = await db.Appointments
            .Include(a => a.Slot)
            .Where(a => a.PatientId == patientId)
            .OrderByDescending(a => a.Slot!.SlotTime)
            .Select(a => new MyAppointmentResponse(
                a.Id,
                a.SlotId,
                a.Slot!.SlotTime,
                a.Status.ToString(),
                a.BookedAt,
                a.NoShowRiskScore,
                a.IsHighRisk))
            .ToListAsync(ct);

        return Results.Ok(items);
    }
}

public sealed record MyAppointmentResponse(
    int      Id,
    int      SlotId,
    DateTime SlotTime,
    string   Status,
    DateTime BookedAt,
    int      NoShowRiskScore,
    bool     IsHighRisk);
