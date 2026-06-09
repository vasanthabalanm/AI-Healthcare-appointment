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
/// Vertical-slice endpoint: PATCH /appointments/{id}/reschedule
///
/// Moves a Scheduled appointment to a different available slot (AC-004).
/// Uses EF Core rowversion on the new <see cref="Slot"/> to prevent two patients
/// from concurrently booking the same replacement slot (concurrent → 409).
/// Releases the old slot via <see cref="SwapMonitorJob"/>, cancels the stale
/// T-48h reminder, and re-schedules a reminder for the new slot time.
/// </summary>
public sealed class RescheduleAppointmentEndpoint : IEndpointDefinition
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
        app.MapPatch("/appointments/{id:int}/reschedule", HandleRescheduleAppointment)
           .RequireAuthorization("PatientOnly")
           .WithName("RescheduleAppointment")
           .WithTags("Appointments")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status403Forbidden)
           .Produces(StatusCodes.Status404NotFound)
           .Produces(StatusCodes.Status409Conflict);
    }

    // ── PATCH /appointments/{id}/reschedule ───────────────────────────────────

    public static async Task<IResult> HandleRescheduleAppointment(
        int                   id,
        RescheduleRequest     request,
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

        if (request.NewSlotId <= 0)
            return Results.BadRequest(new { error = "NewSlotId must be a positive integer." });

        var appointment = await db.Appointments
            .Include(a => a.Slot)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (appointment is null)
            return Results.NotFound(new { error = "Appointment not found." });

        if (appointment.PatientId != patientId)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        if (appointment.Status != AppointmentStatus.Scheduled)
            return Results.BadRequest(new { error = "Only Scheduled appointments can be rescheduled." });

        if (appointment.SlotId == request.NewSlotId)
            return Results.BadRequest(new { error = "New slot is the same as the current slot." });

        // AC-002 — enforce cancellation cutoff on the current appointment.
        if (appointment.Slot is not null)
        {
            var cutoffHours = appSettings.Value.CancellationCutoffHours;
            if (appointment.Slot.SlotTime <= DateTime.UtcNow.AddHours(cutoffHours))
                return Results.BadRequest(new
                {
                    error = $"Reschedules must be made at least {cutoffHours} hour(s) before the appointment."
                });
        }

        var newSlot = await db.Slots.FirstOrDefaultAsync(s => s.Id == request.NewSlotId, ct);

        if (newSlot is null)
            return Results.BadRequest(new { error = "New slot not found." });

        if (!newSlot.IsAvailable)
            return Results.Conflict(new { error = "New slot is not available." });

        if (newSlot.SlotTime <= DateTime.UtcNow)
            return Results.BadRequest(new { error = "New slot must be in the future." });

        // AC-004 — atomic transaction with rowversion check on new slot.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        try
        {
            // Book the new slot (rowversion concurrency guard).
            newSlot.IsAvailable = false;

            // Update appointment to new slot.
            var oldSlotId   = appointment.SlotId;
            var oldSlotTime = appointment.Slot?.SlotTime;
            appointment.SlotId = newSlot.Id;

            // Cancel stale reminder job before saving.
            var oldReminderJobId = appointment.ReminderJobId;
            if (!string.IsNullOrWhiteSpace(oldReminderJobId))
                jobs.ChangeState(oldReminderJobId, new DeletedState(), null);

            // TASK_027 / AC-005 — delete stale SMS reminder jobs.
            if (!string.IsNullOrWhiteSpace(appointment.SmsReminderJobId48h))
                jobs.ChangeState(appointment.SmsReminderJobId48h, new DeletedState(), null);
            if (!string.IsNullOrWhiteSpace(appointment.SmsReminderJobId2h))
                jobs.ChangeState(appointment.SmsReminderJobId2h, new DeletedState(), null);
            // TASK_028 — delete stale email reminder job.
            if (!string.IsNullOrWhiteSpace(appointment.EmailReminderJobId))
                jobs.ChangeState(appointment.EmailReminderJobId, new DeletedState(), null);
            appointment.ReminderJobId        = null;
            appointment.SmsReminderJobId48h  = null;
            appointment.SmsReminderJobId2h   = null;
            appointment.EmailReminderJobId   = null;

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            // Invalidate cache for both old and new slot dates.
            if (oldSlotTime.HasValue)
            {
                var oldDateKey = $"{GetSlotsEndpoint.CacheKeyPrefix}{DateOnly.FromDateTime(oldSlotTime.Value):yyyy-MM-dd}";
                await cache.DeleteAsync(oldDateKey, ct);
            }

            var newDateKey = $"{GetSlotsEndpoint.CacheKeyPrefix}{DateOnly.FromDateTime(newSlot.SlotTime):yyyy-MM-dd}";
            await cache.DeleteAsync(newDateKey, ct);

            // Offer old slot to waitlist via SwapMonitorJob (slot stays unavailable until job runs).
            jobs.Enqueue<SwapMonitorJob>(j => j.ExecuteAsync(oldSlotId, null!));

            // Re-schedule email reminder for new slot time.
            var newReminderAt = newSlot.SlotTime.AddHours(-48);
            if (newReminderAt > DateTime.UtcNow)
            {
                var newReminderJobId = jobs.Schedule<SendReminderJob>(
                    j => j.ExecuteAsync(appointment.Id, null!),
                    newReminderAt - DateTime.UtcNow);

                appointment.ReminderJobId = newReminderJobId;
            }

            // TASK_027 — re-schedule SMS reminders for new slot time.
            var newSmsAt48h = newSlot.SlotTime.AddHours(-48);
            if (newSmsAt48h > DateTime.UtcNow)
            {
                appointment.SmsReminderJobId48h = jobs.Schedule<SendSmsReminderJob>(
                    j => j.ExecuteAsync(appointment.Id, "T-48h", null!),
                    newSmsAt48h - DateTime.UtcNow);
            }

            var newSmsAt2h = newSlot.SlotTime.AddHours(-2);
            if (newSmsAt2h > DateTime.UtcNow)
            {
                appointment.SmsReminderJobId2h = jobs.Schedule<SendSmsReminderJob>(
                    j => j.ExecuteAsync(appointment.Id, "T-2h", null!),
                    newSmsAt2h - DateTime.UtcNow);
            }

            // TASK_028 — re-schedule email reminder with new cancellation link.
            var newEmailReminderAt = newSlot.SlotTime.AddHours(-48);
            if (newEmailReminderAt > DateTime.UtcNow)
            {
                appointment.EmailReminderJobId = jobs.Schedule<SendEmailReminderJob>(
                    j => j.ExecuteAsync(appointment.Id, null!),
                    newEmailReminderAt - DateTime.UtcNow);
            }

            // Persist new job IDs outside the transaction (bounded failure: one silent skip on crash).
            if (appointment.ReminderJobId is not null ||
                appointment.SmsReminderJobId48h is not null ||
                appointment.SmsReminderJobId2h is not null ||
                appointment.EmailReminderJobId is not null)
                await db.SaveChangesAsync(ct);

            return Results.Ok(new { appointmentId = appointment.Id, newSlotId = newSlot.Id });
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            return Results.Conflict(new { error = "New slot no longer available." });
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
}
