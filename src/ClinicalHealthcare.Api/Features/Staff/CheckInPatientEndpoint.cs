using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace ClinicalHealthcare.Api.Features.Staff;

/// <summary>
/// Vertical-slice endpoint: PATCH /appointments/{id}/checkin
///
/// Transitions Appointment.Status from Scheduled → Arrived (AC-001/AC-002).
///   1. Requires StaffOrAdmin role.
///   2. FSM interceptor enforces the Scheduled→Arrived transition (AC-002).
///   3. If no QueueEntry exists (online-booked), creates one at the end of today's queue (AC-003).
///      If one exists with Status=Waiting, marks it CheckedIn.  Already-CheckedIn/Removed → no-op.
///   4. RowVersion on Appointment prevents double check-in; concurrent request → 409 (AC-004).
///   5. AuditLog entry written on successful check-in (AC-005).
///   6. Staff ID sourced from JWT sub claim — never from request body (OWASP A01).
/// </summary>
public sealed class CheckInPatientEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPatch("/appointments/{id:int}/checkin", HandleCheckIn)
           .RequireAuthorization("StaffOrAdmin")
           .WithName("CheckInPatient")
           .WithTags("Staff")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status404NotFound)
           .Produces(StatusCodes.Status409Conflict);
    }

    // ── PATCH /appointments/{id}/checkin ─────────────────────────────────────

    public static async Task<IResult> HandleCheckIn(
        int                  id,
        HttpContext          httpContext,
        ApplicationDbContext db,
        CancellationToken    ct)
    {
        // Staff ID from JWT — never from request body (OWASP A01).
        var sub = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
               ?? httpContext.User.FindFirst("sub")?.Value;
        if (!int.TryParse(sub, out var staffId))
            return Results.Unauthorized();

        var appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (appointment is null)
            return Results.NotFound(new { error = $"Appointment {id} not found." });

        // AC-002: only Scheduled → Arrived is valid; FSM interceptor throws on any other transition.
        if (appointment.Status != AppointmentStatus.Scheduled)
            return Results.Conflict(new
            {
                error = $"Appointment {id} cannot be checked in: current status is {appointment.Status}."
            });

        appointment.Status = AppointmentStatus.Arrived;

        // AC-003: ensure a QueueEntry exists for today and mark it CheckedIn.
        // Online-booked patients have no pre-existing QueueEntry — create one now so they
        // appear in the Same-Day Queue after check-in.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var queueEntry = await db.QueueEntries
            .FirstOrDefaultAsync(q => q.PatientId == appointment.PatientId
                                   && q.QueueDate == today, ct);

        if (queueEntry is null)
        {
            // Compute next position (MAX + 1) for today; default to 1 if queue is empty.
            var nextPosition = await db.QueueEntries
                .Where(q => q.QueueDate == today)
                .Select(q => (int?)q.Position)
                .MaxAsync(ct) ?? 0;

            db.QueueEntries.Add(new QueueEntry
            {
                PatientId      = appointment.PatientId,
                QueueDate      = today,
                Position       = nextPosition + 1,
                Status         = QueueStatus.CheckedIn,
                IsWalkIn       = false,
                AddedByStaffId = staffId,
            });
        }
        else if (queueEntry.Status == QueueStatus.Waiting)
        {
            queueEntry.Status = QueueStatus.CheckedIn;
        }

        // AC-005: AuditLog INSERT — must be in same SaveChangesAsync as the Appointment update
        // so that both succeed or fail together. AuditLogs is INSERT-only at SQL Server GRANT level.
        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(Appointment),
            EntityId   = appointment.Id,
            ActorId    = staffId,
            Action     = "CheckIn",
            AfterValue = $"{{\"appointmentId\":{appointment.Id},\"patientId\":{appointment.PatientId},\"status\":\"Arrived\"}}",
        });

        try
        {
            // AC-004: RowVersion on Appointment detected by EF Core; concurrent update → DbUpdateConcurrencyException → 409.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { error = "Appointment was modified concurrently. Refresh and retry." });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Invalid Appointment status transition"))
        {
            // FSM interceptor fired (should not reach here given the guard above, but defensive).
            return Results.Conflict(new { error = ex.Message });
        }

        return Results.Ok(new { checkedInAppointmentId = appointment.Id });
    }
}
