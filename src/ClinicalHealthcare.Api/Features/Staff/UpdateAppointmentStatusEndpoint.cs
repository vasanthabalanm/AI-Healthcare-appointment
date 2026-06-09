using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Api.Features.Appointments;
using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Jobs;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace ClinicalHealthcare.Api.Features.Staff;

/// <summary>
/// Vertical-slice endpoint: PATCH /appointments/{id}/status
///
/// Transitions an Appointment to NoShow (AC-003 / AC-004).
///   1. Requires StaffOrAdmin role.
///   2. Staff ID from JWT sub claim (OWASP A01).
///   3. Only "NoShow" is accepted as target status (FSM: Scheduled→NoShow).
///   4. FSM interceptor enforces the transition; Arrived→NoShow or Cancelled→NoShow → exception → 409.
///   5. Releases slot (IsAvailable=true) on NoShow (AC-004).
///   6. Writes AuditLog entry (AC-003).
///   7. Enqueues SwapMonitorJob so the freed slot is offered to waitlisted patients (AC-004).
///   8. Invalidates slot date cache.
/// </summary>
public sealed class UpdateAppointmentStatusEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPatch("/appointments/{id:int}/status", HandleUpdateStatus)
           .RequireAuthorization("StaffOrAdmin")
           .WithName("UpdateAppointmentStatus")
           .WithTags("Staff")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status404NotFound)
           .Produces(StatusCodes.Status409Conflict);
    }

    // ── PATCH /appointments/{id}/status ──────────────────────────────────────

    public static async Task<IResult> HandleUpdateStatus(
        int                      id,
        UpdateStatusRequest      request,
        HttpContext               httpContext,
        ApplicationDbContext     db,
        ICacheService            cache,
        IBackgroundJobClient     jobs,
        CancellationToken        ct)
    {
        // Staff ID from JWT — never from request body (OWASP A01).
        var sub = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
               ?? httpContext.User.FindFirst("sub")?.Value;
        if (!int.TryParse(sub, out var staffId))
            return Results.Unauthorized();

        // Only "NoShow" is supported by this endpoint.
        if (!string.Equals(request.Status, "NoShow", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { error = "Only 'NoShow' status transitions are supported by this endpoint." });

        var appointment = await db.Appointments
            .Include(a => a.Slot)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (appointment is null)
            return Results.NotFound(new { error = $"Appointment {id} not found." });

        // FSM guard: only Scheduled → NoShow is valid (interceptor also enforces).
        if (appointment.Status != AppointmentStatus.Scheduled)
            return Results.Conflict(new
            {
                error = $"Appointment {id} cannot be marked NoShow: current status is {appointment.Status}."
            });

        // AC-003 — transition via FSM (interceptor will validate at save time).
        appointment.Status = AppointmentStatus.NoShow;

        // AC-004 — release the slot immediately so the swap offer is on a live slot.
        if (appointment.Slot is not null)
            appointment.Slot.IsAvailable = true;

        // AC-003 — AuditLog INSERT (same SaveChangesAsync call — INSERT-only constraint).
        db.AuditLogs.Add(new AuditLog
        {
            EntityType = nameof(Appointment),
            EntityId   = appointment.Id,
            ActorId    = staffId,
            Action     = "NoShow",
            AfterValue = $"{{\"appointmentId\":{appointment.Id},\"patientId\":{appointment.PatientId},\"status\":\"NoShow\"}}",
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Invalid Appointment status transition"))
        {
            return Results.Conflict(new { error = ex.Message });
        }

        // AC-004 — enqueue SwapMonitorJob for the freed slot.
        jobs.Enqueue<SwapMonitorJob>(j => j.ExecuteAsync(appointment.SlotId, null!));

        // Invalidate slot date cache so GET /slots reflects updated availability.
        if (appointment.Slot is not null)
        {
            var dateKey = $"{GetSlotsEndpoint.CacheKeyPrefix}{DateOnly.FromDateTime(appointment.Slot.SlotTime):yyyy-MM-dd}";
            await cache.DeleteAsync(dateKey, ct);
        }

        return Results.Ok(new { noShowAppointmentId = appointment.Id });
    }
}

/// <summary>Request body for PATCH /appointments/{id}/status.</summary>
public sealed record UpdateStatusRequest(string Status);
