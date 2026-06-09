using System.Reflection;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Jobs;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Jobs;

/// <summary>
/// Unit tests for <see cref="SendEmailReminderJob"/>.
/// Verifies:
///   AC-002 — cancellation token hash + expiry persisted before SMTP send.
///   AC-003 — idempotent guard: already-sent appointments are skipped.
///   Guard paths — skips gracefully for missing appointment, cancelled status,
///                 null patient/slot, and invalid patient email.
/// </summary>
[Collection("SmtpSerial")]
public sealed class SendEmailReminderJobTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(opts);
    }

    private static SendEmailReminderJob CreateJob(ApplicationDbContext db) =>
        new(db, NullLogger<SendEmailReminderJob>.Instance);

    private static IJobCancellationToken NoCancellation() =>
        new JobCancellationToken(false);

    private static (UserAccount patient, Slot slot, Appointment appointment) Seed(
        ApplicationDbContext db,
        string            email      = "patient@example.com",
        AppointmentStatus status     = AppointmentStatus.Scheduled,
        DateTime?         sentAt     = null)
    {
        var patient = new UserAccount
        {
            Email        = email,
            Role         = "patient",
            FirstName    = "Jane",
            LastName     = "Doe",
            PasswordHash = "h",
            IsActive     = true,
        };
        db.UserAccounts.Add(patient);
        db.SaveChanges();

        var slot = new Slot
        {
            SlotTime        = DateTime.UtcNow.AddHours(50),
            DurationMinutes = 30,
            IsAvailable     = false,
        };
        db.Slots.Add(slot);
        db.SaveChanges();

        var appt = new Appointment
        {
            PatientId           = patient.Id,
            SlotId              = slot.Id,
            Status              = status,
            BookedAt            = DateTime.UtcNow,
            EmailReminderSentAt = sentAt,
        };
        db.Appointments.Add(appt);
        db.SaveChanges();

        return (patient, slot, appt);
    }

    // ── Guard: appointment not found ──────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_AppointmentNotFound_CompletesWithoutException()
    {
        var db  = CreateDb();
        var job = CreateJob(db);

        // Non-existent ID; must not throw.
        await job.ExecuteAsync(9999, NoCancellation());
    }

    // ── Guard: non-Scheduled status ───────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CancelledAppointment_SkipsWithoutWritingToken()
    {
        var db  = CreateDb();
        var (_, _, appt) = Seed(db, status: AppointmentStatus.Cancelled);
        var job = CreateJob(db);

        await job.ExecuteAsync(appt.Id, NoCancellation());

        var reloaded = await db.Appointments.FindAsync(appt.Id);
        Assert.Null(reloaded!.CancellationLinkTokenHash);
    }

    // ── Guard: invalid patient email ──────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_InvalidPatientEmail_SkipsWithoutException()
    {
        var db  = CreateDb();
        // Empty string is guaranteed to fail MailboxAddress.TryParse — MimeKit accepts
        // bare local parts, so we need a truly unparseable value here.
        var (_, _, appt) = Seed(db, email: "");
        var job = CreateJob(db);

        // Guard path: MailboxAddress.TryParse fails; job must return gracefully without throwing.
        await job.ExecuteAsync(appt.Id, NoCancellation());
    }

    // ── AC-002: token hash and expiry persisted before SMTP ───────────────────

    [Fact]
    public async Task ExecuteAsync_ValidAppointment_PersistsTokenHashBeforeSmtp()
    {
        // Arrange: configure env vars so SMTP step can be reached.
        // The job will throw on ConnectAsync (no real SMTP server), but the token
        // should already be in the DB by that point.
        Environment.SetEnvironmentVariable("SMTP_HOST", "127.0.0.1");
        Environment.SetEnvironmentVariable("SMTP_PORT", "465");
        Environment.SetEnvironmentVariable("SMTP_USER", "user");
        Environment.SetEnvironmentVariable("SMTP_PASS", "pass");
        Environment.SetEnvironmentVariable("SMTP_FROM_ADDRESS", "noreply@clinic.test");

        try
        {
            var db  = CreateDb();
            var (_, _, appt) = Seed(db);
            var job = CreateJob(db);

            // Act: job will fail at SMTP connect — catch expected network/auth exception.
            await Assert.ThrowsAnyAsync<Exception>(() => job.ExecuteAsync(appt.Id, NoCancellation()));

            // Assert: token hash and expiry were persisted before the SMTP attempt.
            var reloaded = await db.Appointments.FindAsync(appt.Id);
            Assert.NotNull(reloaded!.CancellationLinkTokenHash);
            Assert.NotNull(reloaded.CancellationLinkExpiry);
            Assert.True(reloaded.CancellationLinkExpiry > DateTime.UtcNow);
            Assert.False(reloaded.CancellationLinkUsed);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SMTP_HOST", null);
            Environment.SetEnvironmentVariable("SMTP_PORT", null);
            Environment.SetEnvironmentVariable("SMTP_USER", null);
            Environment.SetEnvironmentVariable("SMTP_PASS", null);
            Environment.SetEnvironmentVariable("SMTP_FROM_ADDRESS", null);
        }
    }

    // ── AC-003 idempotence: already-sent appointments are skipped ─────────────

    [Fact]
    public async Task ExecuteAsync_AlreadySent_SkipsWithoutOverwritingToken()
    {
        var db  = CreateDb();
        var originalSentAt = DateTime.UtcNow.AddMinutes(-5);
        var (_, _, appt) = Seed(db, sentAt: originalSentAt);

        // Pre-seed a fake token hash so we can verify it is not overwritten.
        appt.CancellationLinkTokenHash = "deadbeef";
        await db.SaveChangesAsync();

        var job = CreateJob(db);
        await job.ExecuteAsync(appt.Id, NoCancellation());

        var reloaded = await db.Appointments.FindAsync(appt.Id);
        // Token must not have been replaced and sent-at must remain unchanged.
        Assert.Equal("deadbeef", reloaded!.CancellationLinkTokenHash);
        Assert.Equal(originalSentAt, reloaded.EmailReminderSentAt);
    }

    // ── TC-002: CancellationLinkExpiry is approximately 48 hours from now ─────

    [Fact]
    public async Task ExecuteAsync_ValidAppointment_CancellationLinkExpiryIsApproximately48Hours()
    {
        Environment.SetEnvironmentVariable("SMTP_HOST",         "127.0.0.1");
        Environment.SetEnvironmentVariable("SMTP_PORT",         "465");
        Environment.SetEnvironmentVariable("SMTP_USER",         "user");
        Environment.SetEnvironmentVariable("SMTP_PASS",         "pass");
        Environment.SetEnvironmentVariable("SMTP_FROM_ADDRESS", "noreply@clinic.test");

        try
        {
            var db  = CreateDb();
            var (_, _, appt) = Seed(db);
            var job = CreateJob(db);

            var before = DateTime.UtcNow.AddHours(47);
            var after  = DateTime.UtcNow.AddHours(49);

            await Assert.ThrowsAnyAsync<Exception>(() => job.ExecuteAsync(appt.Id, NoCancellation()));

            var reloaded = await db.Appointments.FindAsync(appt.Id);
            Assert.NotNull(reloaded!.CancellationLinkExpiry);
            Assert.True(reloaded.CancellationLinkExpiry >= before,
                $"Expiry {reloaded.CancellationLinkExpiry} is before expected lower bound {before}.");
            Assert.True(reloaded.CancellationLinkExpiry <= after,
                $"Expiry {reloaded.CancellationLinkExpiry} is after expected upper bound {after}.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SMTP_HOST",         null);
            Environment.SetEnvironmentVariable("SMTP_PORT",         null);
            Environment.SetEnvironmentVariable("SMTP_USER",         null);
            Environment.SetEnvironmentVariable("SMTP_PASS",         null);
            Environment.SetEnvironmentVariable("SMTP_FROM_ADDRESS", null);
        }
    }

    // ── TC-003 / TC-004: ComputeSha256Hex helper ──────────────────────────────

    [Fact]
    public void ComputeSha256Hex_SameInput_ReturnsDeterministicLowercaseHex()
    {
        const string input = "test-cancellation-token-abc123";

        var result1 = SendEmailReminderJob.ComputeSha256Hex(input);
        var result2 = SendEmailReminderJob.ComputeSha256Hex(input);

        Assert.Equal(result1, result2);
        Assert.Equal(64, result1.Length);
        Assert.Matches("^[0-9a-f]{64}$", result1);
    }

    [Fact]
    public void ComputeSha256Hex_EmptyInput_ReturnsKnownVector()
    {
        // SHA-256("") is the well-known empty-string digest per NIST FIPS 180-4.
        const string expected = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        var result = SendEmailReminderJob.ComputeSha256Hex(string.Empty);

        Assert.Equal(expected, result);
    }

    // ── ES-001: AutomaticRetry attribute — 3 attempts, delays [30,60,120] ─────

    [Fact]
    public void ExecuteAsync_HasAutomaticRetryAttribute_3Attempts_30_60_120_Seconds()
    {
        var method = typeof(SendEmailReminderJob)
            .GetMethod(nameof(SendEmailReminderJob.ExecuteAsync));
        Assert.NotNull(method);

        var attr = method!.GetCustomAttribute<AutomaticRetryAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(3, attr!.Attempts);
        Assert.Equal(new[] { 30, 60, 120 }, attr.DelaysInSeconds);
    }
}
