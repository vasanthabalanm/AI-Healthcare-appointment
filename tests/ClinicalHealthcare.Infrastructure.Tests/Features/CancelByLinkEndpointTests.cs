using ClinicalHealthcare.Api.Features.Appointments;
using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Jobs;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for <see cref="CancelByLinkEndpoint"/> (TASK_028).
/// Verifies all guard paths and the happy-path cancellation flow.
/// </summary>
public sealed class CancelByLinkEndpointTests
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

    /// <summary>
    /// Seeds a Scheduled appointment that has a valid cancellation token.
    /// Returns both the appointment and the raw (unhashed) token so tests can pass it to the endpoint.
    /// </summary>
    private static (Appointment appointment, string rawToken) SeedWithToken(
        ApplicationDbContext db,
        AppointmentStatus    status   = AppointmentStatus.Scheduled,
        bool                 used     = false,
        DateTime?            expiry   = null)
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
            SlotTime        = DateTime.UtcNow.AddHours(50),
            DurationMinutes = 30,
            IsAvailable     = false,
        };
        db.Slots.Add(slot);
        db.SaveChanges();

        // Produce a raw token the same way the job does.
        var rawBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToBase64String(rawBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var hash     = SendEmailReminderJob.ComputeSha256Hex(rawToken);

        var appt = new Appointment
        {
            PatientId                 = patient.Id,
            SlotId                    = slot.Id,
            Status                    = status,
            BookedAt                  = DateTime.UtcNow,
            CancellationLinkTokenHash = hash,
            CancellationLinkExpiry    = expiry ?? DateTime.UtcNow.AddHours(48),
            CancellationLinkUsed      = used,
            ReminderJobId             = "job-reminder",
            EmailReminderJobId        = "job-email",
            SmsReminderJobId48h       = "job-sms-48h",
            SmsReminderJobId2h        = "job-sms-2h",
        };
        db.Appointments.Add(appt);
        db.SaveChanges();

        return (appt, rawToken);
    }

    // ── Guard: missing token ──────────────────────────────────────────────────

    [Fact]
    public async Task CancelByLink_MissingToken_Returns400()
    {
        var db   = CreateDb();
        var jobs = new Mock<IBackgroundJobClient>();

        var result = await CancelByLinkEndpoint.HandleCancelByLink(
            " ", db, NoOpCache(), jobs.Object, CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── Guard: token not found in DB ──────────────────────────────────────────

    [Fact]
    public async Task CancelByLink_UnknownToken_Returns400()
    {
        var db   = CreateDb();
        var jobs = new Mock<IBackgroundJobClient>();

        var result = await CancelByLinkEndpoint.HandleCancelByLink(
            "completely-invalid-token", db, NoOpCache(), jobs.Object, CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── Guard: already-used token ─────────────────────────────────────────────

    [Fact]
    public async Task CancelByLink_AlreadyUsedToken_Returns400()
    {
        var db   = CreateDb();
        var jobs = new Mock<IBackgroundJobClient>();
        var (_, rawToken) = SeedWithToken(db, used: true);

        var result = await CancelByLinkEndpoint.HandleCancelByLink(
            rawToken, db, NoOpCache(), jobs.Object, CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── Guard: expired token ──────────────────────────────────────────────────

    [Fact]
    public async Task CancelByLink_ExpiredToken_Returns400()
    {
        var db   = CreateDb();
        var jobs = new Mock<IBackgroundJobClient>();
        var (_, rawToken) = SeedWithToken(db, expiry: DateTime.UtcNow.AddHours(-1));

        var result = await CancelByLinkEndpoint.HandleCancelByLink(
            rawToken, db, NoOpCache(), jobs.Object, CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── Guard: appointment already cancelled ──────────────────────────────────

    [Fact]
    public async Task CancelByLink_AlreadyCancelledAppointment_Returns400()
    {
        var db   = CreateDb();
        var jobs = new Mock<IBackgroundJobClient>();
        var (_, rawToken) = SeedWithToken(db, status: AppointmentStatus.Cancelled);

        var result = await CancelByLinkEndpoint.HandleCancelByLink(
            rawToken, db, NoOpCache(), jobs.Object, CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── Happy path: valid token → appointment cancelled ───────────────────────

    [Fact]
    public async Task CancelByLink_ValidToken_Returns200AndCancelsAppointment()
    {
        var db   = CreateDb();
        var jobs = new Mock<IBackgroundJobClient>();
        jobs.Setup(j => j.ChangeState(It.IsAny<string>(), It.IsAny<IState>(), It.IsAny<string>()))
            .Returns(true);

        var (appt, rawToken) = SeedWithToken(db);

        var result = await CancelByLinkEndpoint.HandleCancelByLink(
            rawToken, db, NoOpCache(), jobs.Object, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));

        var reloaded = await db.Appointments.FindAsync(appt.Id);
        Assert.Equal(AppointmentStatus.Cancelled, reloaded!.Status);
        Assert.True(reloaded.CancellationLinkUsed);
    }

    // ── Happy path: all four reminder job IDs are deleted ────────────────────

    [Fact]
    public async Task CancelByLink_ValidToken_DeletesAllFourReminderJobIds()
    {
        var db   = CreateDb();
        var jobs = new Mock<IBackgroundJobClient>();
        jobs.Setup(j => j.ChangeState(It.IsAny<string>(), It.IsAny<IState>(), It.IsAny<string>()))
            .Returns(true);

        var (_, rawToken) = SeedWithToken(db);

        await CancelByLinkEndpoint.HandleCancelByLink(
            rawToken, db, NoOpCache(), jobs.Object, CancellationToken.None);

        jobs.Verify(j => j.ChangeState("job-reminder",  It.IsAny<DeletedState>(), null), Times.Once);
        jobs.Verify(j => j.ChangeState("job-email",     It.IsAny<DeletedState>(), null), Times.Once);
        jobs.Verify(j => j.ChangeState("job-sms-48h",   It.IsAny<DeletedState>(), null), Times.Once);
        jobs.Verify(j => j.ChangeState("job-sms-2h",    It.IsAny<DeletedState>(), null), Times.Once);
    }

    // ── Happy path: SwapMonitorJob enqueued ───────────────────────────────────

    [Fact]
    public async Task CancelByLink_ValidToken_EnqueuesSwapMonitorJob()
    {
        var db   = CreateDb();
        var jobs = new Mock<IBackgroundJobClient>();
        jobs.Setup(j => j.ChangeState(It.IsAny<string>(), It.IsAny<IState>(), It.IsAny<string>()))
            .Returns(true);

        var (_, rawToken) = SeedWithToken(db);

        await CancelByLinkEndpoint.HandleCancelByLink(
            rawToken, db, NoOpCache(), jobs.Object, CancellationToken.None);

        jobs.Verify(
            j => j.Create(It.Is<Job>(job => job.Type == typeof(SwapMonitorJob)), It.IsAny<IState>()),
            Times.Once);
    }
}
