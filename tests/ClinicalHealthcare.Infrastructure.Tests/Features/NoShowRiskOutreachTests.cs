using ClinicalHealthcare.Api.Features.Staff;
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
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for TASK_037 — No-show risk alerts + outreach recording.
/// Covers AC-001 to AC-004, FSM edge cases, and error paths.
/// </summary>
public sealed class NoShowRiskOutreachTests
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

    private static HttpContext BuildStaffContext(int staffId = 1)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, staffId.ToString())],
            "TestAuth"));
        return ctx;
    }

    private static (Mock<ICacheService> cache, Mock<IBackgroundJobClient> jobs) CreateMocks()
        => (new Mock<ICacheService>(), new Mock<IBackgroundJobClient>());

    private static async Task<(UserAccount staff, UserAccount patient, Slot slot, Appointment appt)>
        SeedAppointmentAsync(
            ApplicationDbContext db,
            DateTime slotTime,
            AppointmentStatus status = AppointmentStatus.Scheduled,
            bool isHighRisk = false,
            int riskScore = 0)
    {
        var staff   = new UserAccount { Email = $"s-{Guid.NewGuid()}@t.com", PasswordHash = "", Role = "staff",   FirstName = "S", LastName = "T" };
        var patient = new UserAccount { Email = $"p-{Guid.NewGuid()}@t.com", PasswordHash = "", Role = "patient", FirstName = "P", LastName = "L" };
        db.UserAccounts.AddRange(staff, patient);
        await db.SaveChangesAsync();

        var slot = new Slot { SlotTime = slotTime, DurationMinutes = 30, IsAvailable = false };
        db.Slots.Add(slot);
        await db.SaveChangesAsync();

        var appt = new Appointment
        {
            PatientId        = patient.Id,
            SlotId           = slot.Id,
            Status           = status,
            IsHighRisk       = isHighRisk,
            NoShowRiskScore  = riskScore,
        };
        db.Appointments.Add(appt);
        await db.SaveChangesAsync();

        return (staff, patient, slot, appt);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-001 — GET /schedule/high-risk returns IsHighRisk=true appointments
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetHighRisk_ReturnsOnlyHighRiskAppointments()
    {
        await using var db = BuildDb();
        var today = DateTime.UtcNow.Date;

        await SeedAppointmentAsync(db, today.AddHours(9),  isHighRisk: true,  riskScore: 80);
        await SeedAppointmentAsync(db, today.AddHours(10), isHighRisk: false, riskScore: 20);

        var result = await GetHighRiskAppointmentsEndpoint.HandleGetHighRisk(db, null, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));
        var list = ResponseValue<IReadOnlyList<HighRiskEntryDto>>(result);
        Assert.Single(list);
        Assert.Equal(80, list[0].RiskScore);
    }

    [Fact]
    public async Task GetHighRisk_ExcludesCancelledAppointments()
    {
        await using var db = BuildDb();
        var today = DateTime.UtcNow.Date;

        await SeedAppointmentAsync(db, today.AddHours(9), isHighRisk: true, status: AppointmentStatus.Cancelled);

        var result = await GetHighRiskAppointmentsEndpoint.HandleGetHighRisk(db, null, CancellationToken.None);
        var list = ResponseValue<IReadOnlyList<HighRiskEntryDto>>(result);
        Assert.Empty(list);
    }

    [Fact]
    public async Task GetHighRisk_DateParam_FiltersToThatDate()
    {
        await using var db = BuildDb();
        var today     = DateTime.UtcNow.Date;
        var yesterday = today.AddDays(-1);

        await SeedAppointmentAsync(db, yesterday.AddHours(9), isHighRisk: true);
        await SeedAppointmentAsync(db, today.AddHours(9),     isHighRisk: true);

        var result = await GetHighRiskAppointmentsEndpoint.HandleGetHighRisk(
            db, DateOnly.FromDateTime(yesterday), CancellationToken.None);
        var list = ResponseValue<IReadOnlyList<HighRiskEntryDto>>(result);
        Assert.Single(list);
        Assert.Equal(DateOnly.FromDateTime(yesterday), DateOnly.FromDateTime(list[0].SlotTime));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-002 — POST /outreach creates OutreachRecord
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RecordOutreach_ValidRequest_Returns201AndPersistsRecord()
    {
        await using var db = BuildDb();
        var today = DateTime.UtcNow.Date;
        var (staff, _, _, appt) = await SeedAppointmentAsync(db, today.AddHours(9));
        var ctx = BuildStaffContext(staff.Id);

        var result = await RecordOutreachEndpoint.HandleRecordOutreach(
            appt.Id, new OutreachRequest("Called, no answer"), ctx, db, CancellationToken.None);

        Assert.Equal(201, StatusCode(result));

        var saved = await db.OutreachRecords.FirstOrDefaultAsync(o => o.AppointmentId == appt.Id);
        Assert.NotNull(saved);
        Assert.Equal("Called, no answer", saved.Notes);
        Assert.Equal(staff.Id, saved.StaffId);
    }

    [Fact]
    public async Task RecordOutreach_UnknownAppointment_Returns404()
    {
        await using var db = BuildDb();
        var ctx = BuildStaffContext(1);

        var result = await RecordOutreachEndpoint.HandleRecordOutreach(
            9999, new OutreachRequest("test"), ctx, db, CancellationToken.None);

        Assert.Equal(404, StatusCode(result));
    }

    [Fact]
    public async Task RecordOutreach_MissingSubClaim_Returns401()
    {
        await using var db = BuildDb();
        var today = DateTime.UtcNow.Date;
        var (_, _, _, appt) = await SeedAppointmentAsync(db, today.AddHours(9));
        var ctx = new DefaultHttpContext();  // no sub claim

        var result = await RecordOutreachEndpoint.HandleRecordOutreach(
            appt.Id, new OutreachRequest("test"), ctx, db, CancellationToken.None);

        Assert.Equal(401, StatusCode(result));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-003 — PATCH /status NoShow transitions FSM + writes AuditLog
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateStatus_NoShow_Returns200AndStatusIsNoShow()
    {
        await using var db = BuildDb();
        var today = DateTime.UtcNow.Date;
        var (staff, _, _, appt) = await SeedAppointmentAsync(db, today.AddHours(9));
        var (cache, jobs) = CreateMocks();
        var ctx = BuildStaffContext(staff.Id);

        var result = await UpdateAppointmentStatusEndpoint.HandleUpdateStatus(
            appt.Id, new UpdateStatusRequest("NoShow"), ctx, db, cache.Object, jobs.Object, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));
        var reloaded = await db.Appointments.FindAsync(appt.Id);
        Assert.Equal(AppointmentStatus.NoShow, reloaded!.Status);
    }

    [Fact]
    public async Task UpdateStatus_NoShow_WritesAuditLog()
    {
        await using var db = BuildDb();
        var today = DateTime.UtcNow.Date;
        var (staff, _, _, appt) = await SeedAppointmentAsync(db, today.AddHours(9));
        var (cache, jobs) = CreateMocks();
        var ctx = BuildStaffContext(staff.Id);

        await UpdateAppointmentStatusEndpoint.HandleUpdateStatus(
            appt.Id, new UpdateStatusRequest("NoShow"), ctx, db, cache.Object, jobs.Object, CancellationToken.None);

        var audit = await db.AuditLogs.FirstOrDefaultAsync(
            a => a.EntityType == "Appointment" && a.EntityId == appt.Id && a.Action == "NoShow");
        Assert.NotNull(audit);
        Assert.Equal(staff.Id, audit.ActorId);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-004 — Slot released + SwapMonitorJob enqueued
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateStatus_NoShow_ReleasesSlotAndEnqueuesSwapJob()
    {
        await using var db = BuildDb();
        var today = DateTime.UtcNow.Date;
        var (staff, _, slot, appt) = await SeedAppointmentAsync(db, today.AddHours(9));
        var (cache, jobs) = CreateMocks();
        var ctx = BuildStaffContext(staff.Id);

        await UpdateAppointmentStatusEndpoint.HandleUpdateStatus(
            appt.Id, new UpdateStatusRequest("NoShow"), ctx, db, cache.Object, jobs.Object, CancellationToken.None);

        var reloadedSlot = await db.Slots.FindAsync(slot.Id);
        Assert.True(reloadedSlot!.IsAvailable);
        jobs.Verify(j => j.Create(
            It.Is<Job>(job => job.Type == typeof(SwapMonitorJob)),
            It.IsAny<IState>()),
            Times.Once);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // FSM edge cases — Arrived→NoShow and Cancelled→NoShow → 409
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateStatus_ArrivedAppointment_Returns409()
    {
        await using var db = BuildDb();
        var today = DateTime.UtcNow.Date;
        var (staff, _, _, appt) = await SeedAppointmentAsync(db, today.AddHours(9), AppointmentStatus.Arrived);
        var (cache, jobs) = CreateMocks();
        var ctx = BuildStaffContext(staff.Id);

        var result = await UpdateAppointmentStatusEndpoint.HandleUpdateStatus(
            appt.Id, new UpdateStatusRequest("NoShow"), ctx, db, cache.Object, jobs.Object, CancellationToken.None);

        Assert.Equal(409, StatusCode(result));
    }

    [Fact]
    public async Task UpdateStatus_CancelledAppointment_Returns409()
    {
        await using var db = BuildDb();
        var today = DateTime.UtcNow.Date;
        var (staff, _, _, appt) = await SeedAppointmentAsync(db, today.AddHours(9), AppointmentStatus.Cancelled);
        var (cache, jobs) = CreateMocks();
        var ctx = BuildStaffContext(staff.Id);

        var result = await UpdateAppointmentStatusEndpoint.HandleUpdateStatus(
            appt.Id, new UpdateStatusRequest("NoShow"), ctx, db, cache.Object, jobs.Object, CancellationToken.None);

        Assert.Equal(409, StatusCode(result));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Error paths
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task UpdateStatus_UnknownStatus_Returns400()
    {
        await using var db = BuildDb();
        var today = DateTime.UtcNow.Date;
        var (staff, _, _, appt) = await SeedAppointmentAsync(db, today.AddHours(9));
        var (cache, jobs) = CreateMocks();
        var ctx = BuildStaffContext(staff.Id);

        var result = await UpdateAppointmentStatusEndpoint.HandleUpdateStatus(
            appt.Id, new UpdateStatusRequest("Completed"), ctx, db, cache.Object, jobs.Object, CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    [Fact]
    public async Task UpdateStatus_UnknownAppointment_Returns404()
    {
        await using var db = BuildDb();
        var (cache, jobs) = CreateMocks();
        var ctx = BuildStaffContext(1);

        var result = await UpdateAppointmentStatusEndpoint.HandleUpdateStatus(
            9999, new UpdateStatusRequest("NoShow"), ctx, db, cache.Object, jobs.Object, CancellationToken.None);

        Assert.Equal(404, StatusCode(result));
    }

    [Fact]
    public async Task UpdateStatus_MissingSubClaim_Returns401()
    {
        await using var db = BuildDb();
        var today = DateTime.UtcNow.Date;
        var (_, _, _, appt) = await SeedAppointmentAsync(db, today.AddHours(9));
        var (cache, jobs) = CreateMocks();
        var ctx = new DefaultHttpContext();  // no sub claim

        var result = await UpdateAppointmentStatusEndpoint.HandleUpdateStatus(
            appt.Id, new UpdateStatusRequest("NoShow"), ctx, db, cache.Object, jobs.Object, CancellationToken.None);

        Assert.Equal(401, StatusCode(result));
    }
}
