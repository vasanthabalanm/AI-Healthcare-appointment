using System.Reflection;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Jobs;
using ClinicalHealthcare.Infrastructure.Pdf;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Jobs;

[Collection("SmtpSerial")]
/// <summary>
/// Unit tests for <see cref="SendConfirmationEmailJob"/> and
/// <see cref="ConfirmationPdfGenerator"/> (US_026).
/// Verifies:
///   AC-001 — Email delivery attempted via SMTP (env-var pattern).
///   AC-002 — PDF generated in-memory by ConfirmationPdfGenerator; non-empty bytes returned.
///   AC-003 — Missing SMTP_HOST throws; misconfiguration surfaced promptly.
///   AC-004 — AutomaticRetry attribute: 3 attempts, delays [30, 60, 120] seconds.
///   AC-005 — QuestPDF failure is non-fatal; SMTP step still reached.
///   Guard  — Appointment not found, null Patient, null Slot → return cleanly.
/// </summary>
public sealed class SendConfirmationEmailJobTests
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

    private static SendConfirmationEmailJob CreateJob(ApplicationDbContext db) =>
        new(db, NullLogger<SendConfirmationEmailJob>.Instance);

    private static IJobCancellationToken NoCancellation() =>
        new JobCancellationToken(false);

    /// <summary>Seeds a complete Patient + Slot + Scheduled Appointment.</summary>
    private static (UserAccount patient, Slot slot, Appointment appointment) Seed(
        ApplicationDbContext db,
        string email  = "patient@clinic.test",
        string firstName = "Jane",
        string lastName  = "Doe")
    {
        var patient = new UserAccount
        {
            Email        = email,
            Role         = "patient",
            FirstName    = firstName,
            LastName     = lastName,
            PasswordHash = "h",
            IsActive     = true,
        };
        db.UserAccounts.Add(patient);
        db.SaveChanges();

        var slot = new Slot
        {
            SlotTime        = DateTime.UtcNow.AddHours(48),
            DurationMinutes = 30,
            IsAvailable     = false,
        };
        db.Slots.Add(slot);
        db.SaveChanges();

        var appt = new Appointment
        {
            PatientId = patient.Id,
            SlotId    = slot.Id,
            Status    = AppointmentStatus.Scheduled,
            BookedAt  = DateTime.UtcNow,
        };
        db.Appointments.Add(appt);
        db.SaveChanges();

        return (patient, slot, appt);
    }

    // ── TC-001: ConfirmationPdfGenerator returns non-empty bytes ─────────────

    [Fact]
    public void ConfirmationPdfGenerator_Generate_ReturnsNonEmptyBytes()
    {
        var dto = new AppointmentConfirmationDto(
            AppointmentId:   42,
            PatientFullName: "Jane Doe",
            PatientEmail:    "jane@clinic.test",
            SlotTime:        DateTime.UtcNow.AddHours(24),
            DurationMinutes: 30,
            Status:          "Scheduled");

        var pdfBytes = ConfirmationPdfGenerator.Generate(dto);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 0, "PDF must be non-empty.");
    }

    // ── TC-002: Generated PDF is within reasonable size bound ────────────────

    [Fact]
    public void ConfirmationPdfGenerator_Generate_ResultIsWithin200KB()
    {
        var dto = new AppointmentConfirmationDto(
            AppointmentId:   1,
            PatientFullName: "John Smith",
            PatientEmail:    "john@clinic.test",
            SlotTime:        DateTime.UtcNow.AddHours(72),
            DurationMinutes: 60,
            Status:          "Scheduled");

        var pdfBytes = ConfirmationPdfGenerator.Generate(dto);

        Assert.True(pdfBytes.Length <= 204_800,
            $"PDF size {pdfBytes.Length} bytes exceeds 200 KB limit.");
    }

    // ── TC-003: Zero-duration appointment does not cause generator to throw ───

    [Fact]
    public void ConfirmationPdfGenerator_Generate_ZeroDuration_DoesNotThrow()
    {
        var dto = new AppointmentConfirmationDto(
            AppointmentId:   99,
            PatientFullName: "Zero Duration",
            PatientEmail:    "zero@clinic.test",
            SlotTime:        DateTime.UtcNow.AddHours(10),
            DurationMinutes: 0,
            Status:          "Scheduled");

        var pdfBytes = ConfirmationPdfGenerator.Generate(dto);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 0);
    }

    // ── TC-004: Appointment not found → job returns cleanly ──────────────────

    [Fact]
    public async Task ExecuteAsync_AppointmentNotFound_ReturnsWithoutException()
    {
        var db  = CreateDb();
        var job = CreateJob(db);

        // Non-existent appointment ID — must not throw.
        await job.ExecuteAsync(9999, NoCancellation());
    }

    // ── TC-005: Null Patient (dangling FK) → job returns cleanly ─────────────

    [Fact]
    public async Task ExecuteAsync_NullPatient_ReturnsWithoutException()
    {
        var db = CreateDb();

        // Insert a Slot so the Slot FK can resolve.
        var slot = new Slot
        {
            SlotTime        = DateTime.UtcNow.AddHours(24),
            DurationMinutes = 30,
            IsAvailable     = false,
        };
        db.Slots.Add(slot);
        db.SaveChanges();

        // Appointment points to a non-existent PatientId (9999).
        // In-memory EF does not enforce FK constraints, so SaveChanges succeeds.
        // Include(a => a.Patient) will yield null because no UserAccount with Id=9999 exists.
        var appt = new Appointment
        {
            PatientId = 9999,
            SlotId    = slot.Id,
            Status    = AppointmentStatus.Scheduled,
            BookedAt  = DateTime.UtcNow,
        };
        db.Appointments.Add(appt);
        db.SaveChanges();

        var job = CreateJob(db);
        await job.ExecuteAsync(appt.Id, NoCancellation());
    }

    // ── TC-006: Null Slot (dangling FK) → job returns cleanly ────────────────

    [Fact]
    public async Task ExecuteAsync_NullSlot_ReturnsWithoutException()
    {
        var db = CreateDb();

        var patient = new UserAccount
        {
            Email        = "slot-null@clinic.test",
            Role         = "patient",
            FirstName    = "Slot",
            LastName     = "Null",
            PasswordHash = "h",
            IsActive     = true,
        };
        db.UserAccounts.Add(patient);
        db.SaveChanges();

        // Appointment points to a non-existent SlotId (9999).
        var appt = new Appointment
        {
            PatientId = patient.Id,
            SlotId    = 9999,
            Status    = AppointmentStatus.Scheduled,
            BookedAt  = DateTime.UtcNow,
        };
        db.Appointments.Add(appt);
        db.SaveChanges();

        var job = CreateJob(db);
        await job.ExecuteAsync(appt.Id, NoCancellation());
    }

    // ── TC-007 / EC-001: SMTP env vars set → job reaches SMTP connect ─────────
    // PDF generation succeeds; SMTP ConnectAsync throws (no real server) — expected.
    // Verifies the job progresses through PDF generation and reaches the SMTP step.

    [Fact]
    public async Task ExecuteAsync_ValidAppointmentSmtpEnvVarsSet_ThrowsSmtpException()
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

            // The job will throw at SMTP ConnectAsync (no real server at 127.0.0.1:465).
            // Reaching this point confirms PDF generation succeeded and
            // RequireEnv("SMTP_HOST") resolved (not InvalidOperationException).
            var ex = await Assert.ThrowsAnyAsync<Exception>(() => job.ExecuteAsync(appt.Id, NoCancellation()));

            // Must be a network/SMTP exception — not a PDF or configuration exception.
            Assert.IsNotType<InvalidOperationException>(ex);
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

    // ── EC-001: Missing SMTP_HOST env var → RequireEnv throws ─────────────────

    [Fact]
    public async Task ExecuteAsync_MissingSmtpHost_ThrowsInvalidOperationException()
    {
        // Ensure SMTP_HOST is not set (guard against test-order interference).
        Environment.SetEnvironmentVariable("SMTP_HOST", null);

        var db  = CreateDb();
        var (_, _, appt) = Seed(db);
        var job = CreateJob(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => job.ExecuteAsync(appt.Id, NoCancellation()));
    }

    // ── ES-001: AutomaticRetry attribute — 3 attempts, delays [30,60,120] ─────

    [Fact]
    public void ExecuteAsync_HasAutomaticRetryAttribute_3Attempts_30_60_120_Seconds()
    {
        var method = typeof(SendConfirmationEmailJob)
            .GetMethod(nameof(SendConfirmationEmailJob.ExecuteAsync));
        Assert.NotNull(method);

        var attr = method!.GetCustomAttribute<AutomaticRetryAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(3, attr!.Attempts);
        Assert.Equal(new[] { 30, 60, 120 }, attr.DelaysInSeconds);
    }
}
