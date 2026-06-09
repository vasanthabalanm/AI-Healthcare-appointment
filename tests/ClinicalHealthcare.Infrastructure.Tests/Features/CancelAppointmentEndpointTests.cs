using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ClinicalHealthcare.Api.Features.Appointments;
using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Configuration;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Jobs;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for TASK_023: DELETE /appointments/{id} (cancel).
/// Verifies:
///   AC-001 — patient can cancel their own Scheduled appointment.
///   AC-002 — cancellation cutoff window enforced (400 when within window).
///   AC-003 — slot stays unavailable; SwapMonitorJob is enqueued.
///   AC-005 — stale Hangfire reminder job is deleted on cancel.
/// </summary>
public sealed class CancelAppointmentEndpointTests
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

    private static IOptions<AppSettings> DefaultSettings(int cutoffHours = 24) =>
        Options.Create(new AppSettings { CancellationCutoffHours = cutoffHours });

    private static ICacheService NoOpCache()
    {
        var m = new Mock<ICacheService>();
        m.Setup(c => c.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
         .Returns(Task.CompletedTask);
        return m.Object;
    }

    private static int StatusCode(IResult result)
    {
        var prop = result.GetType().GetProperty("StatusCode");
        return (int)(prop?.GetValue(result) ?? 0);
    }

    private static HttpContext BuildPatientContext(int userId)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())],
            "TestAuth"));
        return ctx;
    }

    /// <summary>Seeds a Scheduled appointment whose slot is 48 h in the future (well beyond cutoff).</summary>
    private static (UserAccount patient, Slot slot, Appointment appointment) SeedScheduledAppointment(
        ApplicationDbContext db,
        double slotOffsetHours = 48)
    {
        var patient = new UserAccount
        {
            Email        = "p@test.com",
            Role         = "patient",
            FirstName    = "P",
            LastName     = "T",
            PasswordHash = "h",
            IsActive     = true,
        };
        db.UserAccounts.Add(patient);
        db.SaveChanges();

        var slot = new Slot
        {
            SlotTime        = DateTime.UtcNow.AddHours(slotOffsetHours),
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

    // ── AC-001 + AC-003: cancel returns 200, slot stays unavailable ───────────

    [Fact]
    public async Task CancelAppointment_Scheduled_Returns200_SlotRemainsUnavailable()
    {
        var db = CreateDb();
        var (patient, slot, appt) = SeedScheduledAppointment(db);
        var jobs = new Mock<IBackgroundJobClient>();
        var ctx  = BuildPatientContext(patient.Id);

        var result = await CancelAppointmentEndpoint.HandleCancelAppointment(
            appt.Id, ctx, db, NoOpCache(), jobs.Object, DefaultSettings(), CancellationToken.None);

        Assert.Equal(200, StatusCode(result));

        var reloadedSlot = await db.Slots.FindAsync(slot.Id);
        Assert.False(reloadedSlot!.IsAvailable,
            "Slot must remain unavailable — SwapMonitorJob owns slot release.");
    }

    // ── AC-003: SwapMonitorJob is enqueued on successful cancel ───────────────

    [Fact]
    public async Task CancelAppointment_Scheduled_EnqueuesSwapMonitorJob()
    {
        var db = CreateDb();
        var (patient, _, appt) = SeedScheduledAppointment(db);
        var jobs = new Mock<IBackgroundJobClient>();
        var ctx  = BuildPatientContext(patient.Id);

        await CancelAppointmentEndpoint.HandleCancelAppointment(
            appt.Id, ctx, db, NoOpCache(), jobs.Object, DefaultSettings(), CancellationToken.None);

        jobs.Verify(
            j => j.Create(It.Is<Job>(job => job.Type == typeof(SwapMonitorJob)), It.IsAny<IState>()),
            Times.Once);
    }

    // ── AC-005: stale reminder job is cancelled on cancel ─────────────────────

    [Fact]
    public async Task CancelAppointment_WithReminderJobId_CancelsReminderJob()
    {
        var db = CreateDb();
        var (patient, _, appt) = SeedScheduledAppointment(db);
        appt.ReminderJobId = "job-reminder-42";
        db.SaveChanges();

        var jobs = new Mock<IBackgroundJobClient>();
        jobs.Setup(j => j.ChangeState(It.IsAny<string>(), It.IsAny<IState>(), It.IsAny<string>()))
            .Returns(true);

        var ctx = BuildPatientContext(patient.Id);

        await CancelAppointmentEndpoint.HandleCancelAppointment(
            appt.Id, ctx, db, NoOpCache(), jobs.Object, DefaultSettings(), CancellationToken.None);

        jobs.Verify(
            j => j.ChangeState("job-reminder-42", It.IsAny<DeletedState>(), null),
            Times.Once);
    }

    // ── TASK_027 / AC-005: SMS reminder job IDs are deleted on cancel ─────────

    [Fact]
    public async Task CancelAppointment_WithSmsJobIds_DeletesBothSmsJobs()
    {
        var db = CreateDb();
        var (patient, _, appt) = SeedScheduledAppointment(db);
        appt.SmsReminderJobId48h = "sms-job-48h";
        appt.SmsReminderJobId2h  = "sms-job-2h";
        db.SaveChanges();

        var jobs = new Mock<IBackgroundJobClient>();
        jobs.Setup(j => j.ChangeState(It.IsAny<string>(), It.IsAny<IState>(), It.IsAny<string>()))
            .Returns(true);

        var ctx = BuildPatientContext(patient.Id);

        await CancelAppointmentEndpoint.HandleCancelAppointment(
            appt.Id, ctx, db, NoOpCache(), jobs.Object, DefaultSettings(), CancellationToken.None);

        jobs.Verify(
            j => j.ChangeState("sms-job-48h", It.IsAny<DeletedState>(), null),
            Times.Once);
        jobs.Verify(
            j => j.ChangeState("sms-job-2h", It.IsAny<DeletedState>(), null),
            Times.Once);
    }

    // ── AC-002: within cutoff window returns 400 ──────────────────────────────

    [Fact]
    public async Task CancelAppointment_WithinCutoff_Returns400()
    {
        var db = CreateDb();
        // Slot is only 12 h away; cutoff is 24 h → should be rejected.
        var (patient, _, appt) = SeedScheduledAppointment(db, slotOffsetHours: 12);
        var jobs = new Mock<IBackgroundJobClient>();
        var ctx  = BuildPatientContext(patient.Id);

        var result = await CancelAppointmentEndpoint.HandleCancelAppointment(
            appt.Id, ctx, db, NoOpCache(), jobs.Object, DefaultSettings(cutoffHours: 24), CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── Guard: wrong patient returns 403 ─────────────────────────────────────

    [Fact]
    public async Task CancelAppointment_WrongPatient_Returns403()
    {
        var db = CreateDb();
        var (_, _, appt) = SeedScheduledAppointment(db);
        var jobs = new Mock<IBackgroundJobClient>();
        var ctx  = BuildPatientContext(userId: 9999);

        var result = await CancelAppointmentEndpoint.HandleCancelAppointment(
            appt.Id, ctx, db, NoOpCache(), jobs.Object, DefaultSettings(), CancellationToken.None);

        Assert.Equal(403, StatusCode(result));
    }

    // ── Guard: non-Scheduled status returns 400 ──────────────────────────────

    [Fact]
    public async Task CancelAppointment_AlreadyCancelled_Returns400()
    {
        var db = CreateDb();
        var (patient, slot, _) = SeedScheduledAppointment(db);
        var appt = new Appointment
        {
            PatientId = patient.Id,
            SlotId    = slot.Id,
            Status    = AppointmentStatus.Cancelled,
            BookedAt  = DateTime.UtcNow,
        };
        db.Appointments.Add(appt);
        db.SaveChanges();

        var jobs = new Mock<IBackgroundJobClient>();
        var ctx  = BuildPatientContext(patient.Id);

        var result = await CancelAppointmentEndpoint.HandleCancelAppointment(
            appt.Id, ctx, db, NoOpCache(), jobs.Object, DefaultSettings(), CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── Guard: non-existent appointment returns 404 ───────────────────────────

    [Fact]
    public async Task CancelAppointment_NotFound_Returns404()
    {
        var db   = CreateDb();
        var jobs = new Mock<IBackgroundJobClient>();
        var ctx  = BuildPatientContext(userId: 1);

        var result = await CancelAppointmentEndpoint.HandleCancelAppointment(
            id: 9999, ctx, db, NoOpCache(), jobs.Object, DefaultSettings(), CancellationToken.None);

        Assert.Equal(404, StatusCode(result));
    }
}
