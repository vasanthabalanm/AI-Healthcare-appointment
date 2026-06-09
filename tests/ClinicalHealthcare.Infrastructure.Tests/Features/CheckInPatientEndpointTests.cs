using ClinicalHealthcare.Api.Features.Staff;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for TASK_035 — Staff patient arrival check-in.
/// Covers AC-001 to AC-005 and edge cases.
/// </summary>
public sealed class CheckInPatientEndpointTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ApplicationDbContext BuildDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static int StatusCode(Microsoft.AspNetCore.Http.IResult result)
    {
        if (result is IStatusCodeHttpResult sc && sc.StatusCode is not null)
            return sc.StatusCode.Value;
        return (int)(result.GetType().GetProperty("StatusCode")?.GetValue(result) ?? 0);
    }

    private static HttpContext BuildStaffContext(int staffId = 1)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, staffId.ToString())],
            "TestAuth"));
        return ctx;
    }

    private static async Task<(UserAccount staff, UserAccount patient, Slot slot, Appointment appt)>
        SeedScheduledAppointmentAsync(ApplicationDbContext db)
    {
        var staff = new UserAccount { Email = "staff@test.com", PasswordHash = "", Role = "staff", FirstName = "S", LastName = "T" };
        var patient = new UserAccount { Email = "p@test.com", PasswordHash = "", Role = "patient", FirstName = "P", LastName = "L" };
        db.UserAccounts.AddRange(staff, patient);
        await db.SaveChangesAsync();

        var slot = new Slot { SlotTime = DateTime.UtcNow.AddHours(1), DurationMinutes = 30, IsAvailable = false };
        db.Slots.Add(slot);
        await db.SaveChangesAsync();

        var appt = new Appointment
        {
            PatientId = patient.Id,
            SlotId    = slot.Id,
            Status    = AppointmentStatus.Scheduled,
        };
        db.Appointments.Add(appt);
        await db.SaveChangesAsync();

        return (staff, patient, slot, appt);
    }

    // ── Subclass that throws DbUpdateConcurrencyException on next SaveChanges ─

    private sealed class ThrowingConcurrencyDbContext : ApplicationDbContext
    {
        public bool ThrowOnNextSave { get; set; }

        public ThrowingConcurrencyDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public override Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            if (ThrowOnNextSave)
            {
                ThrowOnNextSave = false;
                throw new DbUpdateConcurrencyException("Simulated concurrent edit", []);
            }
            return base.SaveChangesAsync(ct);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-001 — Scheduled → Arrived transition
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CheckIn_ScheduledAppointment_Returns200AndStatusArrived()
    {
        await using var db = BuildDb();
        var (staff, _, _, appt) = await SeedScheduledAppointmentAsync(db);
        var ctx = BuildStaffContext(staff.Id);

        var result = await CheckInPatientEndpoint.HandleCheckIn(appt.Id, ctx, db, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));

        var reloaded = await db.Appointments.FindAsync(appt.Id);
        Assert.Equal(AppointmentStatus.Arrived, reloaded!.Status);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-002 — FSM guard: already-Arrived appointment → 409
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CheckIn_AlreadyArrivedAppointment_Returns409()
    {
        await using var db = BuildDb();
        var (staff, _, _, appt) = await SeedScheduledAppointmentAsync(db);
        appt.Status = AppointmentStatus.Arrived;
        await db.SaveChangesAsync();
        var ctx = BuildStaffContext(staff.Id);

        var result = await CheckInPatientEndpoint.HandleCheckIn(appt.Id, ctx, db, CancellationToken.None);

        Assert.Equal(409, StatusCode(result));
    }

    [Fact]
    public async Task CheckIn_CompletedAppointment_Returns409()
    {
        await using var db = BuildDb();
        var (staff, _, _, appt) = await SeedScheduledAppointmentAsync(db);
        appt.Status = AppointmentStatus.Completed;
        await db.SaveChangesAsync();
        var ctx = BuildStaffContext(staff.Id);

        var result = await CheckInPatientEndpoint.HandleCheckIn(appt.Id, ctx, db, CancellationToken.None);

        Assert.Equal(409, StatusCode(result));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-003 — QueueEntry set to CheckedIn on check-in
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CheckIn_WithQueueEntry_SetsQueueEntryCheckedIn()
    {
        await using var db = BuildDb();
        var (staff, patient, _, appt) = await SeedScheduledAppointmentAsync(db);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var qe = new QueueEntry
        {
            PatientId      = patient.Id,
            QueueDate      = today,
            Position       = 1,
            Status         = QueueStatus.Waiting,
            IsWalkIn       = true,
            AddedByStaffId = staff.Id,
        };
        db.QueueEntries.Add(qe);
        await db.SaveChangesAsync();

        var ctx = BuildStaffContext(staff.Id);
        var result = await CheckInPatientEndpoint.HandleCheckIn(appt.Id, ctx, db, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));
        var reloadedQe = await db.QueueEntries.FindAsync(qe.Id);
        Assert.Equal(QueueStatus.CheckedIn, reloadedQe!.Status);
    }

    [Fact]
    public async Task CheckIn_NoQueueEntry_StillReturns200()
    {
        await using var db = BuildDb();
        var (staff, _, _, appt) = await SeedScheduledAppointmentAsync(db);
        // No QueueEntry seeded — booked-online patient.
        var ctx = BuildStaffContext(staff.Id);

        var result = await CheckInPatientEndpoint.HandleCheckIn(appt.Id, ctx, db, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-004 — Rowversion: concurrent check-in → 409
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CheckIn_ConcurrentUpdate_Returns409()
    {
        await using var db = new ThrowingConcurrencyDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        var staff = new UserAccount { Email = "s@t.com", PasswordHash = "", Role = "staff", FirstName = "S", LastName = "T" };
        var patient = new UserAccount { Email = "p@t.com", PasswordHash = "", Role = "patient", FirstName = "P", LastName = "L" };
        db.UserAccounts.AddRange(staff, patient);
        await db.SaveChangesAsync();

        var slot = new Slot { SlotTime = DateTime.UtcNow.AddHours(1), DurationMinutes = 30, IsAvailable = false };
        db.Slots.Add(slot);
        await db.SaveChangesAsync();

        var appt = new Appointment { PatientId = patient.Id, SlotId = slot.Id, Status = AppointmentStatus.Scheduled };
        db.Appointments.Add(appt);
        await db.SaveChangesAsync();

        db.ThrowOnNextSave = true;
        var ctx = BuildStaffContext(staff.Id);

        var result = await CheckInPatientEndpoint.HandleCheckIn(appt.Id, ctx, db, CancellationToken.None);

        Assert.Equal(409, StatusCode(result));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-005 — AuditLog entry written on successful check-in
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CheckIn_WritesAuditLog()
    {
        await using var db = BuildDb();
        var (staff, _, _, appt) = await SeedScheduledAppointmentAsync(db);
        var ctx = BuildStaffContext(staff.Id);

        await CheckInPatientEndpoint.HandleCheckIn(appt.Id, ctx, db, CancellationToken.None);

        var audit = await db.AuditLogs
            .FirstOrDefaultAsync(a => a.EntityType == "Appointment" && a.EntityId == appt.Id && a.Action == "CheckIn");
        Assert.NotNull(audit);
        Assert.Equal(staff.Id, audit.ActorId);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Error paths
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CheckIn_UnknownAppointmentId_Returns404()
    {
        await using var db = BuildDb();
        var ctx = BuildStaffContext(1);

        var result = await CheckInPatientEndpoint.HandleCheckIn(9999, ctx, db, CancellationToken.None);

        Assert.Equal(404, StatusCode(result));
    }

    [Fact]
    public async Task CheckIn_MissingSubClaim_Returns401()
    {
        await using var db = BuildDb();
        var (_, _, _, appt) = await SeedScheduledAppointmentAsync(db);
        var ctx = new DefaultHttpContext();   // no sub claim

        var result = await CheckInPatientEndpoint.HandleCheckIn(appt.Id, ctx, db, CancellationToken.None);

        Assert.Equal(401, StatusCode(result));
    }
}
