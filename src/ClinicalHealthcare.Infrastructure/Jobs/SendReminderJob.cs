using Hangfire;
using Microsoft.Extensions.Logging;

namespace ClinicalHealthcare.Infrastructure.Jobs;

/// <summary>
/// Hangfire scheduled job that sends the T-48h appointment reminder to a patient.
///
/// This is a stub implementation. Full email/SMS delivery is in US_026/US_027.
/// </summary>
public sealed class SendReminderJob
{
    private readonly ILogger<SendReminderJob> _logger;

    public SendReminderJob(ILogger<SendReminderJob> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Sends the reminder for the given appointment.
    /// Scheduled 48 hours before the slot time by <c>BookAppointmentEndpoint</c>.
    /// </summary>
    /// <param name="appointmentId">The ID of the appointment to remind about.</param>
    /// <param name="cancellationToken">Hangfire-supplied cancellation token.</param>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 60, 120])]
    public Task ExecuteAsync(int appointmentId, IJobCancellationToken cancellationToken)
    {
        // Stub — full email/SMS implementation in US_026/US_027.
        _logger.LogInformation(
            "SendReminderJob: appointment {AppointmentId} — T-48h reminder scheduled (stub).",
            appointmentId);

        return Task.CompletedTask;
    }
}
