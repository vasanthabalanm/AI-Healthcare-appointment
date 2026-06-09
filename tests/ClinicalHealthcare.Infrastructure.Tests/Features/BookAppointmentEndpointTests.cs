using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ClinicalHealthcare.Api.Features.Appointments;
using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Configuration;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Jobs;
using ClinicalHealthcare.Infrastructure.Services;
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
/// Unit tests for TASK_019: POST /appointments — slot booking with optimistic lock.
/// Covers AC-002, AC-003, AC-004, AC-005 and all edge cases.
/// </summary>
public sealed class BookAppointmentEndpointTests
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

    private static int StatusCode(IResult result)
    {
        var prop = result.GetType().GetProperty("StatusCode");
        return (int)(prop?.GetValue(result) ?? 0);
    }

    /// <summary>Builds an authenticated HttpContext with a JWT sub claim for the given userId.</summary>
    private static HttpContext BuildPatientContext(int userId = 42)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())],
            "TestAuth"));
        return ctx;
    }

    private static UserAccount SeedPatient(ApplicationDbContext db, int? forceId = null)
    {
        var account = new UserAccount
        {
            Email        = "patient@test.com",
            Role         = "patient",
            FirstName    = "Test",
            LastName     = "Patient",
            PasswordHash = "hash",
            IsActive     = true,
        };
        db.UserAccounts.Add(account);
        db.SaveChanges();
        return account;
    }

    private static Slot SeedAvailableSlot(ApplicationDbContext db, int daysFromNow = 1)
    {
        var slot = new Slot
        {
            SlotTime        = DateTime.UtcNow.AddDays(daysFromNow).AddHours(9),
            DurationMinutes = 30,
            IsAvailable     = true,
        };
        db.Slots.Add(slot);
        db.SaveChanges();
        return slot;
    }

    private static (Mock<ICacheService> cacheMock, Mock<IBackgroundJobClient> jobsMock)
        CreateMocks()
    {
        var cache = new Mock<ICacheService>();
        var jobs  = new Mock<IBackgroundJobClient>();
        return (cache, jobs);
    }

    private static INoShowRiskScoreService NoOpRisk()
        => Mock.Of<INoShowRiskScoreService>(
            r => r.CalculateAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())
                 == Task.FromResult(0));

    // ── AC-002: valid booking returns 201 + slot marked unavailable ───────────

    [Fact]
    public async Task BookAppointment_ValidSlot_Returns201_SlotMarkedUnavailable()
    {
        var db      = CreateDb();
        var patient = SeedPatient(db);
        var slot    = SeedAvailableSlot(db);
        var (cache, jobs) = CreateMocks();
        var ctx     = BuildPatientContext(patient.Id);

        var result = await BookAppointmentEndpoint.HandleBookAppointment(
            new BookAppointmentRequest(slot.Id), ctx, db, cache.Object, jobs.Object, NoOpRisk(), Options.Create(new AppSettings()), CancellationToken.None);

        Assert.Equal(201, StatusCode(result));

        db.ChangeTracker.Clear();
        var updatedSlot = await db.Slots.FindAsync(slot.Id);
        Assert.False(updatedSlot!.IsAvailable);
    }

    // ── AC-002: appointment row is created with Scheduled status ─────────────

    [Fact]
    public async Task BookAppointment_ValidSlot_InsertsAppointmentWithScheduledStatus()
    {
        var db      = CreateDb();
        var patient = SeedPatient(db);
        var slot    = SeedAvailableSlot(db);
        var (cache, jobs) = CreateMocks();
        var ctx     = BuildPatientContext(patient.Id);

        await BookAppointmentEndpoint.HandleBookAppointment(
            new BookAppointmentRequest(slot.Id), ctx, db, cache.Object, jobs.Object, NoOpRisk(), Options.Create(new AppSettings()), CancellationToken.None);

        var appt = await db.Appointments.FirstOrDefaultAsync(a => a.PatientId == patient.Id);
        Assert.NotNull(appt);
        Assert.Equal(AppointmentStatus.Scheduled, appt!.Status);
        Assert.Equal(slot.Id, appt.SlotId);
    }

    // ── Edge: slot already unavailable → 409 before SaveChanges ──────────────

    [Fact]
    public async Task BookAppointment_SlotAlreadyBooked_Returns409()
    {
        var db   = CreateDb();
        var patient = SeedPatient(db);
        var slot = SeedAvailableSlot(db);
        slot.IsAvailable = false;
        db.SaveChanges();

        var (cache, jobs) = CreateMocks();
        var ctx = BuildPatientContext(patient.Id);

        var result = await BookAppointmentEndpoint.HandleBookAppointment(
            new BookAppointmentRequest(slot.Id), ctx, db, cache.Object, jobs.Object, NoOpRisk(), Options.Create(new AppSettings()), CancellationToken.None);

        Assert.Equal(409, StatusCode(result));
        // No appointment should have been created.
        Assert.False(await db.Appointments.AnyAsync());
    }

    // ── Edge: past slot → 400 ─────────────────────────────────────────────────

    [Fact]
    public async Task BookAppointment_PastSlot_Returns400()
    {
        var db = CreateDb();
        var patient = SeedPatient(db);

        var slot = new Slot
        {
            SlotTime        = DateTime.UtcNow.AddDays(-1),
            DurationMinutes = 30,
            IsAvailable     = true,
        };
        db.Slots.Add(slot);
        db.SaveChanges();

        var (cache, jobs) = CreateMocks();
        var ctx = BuildPatientContext(patient.Id);

        var result = await BookAppointmentEndpoint.HandleBookAppointment(
            new BookAppointmentRequest(slot.Id), ctx, db, cache.Object, jobs.Object, NoOpRisk(), Options.Create(new AppSettings()), CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── AC-005 (duplicate appointment): patient with active appt → 409 ────────

    [Fact]
    public async Task BookAppointment_DuplicateActiveAppointment_Returns409()
    {
        var db      = CreateDb();
        var patient = SeedPatient(db);
        var slot1   = SeedAvailableSlot(db, daysFromNow: 2);
        var slot2   = SeedAvailableSlot(db, daysFromNow: 3);

        // Pre-existing active appointment.
        db.Appointments.Add(new Appointment
        {
            PatientId = patient.Id,
            SlotId    = slot1.Id,
            Status    = AppointmentStatus.Scheduled,
        });
        db.SaveChanges();

        var (cache, jobs) = CreateMocks();
        var ctx = BuildPatientContext(patient.Id);

        var result = await BookAppointmentEndpoint.HandleBookAppointment(
            new BookAppointmentRequest(slot2.Id), ctx, db, cache.Object, jobs.Object, NoOpRisk(), Options.Create(new AppSettings()), CancellationToken.None);

        Assert.Equal(409, StatusCode(result));
    }

    // ── AC-004: cache invalidated after successful booking ────────────────────

    [Fact]
    public async Task BookAppointment_Success_InvalidatesSlotCache()
    {
        var db      = CreateDb();
        var patient = SeedPatient(db);
        var slot    = SeedAvailableSlot(db);
        var (cache, jobs) = CreateMocks();
        var ctx     = BuildPatientContext(patient.Id);

        await BookAppointmentEndpoint.HandleBookAppointment(
            new BookAppointmentRequest(slot.Id), ctx, db, cache.Object, jobs.Object, NoOpRisk(), Options.Create(new AppSettings()), CancellationToken.None);

        // Cache key for the slot's date must be deleted.
        var expectedKey = $"{GetSlotsEndpoint.CacheKeyPrefix}{DateOnly.FromDateTime(slot.SlotTime):yyyy-MM-dd}";
        cache.Verify(c => c.DeleteAsync(expectedKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── AC-005: Hangfire jobs enqueued after booking ──────────────────────────

    [Fact]
    public async Task BookAppointment_Success_EnqueuesConfirmationJob()
    {
        var db      = CreateDb();
        var patient = SeedPatient(db);
        var slot    = SeedAvailableSlot(db);
        var (cache, jobs) = CreateMocks();
        var ctx     = BuildPatientContext(patient.Id);

        await BookAppointmentEndpoint.HandleBookAppointment(
            new BookAppointmentRequest(slot.Id), ctx, db, cache.Object, jobs.Object, NoOpRisk(), Options.Create(new AppSettings()), CancellationToken.None);

        jobs.Verify(j => j.Create(
            It.Is<Job>(job => job.Type == typeof(SendConfirmationEmailJob)),
            It.IsAny<IState>()), Times.Once);
    }

    [Fact]
    public async Task BookAppointment_SlotMoreThan48hAway_SchedulesReminderJob()
    {
        var db      = CreateDb();
        var patient = SeedPatient(db);
        var slot    = SeedAvailableSlot(db, daysFromNow: 5);  // well beyond 48h
        var (cache, jobs) = CreateMocks();
        var ctx     = BuildPatientContext(patient.Id);

        await BookAppointmentEndpoint.HandleBookAppointment(
            new BookAppointmentRequest(slot.Id), ctx, db, cache.Object, jobs.Object, NoOpRisk(), Options.Create(new AppSettings()), CancellationToken.None);

        jobs.Verify(j => j.Create(
            It.Is<Job>(job => job.Type == typeof(SendReminderJob)),
            It.IsAny<IState>()), Times.Once);
    }

    // ── Input validation: invalid SlotId → 422 ────────────────────────────────

    [Fact]
    public async Task BookAppointment_InvalidSlotId_Returns422()
    {
        var db  = CreateDb();
        var (cache, jobs) = CreateMocks();
        var ctx = BuildPatientContext(42);

        var result = await BookAppointmentEndpoint.HandleBookAppointment(
            new BookAppointmentRequest(0), ctx, db, cache.Object, jobs.Object, NoOpRisk(), Options.Create(new AppSettings()), CancellationToken.None);

        Assert.Equal(422, StatusCode(result));
    }

    // ── Input validation: missing sub claim → 401 ─────────────────────────────

    [Fact]
    public async Task BookAppointment_MissingSubClaim_Returns401()
    {
        var db  = CreateDb();
        var (cache, jobs) = CreateMocks();

        // No sub claim in context.
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "patient")], "TestAuth"));

        var result = await BookAppointmentEndpoint.HandleBookAppointment(
            new BookAppointmentRequest(1), ctx, db, cache.Object, jobs.Object, NoOpRisk(), Options.Create(new AppSettings()), CancellationToken.None);

        Assert.Equal(401, StatusCode(result));
    }

    // ── Edge: slot not found → 400 ────────────────────────────────────────────

    [Fact]
    public async Task BookAppointment_SlotNotFound_Returns400()
    {
        var db  = CreateDb();
        SeedPatient(db);
        var (cache, jobs) = CreateMocks();
        var ctx = BuildPatientContext(1);

        var result = await BookAppointmentEndpoint.HandleBookAppointment(
            new BookAppointmentRequest(999), ctx, db, cache.Object, jobs.Object, NoOpRisk(), Options.Create(new AppSettings()), CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── AC-001/AC-005: risk score stored on appointment at booking time ────────

    [Fact]
    public async Task BookAppointment_StoresRiskScoreOnAppointment()
    {
        var db      = CreateDb();
        var patient = SeedPatient(db);
        var slot    = SeedAvailableSlot(db);
        var (cache, jobs) = CreateMocks();
        var ctx     = BuildPatientContext(patient.Id);

        // Risk service returns score 45 (below default threshold 70)
        var riskMock = new Mock<INoShowRiskScoreService>();
        riskMock.Setup(r => r.CalculateAsync(patient.Id, slot.SlotTime, It.IsAny<CancellationToken>()))
                .ReturnsAsync(45);

        await BookAppointmentEndpoint.HandleBookAppointment(
            new BookAppointmentRequest(slot.Id), ctx, db, cache.Object, jobs.Object,
            riskMock.Object, Options.Create(new AppSettings()), CancellationToken.None);

        var appt = await db.Appointments.FirstAsync(a => a.PatientId == patient.Id);
        Assert.Equal(45, appt.NoShowRiskScore);
        Assert.False(appt.IsHighRisk); // 45 < 70
    }

    // ── AC-003: IsHighRisk=true when score >= threshold ────────────────────────

    [Fact]
    public async Task BookAppointment_ScoreAboveThreshold_SetsIsHighRisk()
    {
        var db      = CreateDb();
        var patient = SeedPatient(db);
        var slot    = SeedAvailableSlot(db);
        var (cache, jobs) = CreateMocks();
        var ctx     = BuildPatientContext(patient.Id);

        // Risk service returns score 80 (above default threshold 70)
        var riskMock = new Mock<INoShowRiskScoreService>();
        riskMock.Setup(r => r.CalculateAsync(patient.Id, slot.SlotTime, It.IsAny<CancellationToken>()))
                .ReturnsAsync(80);

        await BookAppointmentEndpoint.HandleBookAppointment(
            new BookAppointmentRequest(slot.Id), ctx, db, cache.Object, jobs.Object,
            riskMock.Object, Options.Create(new AppSettings()), CancellationToken.None);

        var appt = await db.Appointments.FirstAsync(a => a.PatientId == patient.Id);
        Assert.Equal(80, appt.NoShowRiskScore);
        Assert.True(appt.IsHighRisk); // 80 >= 70
    }
}
