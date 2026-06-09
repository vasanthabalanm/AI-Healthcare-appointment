using System.Security.Cryptography;
using System.Text;
using ClinicalHealthcare.Infrastructure.Data;
using Hangfire;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace ClinicalHealthcare.Infrastructure.Jobs;

/// <summary>
/// Hangfire scheduled job that sends the T-48h appointment reminder email with a
/// single-use cancellation link (TASK_028).
///
/// AC-001 — Email sent via MailKit SMTPS (port 465, SslOnConnect).
/// AC-002 — Email body includes a cancellation link valid for 48 hours.
/// AC-003 — Token stored as SHA-256 hash; marked CancellationLinkUsed=true on first use.
///
/// Token lifecycle:
///   1. Generate 32 cryptographically-random bytes → Base64URL-encode → raw token.
///   2. SHA-256 hash the raw token → store hash + expiry on Appointment.
///   3. Embed raw token in cancellation URL (never persisted in DB).
///   4. CancelByLinkEndpoint hashes the incoming token and looks up by hash.
///
/// SMTP credentials from env vars: SMTP_HOST, SMTP_PORT, SMTP_USER, SMTP_PASS,
///   SMTP_FROM_ADDRESS, SMTP_FROM_NAME.
/// Base URL for cancellation link from env var: APP_BASE_URL
///   (default: "http://localhost:5153").
/// </summary>
public sealed class SendEmailReminderJob
{
    private readonly ApplicationDbContext              _db;
    private readonly ILogger<SendEmailReminderJob>     _logger;

