using System.IdentityModel.Tokens.Jwt;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Configuration;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Jobs;
using Hangfire;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicalHealthcare.Api.Features.Appointments;

/// <summary>
/// Vertical-slice endpoint: DELETE /appointments/{id}
///
/// Cancels a Scheduled appointment owned by the authenticated patient (AC-001).
/// Enforces a configurable cancellation cutoff window (AC-002).
/// Enqueues <see cref="SwapMonitorJob"/> so the freed slot is offered to the
/// waitlist before being returned to general availability (AC-003).
/// Deletes any pending T-48 h reminder Hangfire job (AC-005).
/// </summary>
public sealed class CancelAppointmentEndpoint : IEndpointDefinition
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
        app.MapDelete("/appointments/{id:int}", HandleCancelAppointment)
           .RequireAuthorization("PatientOnly")
           .WithName("CancelAppointment")
           .WithTags("Appointments")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status403Forbidden)
           .Produces(StatusCodes.Status404NotFound);
    }

    // ── DELETE /appointments/{id} ─────────────────────────────────────────────

    public static async Task<IResult> HandleCancelAppointment(
        int                   id,
        HttpContext            httpContext,
        ApplicationDbContext   db,
        ICacheService          cache,
        IBackgroundJobClient   jobs,
        IOptions<AppSettings>  appSettings,
        CancellationToken      ct)
    {
        var subClaim = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                    ?? httpContext.User.FindFirst("sub")?.Value;

        if (!int.TryParse(subClaim, out var patientId))
            return Results.Unauthorized();

        var appointment = await db.Appointments
            .Include(a => a.Slot)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (appointment is null)
            return Results.NotFound(new { error = "Appointment not found." });

        if (appointment.PatientId != patientId)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        if (appointment.Status != AppointmentStatus.Scheduled)
            return Results.BadRequest(new { error = "Only Scheduled appointments can be cancelled." });

        // AC-002 — enforce cancellation cutoff window.
        if (appointment.Slot is not null)
        {
            var cutoffHours = appSettings.Value.CancellationCutoffHours;
            if (appointment.Slot.SlotTime <= DateTime.UtcNow.AddHours(cutoffHours))
                return Results.BadRequest(new
                {
                    error = $"Cancellations must be made at least {cutoffHours} hour(s) before the appointment."
                });
        }

        // AC-005 — delete the pending T-48h reminder job before committing state change.
        if (!string.IsNullOrWhiteSpace(appointment.ReminderJobId))
            jobs.ChangeState(appointment.ReminderJobId, new DeletedState(), null);

        // TASK_027 / AC-005 — delete pending SMS reminder jobs.
        if (!string.IsNullOrWhiteSpace(appointment.SmsReminderJobId48h))
            jobs.ChangeState(appointment.SmsReminderJobId48h, new DeletedState(), null);
        if (!string.IsNullOrWhiteSpace(appointment.SmsReminderJobId2h))
            jobs.ChangeState(appointment.SmsReminderJobId2h, new DeletedState(), null);

        // TASK_028 / AC-004 — delete pending email reminder job.
        if (!string.IsNullOrWhiteSpace(appointment.EmailReminderJobId))
            jobs.ChangeState(appointment.EmailReminderJobId, new DeletedState(), null);

        // Transition appointment to Cancelled (enforced by AppointmentFsmInterceptor).
        appointment.Status = AppointmentStatus.Cancelled;
        await db.SaveChangesAsync(ct);

        // AC-003 — enqueue SwapMonitorJob; the job owns slot-release + cache invalidation.
        // Slot stays unavailable to prevent a competing booking from preempting the waitlist.
        jobs.Enqueue<SwapMonitorJob>(j => j.ExecuteAsync(appointment.SlotId, null!));

        // Invalidate slot cache for the cancelled date so GET /slots reflects current state.
        if (appointment.Slot is not null)
        {
            var dateKey = $"{GetSlotsEndpoint.CacheKeyPrefix}{DateOnly.FromDateTime(appointment.Slot.SlotTime):yyyy-MM-dd}";
            await cache.DeleteAsync(dateKey, ct);
        }

        return Results.Ok(new { message = "Appointment cancelled." });
    }
}

