using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Pdf;
using Hangfire;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace ClinicalHealthcare.Infrastructure.Jobs;

/// <summary>
/// Hangfire background job that sends the booking confirmation email to a patient.
///
/// AC-001 — Email delivered via MailKit SMTPS (port 465, SslOnConnect).
/// AC-002 — PDF generated in-memory by <see cref="ConfirmationPdfGenerator"/> (no temp file).
/// AC-003 — QuestPDF failure is caught; email is sent without attachment (graceful fallback).
/// AC-004 — <see cref="AutomaticRetryAttribute"/> with 3 attempts and exponential backoff.
/// AC-005 — Full Hangfire background job (not a stub).
///
/// SMTP credentials are read exclusively from environment variables:
///   SMTP_HOST, SMTP_PORT, SMTP_USER, SMTP_PASS, SMTP_FROM_ADDRESS, SMTP_FROM_NAME
/// </summary>
public sealed class SendConfirmationEmailJob
{
    private readonly ApplicationDbContext                  _db;
    private readonly ILogger<SendConfirmationEmailJob>    _logger;

    public SendConfirmationEmailJob(
        ApplicationDbContext               db,
        ILogger<SendConfirmationEmailJob>  logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <summary>
    /// Sends the booking confirmation email for the given appointment.
    /// </summary>
    /// <param name="appointmentId">The ID of the booked appointment.</param>
    /// <param name="cancellationToken">Hangfire-supplied cancellation token.</param>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 60, 120])]
    public async Task ExecuteAsync(int appointmentId, IJobCancellationToken cancellationToken)
    {
        // ── 1. Load Appointment + Patient + Slot ──────────────────────────────
        var appointment = await _db.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Slot)
            .FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken.ShutdownToken);

        if (appointment is null)
        {
            _logger.LogWarning(
                "SendConfirmationEmailJob: appointment {AppointmentId} not found — skipping.",
                appointmentId);
            return;
        }

        var patient = appointment.Patient;
        var slot    = appointment.Slot;

        if (patient is null || slot is null)
        {
            _logger.LogWarning(
                "SendConfirmationEmailJob: appointment {AppointmentId} has null Patient or Slot — skipping.",
                appointmentId);
            return;
        }

        // ── 2. Generate PDF (AC-002 / AC-003) ─────────────────────────────────
        byte[]? pdfBytes = null;
        try
        {
            var dto = new AppointmentConfirmationDto(
                AppointmentId:   appointment.Id,
                PatientFullName: $"{patient.FirstName} {patient.LastName}",
                PatientEmail:    patient.Email,
                SlotTime:        slot.SlotTime,
                DurationMinutes: slot.DurationMinutes,
                Status:          appointment.Status.ToString());

            pdfBytes = ConfirmationPdfGenerator.Generate(dto);

            _logger.LogInformation(
                "SendConfirmationEmailJob: PDF generated for appointment {AppointmentId} ({Bytes} bytes).",
                appointmentId, pdfBytes.Length);
        }
        catch (Exception ex)
        {
            // AC-003: PDF generation failure must not block email delivery.
            _logger.LogWarning(ex,
                "SendConfirmationEmailJob: QuestPDF generation failed for appointment {AppointmentId}. " +
                "Email will be sent without PDF attachment.",
                appointmentId);
        }

        // ── 3. Build MimeMessage ───────────────────────────────────────────────
        var smtpHost    = RequireEnv("SMTP_HOST");
        var smtpPort    = int.Parse(RequireEnv("SMTP_PORT"));
        var smtpUser    = RequireEnv("SMTP_USER");
        var smtpPass    = RequireEnv("SMTP_PASS");
        var fromAddress = RequireEnv("SMTP_FROM_ADDRESS");
        var fromName    = Environment.GetEnvironmentVariable("SMTP_FROM_NAME") ?? "ClinicalHub";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddress));
        message.To.Add(MailboxAddress.Parse(patient.Email));
        message.Subject = $"Your Appointment Confirmation — APT-{appointment.Id:D6}";

        var htmlBody =
            $"""
            <html><body style="font-family:Arial,sans-serif;color:#333;">
            <h2 style="color:#1a237e;">ClinicalHub — Appointment Confirmed</h2>
            <p>Dear {patient.FirstName} {patient.LastName},</p>
            <p>Your appointment has been confirmed. Details below:</p>
            <table cellpadding="6" style="border-collapse:collapse;">
              <tr><td><strong>Reference:</strong></td><td>APT-{appointment.Id:D6}</td></tr>
              <tr><td><strong>Date &amp; Time:</strong></td><td>{slot.SlotTime:dddd, dd MMMM yyyy HH:mm} UTC</td></tr>
              <tr><td><strong>Duration:</strong></td><td>{slot.DurationMinutes} minutes</td></tr>
              <tr><td><strong>Status:</strong></td><td>{appointment.Status}</td></tr>
            </table>
            <p>Please arrive 10 minutes before your appointment and bring a valid photo ID.</p>
            <p style="color:#888;font-size:12px;">This is an automated message — please do not reply.</p>
            </body></html>
            """;

        var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };

        if (pdfBytes is not null)
        {
            // AC-002 / AC-003: attach in-memory PDF — no temp file written.
            bodyBuilder.Attachments.Add(
                $"Confirmation-APT-{appointment.Id:D6}.pdf",
                pdfBytes,
                new ContentType("application", "pdf"));
        }

        message.Body = bodyBuilder.ToMessageBody();

        // ── 4. Send via SMTPS port 465 (AC-001) ───────────────────────────────
        using var smtpClient = new SmtpClient();
        await smtpClient.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.SslOnConnect,
            cancellationToken.ShutdownToken);
        await smtpClient.AuthenticateAsync(smtpUser, smtpPass, cancellationToken.ShutdownToken);
        await smtpClient.SendAsync(message, cancellationToken.ShutdownToken);
        await smtpClient.DisconnectAsync(quit: true, cancellationToken.ShutdownToken);

        _logger.LogInformation(
            "SendConfirmationEmailJob: confirmation email sent for appointment {AppointmentId} " +
            "to {Email} (PDF attached: {PdfAttached}).",
            appointmentId, patient.Email, pdfBytes is not null);
    }

    private static string RequireEnv(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException(
            $"Required environment variable '{name}' is not set.");
}

