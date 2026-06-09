using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Sms;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicalHealthcare.Infrastructure.Jobs;

/// <summary>
/// Hangfire scheduled job that sends an SMS appointment reminder to a patient (AC-001).
///
/// AC-001 — Triggered at T-48h and T-2h before the slot time via Hangfire scheduler.
/// AC-002 — Phone number normalized to E.164 by <see cref="PhoneNormalizer"/> before sending.
/// AC-003 — Patient with no phone number or invalid phone → logs WARNING, returns cleanly (no throw).
/// AC-004 — Sends via <see cref="ISmsGateway"/> abstraction; Twilio sandbox in use for testing.
/// </summary>
public sealed class SendSmsReminderJob
{
    private readonly ApplicationDbContext              _db;
    private readonly ISmsGateway                       _sms;
    private readonly ILogger<SendSmsReminderJob>       _logger;

    public SendSmsReminderJob(
        ApplicationDbContext            db,
        ISmsGateway                     sms,
        ILogger<SendSmsReminderJob>     logger)
    {
        _db     = db;
        _sms    = sms;
        _logger = logger;
    }

    /// <summary>
    /// Sends an SMS reminder for the given appointment.
    /// </summary>
    /// <param name="appointmentId">The ID of the appointment.</param>
    /// <param name="reminderLabel">Human-readable label used in the SMS body (e.g. "T-48h", "T-2h").</param>
    /// <param name="cancellationToken">Hangfire-supplied cancellation token.</param>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 60, 120])]
    public async Task ExecuteAsync(
        int                    appointmentId,
        string                 reminderLabel,
        IJobCancellationToken  cancellationToken)
    {
        // ── 1. Load Appointment + Patient + Slot ──────────────────────────────
        var appointment = await _db.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Slot)
            .FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken.ShutdownToken);

        if (appointment is null)
        {
            _logger.LogWarning(
                "SendSmsReminderJob [{Label}]: appointment {AppointmentId} not found — skipping.",
                reminderLabel, appointmentId);
            return;
        }

        // ── 2. Skip silently if appointment is no longer Scheduled (AC-003) ──
        if (appointment.Status != Entities.AppointmentStatus.Scheduled)
        {
            _logger.LogInformation(
                "SendSmsReminderJob [{Label}]: appointment {AppointmentId} has status {Status} — skipping.",
                reminderLabel, appointmentId, appointment.Status);
            return;
        }

        var patient = appointment.Patient;
        var slot    = appointment.Slot;

        if (patient is null || slot is null)
        {
            _logger.LogWarning(
                "SendSmsReminderJob [{Label}]: appointment {AppointmentId} has null Patient or Slot — skipping.",
                reminderLabel, appointmentId);
            return;
        }

        // ── 3. Normalize phone to E.164 (AC-002 / AC-003) ────────────────────
        var e164 = PhoneNormalizer.ToE164(patient.PhoneNumber);

        if (e164 is null)
        {
            _logger.LogWarning(
                "SendSmsReminderJob [{Label}]: patient {PatientId} has no valid phone number " +
                "(raw: '{RawPhone}') — skipping SMS.",
                reminderLabel, patient.Id, patient.PhoneNumber ?? "(null)");
            return;
        }

        // ── 4. Build and send SMS (AC-001 / AC-004) ───────────────────────────
        // ISO date format (fixed width) prevents multi-part SMS for long locale day/month names.
        var body =
            $"ClinicalHub reminder ({reminderLabel}): Your appointment is on " +
            $"{slot.SlotTime:yyyy-MM-dd} at {slot.SlotTime:HH:mm} UTC. " +
            $"Ref: APT-{appointment.Id:D6}. Please arrive 10 minutes early.";

        await _sms.SendAsync(e164, body, cancellationToken.ShutdownToken);

        _logger.LogInformation(
            "SendSmsReminderJob [{Label}]: SMS sent to patient {PatientId} for appointment {AppointmentId}.",
            reminderLabel, patient.Id, appointmentId);
    }
}
