using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Configuration;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Jobs;
using ClinicalHealthcare.Infrastructure.Services;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicalHealthcare.Api.Features.Appointments;

/// <summary>
/// Vertical-slice endpoint: POST /appointments
///
/// Books an available slot for the authenticated patient.
///
/// Concurrency safety (AC-003):
///   The <see cref="Slot.RowVersion"/> timestamp column is an EF Core optimistic-concurrency token.
///   When two patients attempt to book the same slot simultaneously, exactly one UPDATE wins;
///   the other receives a <see cref="DbUpdateConcurrencyException"/> which is caught and
///   returned as HTTP 409.
///
/// Post-booking side-effects (AC-004 / AC-005):
///   - Slot cache entry invalidated via <see cref="ICacheService.DeleteAsync"/>.
///   - Confirmation email enqueued as a fire-and-forget Hangfire job.
///   - T-48h reminder scheduled as a delayed Hangfire job.
/// </summary>
public sealed class BookAppointmentEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // PatientOnly policy — registered by GetSlotsEndpoint first; guard prevents double-registration.
        services.AddAuthorization(options =>
        {
            if (options.GetPolicy("PatientOnly") is null)
                options.AddPolicy("PatientOnly", p => p.RequireRole("patient"));
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/appointments", HandleBookAppointment)
           .RequireAuthorization("PatientOnly")
           .WithName("BookAppointment")
           .WithTags("Appointments")
           .Produces(StatusCodes.Status201Created)
           .Produces(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status409Conflict)
           .Produces(StatusCodes.Status422UnprocessableEntity);
    }

    // ── POST /appointments ────────────────────────────────────────────────────

    public static async Task<IResult> HandleBookAppointment(
        BookAppointmentRequest   request,
        HttpContext               httpContext,
        ApplicationDbContext      db,
        ICacheService             cache,
        IBackgroundJobClient      jobs,
        INoShowRiskScoreService   riskService,
        IOptions<AppSettings>     appSettings,
        CancellationToken         ct)
    {
        // Extract authenticated patient's user ID from JWT sub claim.
        var subClaim = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                    ?? httpContext.User.FindFirst("sub")?.Value;

        if (!int.TryParse(subClaim, out var patientId))
            return Results.Unauthorized();

        // Basic input validation.
        if (request.SlotId <= 0)
            return Results.UnprocessableEntity(new { error = "SlotId must be a positive integer." });

        // Load the slot — rowversion is included automatically via [Timestamp] attribute.
        var slot = await db.Slots.FirstOrDefaultAsync(s => s.Id == request.SlotId, ct);

        if (slot is null)
            return Results.BadRequest(new { error = "Slot not found." });

        // Edge case: slot already unavailable — fail fast before rowversion check.
        if (!slot.IsAvailable)
            return Results.Conflict(new { error = "Slot no longer available." });

        // Edge case: booking a past slot.
        if (slot.SlotTime <= DateTime.UtcNow)
            return Results.BadRequest(new { error = "Slot date must be in the future." });

        // AC-005 — prevent a patient from holding two active future appointments.
        // Past appointments that were never finalised (still Scheduled) must not block new bookings.
        var hasActive = await db.Appointments
            .AnyAsync(a => a.PatientId == patientId
                        && a.Status == AppointmentStatus.Scheduled
                        && a.Slot!.SlotTime > DateTime.UtcNow, ct);

        if (hasActive)
            return Results.Conflict(new { error = "Cancel existing appointment first." });

        // AC-002 + AC-003 — atomic booking with optimistic concurrency.
        slot.IsAvailable = false;

        // AC-001/AC-002 — calculate no-show risk score before persisting (score is immutable after booking).
        var riskScore = await riskService.CalculateAsync(patientId, slot.SlotTime, ct);

        var appointment = new Appointment
        {
            PatientId        = patientId,
            SlotId           = slot.Id,
            Status           = AppointmentStatus.Scheduled,
            BookedAt         = DateTime.UtcNow,
            NoShowRiskScore  = riskScore,
            IsHighRisk       = riskScore >= appSettings.Value.NoShowRiskThreshold,
        };
        db.Appointments.Add(appointment);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // AC-003 — rowversion mismatch: another request booked this slot first.
            return Results.Conflict(new { error = "Slot no longer available." });
        }

        // AC-004 — invalidate slot cache for the booking date so next GET hits DB.
        var dateKey = $"{GetSlotsEndpoint.CacheKeyPrefix}{DateOnly.FromDateTime(slot.SlotTime):yyyy-MM-dd}";
        await cache.DeleteAsync(dateKey, ct);

        // AC-005 — enqueue confirmation email (fire-and-forget).
        jobs.Enqueue<SendConfirmationEmailJob>(j => j.ExecuteAsync(appointment.Id, null!));

        // AC-005 — schedule T-48h reminder; persist job ID so cancel/reschedule can delete it.
        var reminderAt = slot.SlotTime.AddHours(-48);
        if (reminderAt > DateTime.UtcNow)
        {
            var reminderJobId = jobs.Schedule<SendReminderJob>(
                j => j.ExecuteAsync(appointment.Id, null!),
                reminderAt - DateTime.UtcNow);

            appointment.ReminderJobId = reminderJobId;
            await db.SaveChangesAsync(ct); // persist job ID — second write is intentional
        }

        // TASK_028 — schedule T-48h email reminder with cancellation link (AC-001/AC-002).
        var emailReminderAt = slot.SlotTime.AddHours(-48);
        if (emailReminderAt > DateTime.UtcNow)
        {
            var emailReminderJobId = jobs.Schedule<SendEmailReminderJob>(
                j => j.ExecuteAsync(appointment.Id, null!),
                emailReminderAt - DateTime.UtcNow);

            appointment.EmailReminderJobId = emailReminderJobId;
        }

        // TASK_027 — schedule T-48h and T-2h SMS reminders; store IDs for cancellation (AC-005).
        var smsAt48h = slot.SlotTime.AddHours(-48);
        if (smsAt48h > DateTime.UtcNow)
        {
            var smsJobId48h = jobs.Schedule<SendSmsReminderJob>(
                j => j.ExecuteAsync(appointment.Id, "T-48h", null!),
                smsAt48h - DateTime.UtcNow);

            appointment.SmsReminderJobId48h = smsJobId48h;
        }

        var smsAt2h = slot.SlotTime.AddHours(-2);
        if (smsAt2h > DateTime.UtcNow)
        {
            var smsJobId2h = jobs.Schedule<SendSmsReminderJob>(
                j => j.ExecuteAsync(appointment.Id, "T-2h", null!),
                smsAt2h - DateTime.UtcNow);

            appointment.SmsReminderJobId2h = smsJobId2h;
        }

        if (appointment.SmsReminderJobId48h is not null ||
            appointment.SmsReminderJobId2h  is not null ||
            appointment.EmailReminderJobId  is not null)
            await db.SaveChangesAsync(ct); // persist reminder job IDs

        return Results.Created($"/appointments/{appointment.Id}", new { appointmentId = appointment.Id });
    }
}
