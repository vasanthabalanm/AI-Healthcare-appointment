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
/// Unit tests for TASK_023: PATCH /appointments/{id}/reschedule.
/// Verifies:
///   AC-004 — patient can reschedule to a different available slot.
///   AC-002 — reschedule cutoff window enforced.
///   AC-005 — stale reminder deleted; new reminder scheduled when slot is far enough out.
/// </summary>
public sealed class RescheduleAppointmentEndpointTests
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

    private static (UserAccount patient, Slot currentSlot, Slot newSlot, Appointment appointment) Seed(
        ApplicationDbContext db,
        double currentSlotOffsetHours = 72,
        double newSlotOffsetHours     = 96)
    {
        var patient = new UserAccount
        {
            Email = "p@test.com", Role = "patient",
            FirstName = "P", LastName = "T",
            PasswordHash = "h", IsActive = true,
        };
        db.UserAccounts.Add(patient);
        db.SaveChanges();

        var currentSlot = new Slot
        {
            SlotTime = DateTime.UtcNow.AddHours(currentSlotOffsetHours),
            DurationMinutes = 30,
            IsAvailable = false,
        };
        var newSlot = new Slot
        {
            SlotTime = DateTime.UtcNow.AddHours(newSlotOffsetHours),
            DurationMinutes = 30,
            IsAvailable = true,
        };
        db.Slots.AddRange(currentSlot, newSlot);
        db.SaveChanges();

        var appt = new Appointment
        {
            PatientId = patient.Id,
            SlotId    = currentSlot.Id,
            Status    = AppointmentStatus.Scheduled,
            BookedAt  = DateTime.UtcNow,
        };
        db.Appointments.Add(appt);
        db.SaveChanges();

        return (patient, currentSlot, newSlot, appt);
    }

    // ── AC-004: happy path ────────────────────────────────────────────────────

    [Fact]
    public async Task RescheduleAppointment_ValidNewSlot_Returns200()
    {
        var db = CreateDb();
        var (patient, _, newSlot, appt) = Seed(db);
        var jobs = new Mock<IBackgroundJobClient>();
        var ctx  = BuildPatientContext(patient.Id);

        var result = await RescheduleAppointmentEndpoint.HandleRescheduleAppointment(
            appt.Id,
            new RescheduleRequest(newSlot.Id),
            ctx, db, NoOpCache(), jobs.Object, DefaultSettings(), CancellationToken.None);

        Assert.Equal(200, StatusCode(result));

        var updated = await db.Appointments.FindAsync(appt.Id);
        Assert.Equal(newSlot.Id, updated!.SlotId);
    }

    [Fact]
    public async Task RescheduleAppointment_ValidNewSlot_OldSlotReleasedViaSwapMonitor()
    {
        var db = CreateDb();
        var (patient, currentSlot, newSlot, appt) = Seed(db);
        var jobs = new Mock<IBackgroundJobClient>();
        var ctx  = BuildPatientContext(patient.Id);

        await RescheduleAppointmentEndpoint.HandleRescheduleAppointment(
            appt.Id,
            new RescheduleRequest(newSlot.Id),
            ctx, db, NoOpCache(), jobs.Object, DefaultSettings(), CancellationToken.None);

        // Old slot stays unavailable until SwapMonitorJob runs.
        var oldSlot = await db.Slots.FindAsync(currentSlot.Id);
        Assert.False(oldSlot!.IsAvailable);

        // SwapMonitorJob must be enqueued for the old slot.
        jobs.Verify(
            j => j.Create(It.Is<Job>(job => job.Type == typeof(SwapMonitorJob)), It.IsAny<IState>()),
            Times.Once);
    }

    // ── AC-005: stale reminder deleted ────────────────────────────────────────

    [Fact]
    public async Task RescheduleAppointment_WithReminderJobId_CancelsOldReminder()
    {
        var db = CreateDb();
        var (patient, _, newSlot, appt) = Seed(db);
        appt.ReminderJobId = "job-old-reminder";
        db.SaveChanges();

        var jobs = new Mock<IBackgroundJobClient>();
        jobs.Setup(j => j.ChangeState(It.IsAny<string>(), It.IsAny<IState>(), It.IsAny<string>()))
            .Returns(true);

        var ctx = BuildPatientContext(patient.Id);

        await RescheduleAppointmentEndpoint.HandleRescheduleAppointment(
            appt.Id,
            new RescheduleRequest(newSlot.Id),
            ctx, db, NoOpCache(), jobs.Object, DefaultSettings(), CancellationToken.None);

        jobs.Verify(
            j => j.ChangeState("job-old-reminder", It.IsAny<DeletedState>(), null),
            Times.Once);
    }

    // ── AC-002: within cutoff returns 400 ────────────────────────────────────

    [Fact]
    public async Task RescheduleAppointment_WithinCutoff_Returns400()
    {
        var db = CreateDb();
        // Current slot only 12 h away; cutoff is 24 h.
        var (patient, _, newSlot, appt) = Seed(db, currentSlotOffsetHours: 12);
        var jobs = new Mock<IBackgroundJobClient>();
        var ctx  = BuildPatientContext(patient.Id);

        var result = await RescheduleAppointmentEndpoint.HandleRescheduleAppointment(
            appt.Id,
            new RescheduleRequest(newSlot.Id),
            ctx, db, NoOpCache(), jobs.Object, DefaultSettings(cutoffHours: 24), CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── Guard: unavailable new slot returns 409 ───────────────────────────────

    [Fact]
    public async Task RescheduleAppointment_UnavailableNewSlot_Returns409()
    {
        var db = CreateDb();
        var (patient, _, newSlot, appt) = Seed(db);
        newSlot.IsAvailable = false;
        db.SaveChanges();

        var jobs = new Mock<IBackgroundJobClient>();
        var ctx  = BuildPatientContext(patient.Id);

        var result = await RescheduleAppointmentEndpoint.HandleRescheduleAppointment(
            appt.Id,
            new RescheduleRequest(newSlot.Id),
            ctx, db, NoOpCache(), jobs.Object, DefaultSettings(), CancellationToken.None);

        Assert.Equal(409, StatusCode(result));
    }

    // ── Guard: same slot returns 400 ─────────────────────────────────────────

    [Fact]
    public async Task RescheduleAppointment_SameSlot_Returns400()
    {
        var db = CreateDb();
        var (patient, currentSlot, _, appt) = Seed(db);
        var jobs = new Mock<IBackgroundJobClient>();
        var ctx  = BuildPatientContext(patient.Id);

        var result = await RescheduleAppointmentEndpoint.HandleRescheduleAppointment(
            appt.Id,
            new RescheduleRequest(currentSlot.Id),   // same slot
            ctx, db, NoOpCache(), jobs.Object, DefaultSettings(), CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── Guard: wrong patient returns 403 ─────────────────────────────────────

    [Fact]
    public async Task RescheduleAppointment_WrongPatient_Returns403()
    {
        var db = CreateDb();
        var (_, _, newSlot, appt) = Seed(db);
        var jobs = new Mock<IBackgroundJobClient>();
        var ctx  = BuildPatientContext(userId: 9999);

        var result = await RescheduleAppointmentEndpoint.HandleRescheduleAppointment(
            appt.Id,
            new RescheduleRequest(newSlot.Id),
            ctx, db, NoOpCache(), jobs.Object, DefaultSettings(), CancellationToken.None);

        Assert.Equal(403, StatusCode(result));
    }

    // ── Guard: not-found appointment returns 404 ──────────────────────────────

    [Fact]
    public async Task RescheduleAppointment_NotFound_Returns404()
    {
        var db = CreateDb();
        var jobs = new Mock<IBackgroundJobClient>();
        var ctx  = BuildPatientContext(userId: 1);

        var result = await RescheduleAppointmentEndpoint.HandleRescheduleAppointment(
            id: 9999,
            new RescheduleRequest(1),
            ctx, db, NoOpCache(), jobs.Object, DefaultSettings(), CancellationToken.None);

        Assert.Equal(404, StatusCode(result));
    }

    // ── Guard: non-Scheduled appointment returns 400 ──────────────────────────

    [Fact]
    public async Task RescheduleAppointment_NotScheduled_Returns400()
    {
        var db = CreateDb();
        var (patient, _, newSlot, appt) = Seed(db);
        appt.Status = AppointmentStatus.Cancelled;
        db.SaveChanges();

        var jobs = new Mock<IBackgroundJobClient>();
        var ctx  = BuildPatientContext(patient.Id);

        var result = await RescheduleAppointmentEndpoint.HandleRescheduleAppointment(
            appt.Id,
            new RescheduleRequest(newSlot.Id),
            ctx, db, NoOpCache(), jobs.Object, DefaultSettings(), CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── AC-005: new reminder scheduled for slot > 48 h away ──────────────────

    [Fact]
    public async Task RescheduleAppointment_NewReminderScheduled_WhenSlotBeyond48h()
    {
        var db = CreateDb();
        // newSlotOffsetHours=96 > 48 h → reminder should be scheduled.
        var (patient, _, newSlot, appt) = Seed(db, newSlotOffsetHours: 96);
        var jobs = new Mock<IBackgroundJobClient>();
        jobs.Setup(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Returns("job-new-reminder");
        var ctx = BuildPatientContext(patient.Id);

        await RescheduleAppointmentEndpoint.HandleRescheduleAppointment(
            appt.Id,
            new RescheduleRequest(newSlot.Id),
            ctx, db, NoOpCache(), jobs.Object, DefaultSettings(), CancellationToken.None);

        // Verify a scheduled (delayed) job was created for SendReminderJob.
        jobs.Verify(
            j => j.Create(
                It.Is<Job>(job => job.Type == typeof(SendReminderJob)),
                It.IsAny<ScheduledState>()),
            Times.Once);
    }
}
