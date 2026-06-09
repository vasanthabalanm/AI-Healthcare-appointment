using ClinicalHealthcare.Api.Features.Staff;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for TASK_036 — Staff daily schedule view.
/// Covers AC-001 to AC-005 and edge cases.
/// </summary>
public sealed class GetDailyScheduleEndpointTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ApplicationDbContext BuildDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static int StatusCode(IResult result)
    {
        if (result is IStatusCodeHttpResult sc && sc.StatusCode is not null)
            return sc.StatusCode.Value;
        return (int)(result.GetType().GetProperty("StatusCode")?.GetValue(result) ?? 0);
    }

    private static T ResponseValue<T>(IResult result)
        => (T)result.GetType().GetProperty("Value")!.GetValue(result)!;

    private static async Task<(UserAccount staff, UserAccount patient, Slot slot, Appointment appt)>
        SeedAppointmentAsync(ApplicationDbContext db, DateTime slotTime, AppointmentStatus status = AppointmentStatus.Scheduled, bool isHighRisk = false)
    {
        var staff = new UserAccount { Email = $"staff-{Guid.NewGuid()}@t.com", PasswordHash = "", Role = "staff", FirstName = "S", LastName = "T" };
        var patient = new UserAccount { Email = $"p-{Guid.NewGuid()}@t.com", PasswordHash = "", Role = "patient", FirstName = "Pat", LastName = "Last" };
        db.UserAccounts.AddRange(staff, patient);
        await db.SaveChangesAsync();

        var slot = new Slot { SlotTime = slotTime, DurationMinutes = 30, IsAvailable = false };
        db.Slots.Add(slot);
        await db.SaveChangesAsync();

        var appt = new Appointment
        {
            PatientId   = patient.Id,
            SlotId      = slot.Id,
            Status      = status,
            IsHighRisk  = isHighRisk,
        };
        db.Appointments.Add(appt);
        await db.SaveChangesAsync();

        return (staff, patient, slot, appt);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-001 — Returns appointments ordered by slot time ASC
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetDailySchedule_ReturnsAppointmentsOrderedBySlotTimeAsc()
    {
        await using var db = BuildDb();
        var today = DateTime.UtcNow.Date;

        await SeedAppointmentAsync(db, today.AddHours(10));
        await SeedAppointmentAsync(db, today.AddHours(8));
        await SeedAppointmentAsync(db, today.AddHours(9));

        var result = await GetDailyScheduleEndpoint.HandleGetDailySchedule(db, null, 1, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));
        var body = ResponseValue<SchedulePageResponse>(result);
        Assert.Equal(3, body.TotalCount);
        Assert.Equal(3, body.Data.Count);
        Assert.True(body.Data[0].SlotTime <= body.Data[1].SlotTime);
        Assert.True(body.Data[1].SlotTime <= body.Data[2].SlotTime);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-002 — intakeStatus: Submitted when IntakeRecord exists, Pending otherwise
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetDailySchedule_WithIntakeRecord_IntakeStatusIsSubmitted()
    {
        await using var db = BuildDb();
        var today = DateTime.UtcNow.Date;
        var (_, patient, _, _) = await SeedAppointmentAsync(db, today.AddHours(9));

        db.IntakeRecords.Add(new IntakeRecord
        {
            PatientId  = patient.Id,
            IsLatest   = true,
            IsDeleted  = false,
            Source     = IntakeSource.Manual,
        });
        await db.SaveChangesAsync();

        var result = await GetDailyScheduleEndpoint.HandleGetDailySchedule(db, null, 1, CancellationToken.None);
        var body = ResponseValue<SchedulePageResponse>(result);

        Assert.Single(body.Data);
        Assert.Equal("Submitted", body.Data[0].IntakeStatus);
    }

    [Fact]
    public async Task GetDailySchedule_NoIntakeRecord_IntakeStatusIsPending()
    {
        await using var db = BuildDb();
        var today = DateTime.UtcNow.Date;
        await SeedAppointmentAsync(db, today.AddHours(9));
        // No IntakeRecord seeded.

        var result = await GetDailyScheduleEndpoint.HandleGetDailySchedule(db, null, 1, CancellationToken.None);
        var body = ResponseValue<SchedulePageResponse>(result);

        Assert.Single(body.Data);
        Assert.Equal("Pending", body.Data[0].IntakeStatus);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-003 — riskFlag from Appointment.IsHighRisk
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetDailySchedule_HighRiskAppointment_RiskFlagIsTrue()
    {
        await using var db = BuildDb();
        var today = DateTime.UtcNow.Date;
        await SeedAppointmentAsync(db, today.AddHours(9), isHighRisk: true);

        var result = await GetDailyScheduleEndpoint.HandleGetDailySchedule(db, null, 1, CancellationToken.None);
        var body = ResponseValue<SchedulePageResponse>(result);

        Assert.Single(body.Data);
        Assert.True(body.Data[0].RiskFlag);
    }

    [Fact]
    public async Task GetDailySchedule_NormalRiskAppointment_RiskFlagIsFalse()
    {
        await using var db = BuildDb();
        var today = DateTime.UtcNow.Date;
        await SeedAppointmentAsync(db, today.AddHours(9), isHighRisk: false);

        var result = await GetDailyScheduleEndpoint.HandleGetDailySchedule(db, null, 1, CancellationToken.None);
        var body = ResponseValue<SchedulePageResponse>(result);

        Assert.Single(body.Data);
        Assert.False(body.Data[0].RiskFlag);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-004 — ?date= overrides default date
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetDailySchedule_DateParam_ReturnsAppointmentsForThatDate()
    {
        await using var db = BuildDb();
        var yesterday = DateTime.UtcNow.Date.AddDays(-1);
        var today     = DateTime.UtcNow.Date;

        await SeedAppointmentAsync(db, yesterday.AddHours(9));   // historical
        await SeedAppointmentAsync(db, today.AddHours(9));       // today — should NOT appear

        var targetDate = DateOnly.FromDateTime(yesterday);
        var result = await GetDailyScheduleEndpoint.HandleGetDailySchedule(db, targetDate, 1, CancellationToken.None);
        var body = ResponseValue<SchedulePageResponse>(result);

        Assert.Equal(1, body.TotalCount);
        Assert.Equal(DateOnly.FromDateTime(yesterday), DateOnly.FromDateTime(body.Data[0].SlotTime));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-005 — Pagination: 50/page with totalCount
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetDailySchedule_MoreThan50Appointments_ReturnsFirstPage()
    {
        await using var db = BuildDb();
        var today = DateTime.UtcNow.Date;

        // Seed 55 appointments for today.
        for (var i = 0; i < 55; i++)
            await SeedAppointmentAsync(db, today.AddMinutes(i * 10));

        var result = await GetDailyScheduleEndpoint.HandleGetDailySchedule(db, null, 1, CancellationToken.None);
        var body = ResponseValue<SchedulePageResponse>(result);

        Assert.Equal(55, body.TotalCount);
        Assert.Equal(2, body.PageCount);
        Assert.Equal(50, body.Data.Count);
    }

    [Fact]
    public async Task GetDailySchedule_Page2_ReturnsRemainingRows()
    {
        await using var db = BuildDb();
        var today = DateTime.UtcNow.Date;

        for (var i = 0; i < 55; i++)
            await SeedAppointmentAsync(db, today.AddMinutes(i * 10));

        var result = await GetDailyScheduleEndpoint.HandleGetDailySchedule(db, null, 2, CancellationToken.None);
        var body = ResponseValue<SchedulePageResponse>(result);

        Assert.Equal(55, body.TotalCount);
        Assert.Equal(5, body.Data.Count);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Excluded: Cancelled appointments
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetDailySchedule_CancelledAppointment_IsExcluded()
    {
        await using var db = BuildDb();
        var today = DateTime.UtcNow.Date;

        await SeedAppointmentAsync(db, today.AddHours(9));                                      // Scheduled — included
        await SeedAppointmentAsync(db, today.AddHours(10), AppointmentStatus.Cancelled);        // Cancelled — excluded

        var result = await GetDailyScheduleEndpoint.HandleGetDailySchedule(db, null, 1, CancellationToken.None);
        var body = ResponseValue<SchedulePageResponse>(result);

        Assert.Equal(1, body.TotalCount);
        Assert.NotEqual("Cancelled", body.Data[0].Status);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Edge — no appointments for date → empty list with totalCount=0
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetDailySchedule_NoAppointmentsForDate_ReturnsEmpty()
    {
        await using var db = BuildDb();
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));

        var result = await GetDailyScheduleEndpoint.HandleGetDailySchedule(db, yesterday, 1, CancellationToken.None);
        var body = ResponseValue<SchedulePageResponse>(result);

        Assert.Equal(0, body.TotalCount);
        Assert.Equal(0, body.PageCount);
        Assert.Empty(body.Data);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-002 — NA intake status for walk-in patients with no IntakeRecord
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetDailySchedule_WalkInPatient_NoIntake_IntakeStatusIsNA()
    {
        await using var db = BuildDb();
        var today = DateTime.UtcNow.Date;

        // Seed a walk-in patient explicitly.
        var walkInPatient = new UserAccount
        {
            Email        = $"walkin-{Guid.NewGuid()}@walkin.internal",
            PasswordHash = "",
            Role         = "patient",
            FirstName    = "Walk",
            LastName     = "In",
            WalkIn       = true,
        };
        db.UserAccounts.Add(walkInPatient);
        await db.SaveChangesAsync();

        var slot = new Slot { SlotTime = today.AddHours(9), DurationMinutes = 30, IsAvailable = false };
        db.Slots.Add(slot);
        await db.SaveChangesAsync();

        db.Appointments.Add(new Appointment
        {
            PatientId = walkInPatient.Id,
            SlotId    = slot.Id,
            Status    = AppointmentStatus.Arrived,
        });
        await db.SaveChangesAsync();
        // No IntakeRecord for this walk-in patient.

        var result = await GetDailyScheduleEndpoint.HandleGetDailySchedule(db, null, 1, CancellationToken.None);
        var body = ResponseValue<SchedulePageResponse>(result);

        Assert.Single(body.Data);
        Assert.Equal("NA", body.Data[0].IntakeStatus);
    }
}
