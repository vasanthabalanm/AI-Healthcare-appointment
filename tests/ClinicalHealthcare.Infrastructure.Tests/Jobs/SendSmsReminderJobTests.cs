using System.Reflection;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Jobs;
using ClinicalHealthcare.Infrastructure.Sms;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Jobs;

/// <summary>
/// Unit tests for <see cref="SendSmsReminderJob"/>.
/// Verifies AC-001 (SMS sent), AC-003 (graceful skip for no/invalid phone),
/// status guard (cancelled appointment skipped), and null-entity guards.
/// </summary>
public sealed class SendSmsReminderJobTests
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

    private static SendSmsReminderJob CreateJob(
        ApplicationDbContext  db,
        Mock<ISmsGateway>?    smsMock = null)
    {
        return new SendSmsReminderJob(
            db,
            (smsMock ?? new Mock<ISmsGateway>()).Object,
            NullLogger<SendSmsReminderJob>.Instance);
    }

    private static IJobCancellationToken NoCancellation() =>
        new JobCancellationToken(false);

    private static (UserAccount patient, Slot slot, Appointment appointment) Seed(
        ApplicationDbContext db,
        string?              phoneNumber = "+14155552671",
        AppointmentStatus    status      = AppointmentStatus.Scheduled)
    {
        var patient = new UserAccount
        {
            Email        = "patient@test.com",
            Role         = "patient",
            FirstName    = "Jane",
            LastName     = "Doe",
            PasswordHash = "h",
            IsActive     = true,
            PhoneNumber  = phoneNumber,
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
            PatientId = patient.Id,
            SlotId    = slot.Id,
            Status    = status,
            BookedAt  = DateTime.UtcNow,
        };
        db.Appointments.Add(appt);
        db.SaveChanges();

        return (patient, slot, appt);
    }

    // ── AC-001: valid E.164 phone → SMS sent ──────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ValidPhone_SendsSmsWithE164()
    {
        var db  = CreateDb();
        var sms = new Mock<ISmsGateway>();
        var (_, _, appt) = Seed(db, phoneNumber: "+14155552671");

        var job = CreateJob(db, sms);
        await job.ExecuteAsync(appt.Id, "T-48h", NoCancellation());

        sms.Verify(
            s => s.SendAsync("+14155552671", It.Is<string>(b => b.Contains("APT-")), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_RawFormatPhone_NormalizesToE164BeforeSend()
    {
        var db  = CreateDb();
        var sms = new Mock<ISmsGateway>();
        var (_, _, appt) = Seed(db, phoneNumber: "(415) 555-2671");

        var job = CreateJob(db, sms);
        await job.ExecuteAsync(appt.Id, "T-2h", NoCancellation());

        sms.Verify(
            s => s.SendAsync("+14155552671", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── AC-003: no phone → graceful skip, no exception, no SMS ───────────────

    [Fact]
    public async Task ExecuteAsync_NullPhone_SkipsWithoutException()
    {
        var db  = CreateDb();
        var sms = new Mock<ISmsGateway>();
        var (_, _, appt) = Seed(db, phoneNumber: null);

        var job = CreateJob(db, sms);
        await job.ExecuteAsync(appt.Id, "T-48h", NoCancellation());

        sms.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidPhone_SkipsWithoutException()
    {
        var db  = CreateDb();
        var sms = new Mock<ISmsGateway>();
        var (_, _, appt) = Seed(db, phoneNumber: "not-a-number");

        var job = CreateJob(db, sms);
        await job.ExecuteAsync(appt.Id, "T-48h", NoCancellation());

        sms.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Status guard: cancelled appointment → skip ────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CancelledAppointment_SkipsWithoutSms()
    {
        var db  = CreateDb();
        var sms = new Mock<ISmsGateway>();
        var (_, _, appt) = Seed(db, phoneNumber: "+14155552671", status: AppointmentStatus.Cancelled);

        var job = CreateJob(db, sms);
        await job.ExecuteAsync(appt.Id, "T-48h", NoCancellation());

        sms.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Null-entity guards ────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_AppointmentNotFound_SkipsWithoutException()
    {
        var db  = CreateDb();
        var sms = new Mock<ISmsGateway>();
        var job = CreateJob(db, sms);

        // appointmentId 999 does not exist — should return cleanly.
        await job.ExecuteAsync(999, "T-48h", NoCancellation());

        sms.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── SMS body contains reference number ────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ValidPhone_BodyContainsReminderLabel()
    {
        var db  = CreateDb();
        string? capturedBody = null;
        var sms = new Mock<ISmsGateway>();
        sms.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
           .Callback<string, string, CancellationToken>((_, body, _) => capturedBody = body)
           .Returns(Task.CompletedTask);

        var (_, _, appt) = Seed(db, phoneNumber: "+14155552671");

        var job = CreateJob(db, sms);
        await job.ExecuteAsync(appt.Id, "T-2h", NoCancellation());

        Assert.NotNull(capturedBody);
        Assert.Contains("T-2h", capturedBody);
        Assert.Contains($"APT-{appt.Id:D6}", capturedBody);
        // Body must stay within single-segment SMS limit (160 chars).
        Assert.True(capturedBody!.Length <= 160, $"SMS body length {capturedBody.Length} exceeds 160 chars.");
    }

    // ── ES-001: AutomaticRetry attribute — 3 attempts, delays [30,60,120] ─────

    [Fact]
    public void ExecuteAsync_HasAutomaticRetryAttribute_3Attempts_30_60_120_Seconds()
    {
        var method = typeof(SendSmsReminderJob)
            .GetMethod(nameof(SendSmsReminderJob.ExecuteAsync));
        Assert.NotNull(method);

        var attr = method!.GetCustomAttribute<AutomaticRetryAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(3, attr!.Attempts);
        Assert.Equal(new[] { 30, 60, 120 }, attr.DelaysInSeconds);
    }
}