    public SendEmailReminderJob(
        ApplicationDbContext            db,
        ILogger<SendEmailReminderJob>   logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <summary>
    /// Sends the T-48h reminder email for the given appointment.
    /// </summary>
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
                "SendEmailReminderJob: appointment {AppointmentId} not found — skipping.",
                appointmentId);
            return;
        }

        if (appointment.Status != Entities.AppointmentStatus.Scheduled)
        {
            _logger.LogInformation(
                "SendEmailReminderJob: appointment {AppointmentId} has status {Status} — skipping.",
                appointmentId, appointment.Status);
            return;
        }

        var patient = appointment.Patient;
        var slot    = appointment.Slot;

        if (patient is null || slot is null)
        {
            _logger.LogWarning(
                "SendEmailReminderJob: appointment {AppointmentId} has null Patient or Slot — skipping.",
                appointmentId);
            return;
        }

        // ── 2. Validate patient email early — fail fast before token generation ─
        if (string.IsNullOrWhiteSpace(patient.Email) ||
            !MailboxAddress.TryParse(patient.Email, out var toAddress))
        {
            _logger.LogWarning(
                "SendEmailReminderJob: patient {PatientId} has invalid email '{Email}' — skipping.",
                patient.Id, patient.Email);
            return;
        }

        // ── 3. Idempotent guard — skip if email was already delivered ─────────
        // Protects against Hangfire retrying after a successful send (e.g., post-send crash).
        // If SMTP succeeded on a prior attempt, EmailReminderSentAt is non-null and we stop.
        if (appointment.EmailReminderSentAt is not null)
        {
            _logger.LogInformation(
                "SendEmailReminderJob: appointment {AppointmentId} — reminder already sent; skipping.",
                appointmentId);
            return;
        }

        // ── 3. Generate single-use cancellation token (AC-002 / AC-003) ───────
        // Safe to generate a fresh token here: if we reach this point no email has been
        // delivered, so there is no prior link in the patient's inbox to invalidate.
        // 32 bytes = 256 bits of entropy — OWASP minimum for session tokens.
        var rawBytes  = RandomNumberGenerator.GetBytes(32);
        var rawToken  = Base64UrlEncode(rawBytes);
        var tokenHash = ComputeSha256Hex(rawToken);

        appointment.CancellationLinkTokenHash = tokenHash;
        appointment.CancellationLinkExpiry    = DateTime.UtcNow.AddHours(48);
        appointment.CancellationLinkUsed      = false;

        // Persist token BEFORE sending so the link is valid even if the SMTP call fails.
        await _db.SaveChangesAsync(cancellationToken.ShutdownToken);

        // ── 3. Build cancellation URL ─────────────────────────────────────────
        var baseUrl = Environment.GetEnvironmentVariable("APP_BASE_URL")?.TrimEnd('/')
                   ?? "http://localhost:5153";
        var cancelUrl = $"{baseUrl}/appointments/cancel?token={rawToken}";

        // ── 4. Build MimeMessage (AC-001) ─────────────────────────────────────
        var smtpHost    = RequireEnv("SMTP_HOST");
        var smtpPort    = int.Parse(RequireEnv("SMTP_PORT"));
        var smtpUser    = RequireEnv("SMTP_USER");
        var smtpPass    = RequireEnv("SMTP_PASS");
        var fromAddress = RequireEnv("SMTP_FROM_ADDRESS");
        var fromName    = Environment.GetEnvironmentVariable("SMTP_FROM_NAME") ?? "ClinicalHub";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddress));
        message.To.Add(toAddress);
        message.Subject = $"Reminder: Your Appointment on {slot.SlotTime:yyyy-MM-dd} — APT-{appointment.Id:D6}";

        var htmlBody =
            $"""
            <html><body style="font-family:Arial,sans-serif;color:#333;">
            <h2 style="color:#1a237e;">ClinicalHub — Appointment Reminder</h2>
            <p>Dear {patient.FirstName} {patient.LastName},</p>
            <p>This is a reminder for your upcoming appointment:</p>
            <table cellpadding="6" style="border-collapse:collapse;">
              <tr><td><strong>Reference:</strong></td><td>APT-{appointment.Id:D6}</td></tr>
              <tr><td><strong>Date:</strong></td><td>{slot.SlotTime:yyyy-MM-dd}</td></tr>
              <tr><td><strong>Time:</strong></td><td>{slot.SlotTime:HH:mm} UTC</td></tr>
              <tr><td><strong>Duration:</strong></td><td>{slot.DurationMinutes} minutes</td></tr>
            </table>
            <p>Please arrive 10 minutes before your appointment and bring a valid photo ID.</p>
            <hr style="margin:24px 0;"/>
            <p>Need to cancel? Use the link below — it is valid for 48 hours and can only be used once:</p>
            <p><a href="{cancelUrl}" style="color:#1a237e;">Cancel this appointment</a></p>
            <p style="color:#888;font-size:11px;">
              If you did not request this reminder, please ignore this message.<br/>
              This is an automated message — please do not reply.
            </p>
            </body></html>
            """;

        var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
        message.Body = bodyBuilder.ToMessageBody();

        // ── 5. Send via SMTPS port 465 or STARTTLS port 587 (AC-001) ─────────
        // Select SecureSocketOptions based on port: 587 → STARTTLS, anything else → SslOnConnect.
        var secureOption = smtpPort == 587
            ? SecureSocketOptions.StartTls
            : SecureSocketOptions.SslOnConnect;

        using var smtpClient = new SmtpClient();
        await smtpClient.ConnectAsync(smtpHost, smtpPort, secureOption,
            cancellationToken.ShutdownToken);
        await smtpClient.AuthenticateAsync(smtpUser, smtpPass, cancellationToken.ShutdownToken);
        await smtpClient.SendAsync(message, cancellationToken.ShutdownToken);
        await smtpClient.DisconnectAsync(quit: true, cancellationToken.ShutdownToken);

        // Mark sent AFTER successful SMTP delivery — idempotence sentinel for retries.
        appointment.EmailReminderSentAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken.ShutdownToken);

        _logger.LogInformation(
            "SendEmailReminderJob: reminder email sent for appointment {AppointmentId} to {Email}.",
            appointmentId, patient.Email);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Encodes bytes as Base64URL (no padding, URL-safe).</summary>
    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
               .TrimEnd('=')
               .Replace('+', '-')
               .Replace('/', '_');

    /// <summary>Returns lowercase hex of the SHA-256 hash of <paramref name="input"/>.</summary>
    public static string ComputeSha256Hex(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string RequireEnv(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException(
            $"Required environment variable '{name}' is not set.");
}
