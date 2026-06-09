using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Interceptors;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Entities;

/// <summary>
/// Unit tests for <see cref="UserAccount"/>, <see cref="Slot"/>, and <see cref="Appointment"/>
/// EF Core model configuration and <see cref="AppointmentFsmInterceptor"/> FSM transitions (US_007).
///
/// Model metadata tests (TC-001, TC-002) verify EF Core index / concurrency registration.
/// FSM tests (TC-004 through EC-002) verify the interceptor guards SaveChanges correctly.
/// SQL Server–level DDL and FK enforcement are integration-test concerns and are out of scope here.
/// </summary>
public sealed class UserAccountSlotAppointmentTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .AddInterceptors(new AppointmentFsmInterceptor(), new WaitlistGuardInterceptor())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task<UserAccount> SeedPatientAsync(ApplicationDbContext ctx)
    {
        var patient = new UserAccount { Email = $"{Guid.NewGuid()}@test.com", Role = "patient" };
        ctx.UserAccounts.Add(patient);
        await ctx.SaveChangesAsync();
        return patient;
    }

    private static async Task<Slot> SeedSlotAsync(ApplicationDbContext ctx)
    {
        var slot = new Slot { SlotTime = DateTime.UtcNow.AddDays(1), DurationMinutes = 30 };
        ctx.Slots.Add(slot);
        await ctx.SaveChangesAsync();
        return slot;
    }

    /// <summary>Seeds an Appointment with the given initial status directly (no FSM guard on INSERT).</summary>
    private static async Task<Appointment> SeedAppointmentAsync(
        ApplicationDbContext ctx, AppointmentStatus initialStatus = AppointmentStatus.Scheduled)
    {
        var patient = await SeedPatientAsync(ctx);
        var slot    = await SeedSlotAsync(ctx);
        var appt    = new Appointment { PatientId = patient.Id, SlotId = slot.Id, Status = initialStatus };
        ctx.Appointments.Add(appt);
        await ctx.SaveChangesAsync();
        return appt;
    }

    // ── TC-001: UserAccount Email unique index in EF Core model ─────────────

    [Fact]
    public void UserAccount_Email_HasUniqueIndexInModel()
    {
        using var ctx = CreateContext();

        var entityType = ctx.Model.FindEntityType(typeof(UserAccount))!;
        var hasUniqueEmailIndex = entityType.GetIndexes()
            .Any(i => i.IsUnique && i.Properties.Any(p => p.Name == nameof(UserAccount.Email)));

        Assert.True(hasUniqueEmailIndex);
    }

    // ── TC-002: Slot RowVersion concurrency token registered in model ────────

    [Fact]
    public void Slot_RowVersion_IsConcurrencyTokenInModel()
    {
        using var ctx = CreateContext();

        var entityType = ctx.Model.FindEntityType(typeof(Slot))!;
        var rowVersionProp = entityType.FindProperty(nameof(Slot.RowVersion))!;

        Assert.True(rowVersionProp.IsConcurrencyToken);
    }

    // ── TC-003: Appointment can be inserted with valid PatientId and SlotId ──

    [Fact]
    public async Task Appointment_CanBeInserted_WithValidPatientAndSlot()
    {
        await using var ctx = CreateContext();
        var patient = await SeedPatientAsync(ctx);
        var slot    = await SeedSlotAsync(ctx);

        var appt = new Appointment { PatientId = patient.Id, SlotId = slot.Id };
        ctx.Appointments.Add(appt);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.Appointments.FindAsync(appt.Id);
        Assert.NotNull(loaded);
        Assert.Equal(patient.Id, loaded!.PatientId);
        Assert.Equal(slot.Id, loaded.SlotId);
        Assert.Equal(AppointmentStatus.Scheduled, loaded.Status);
    }

    // ── TC-004: Valid FSM — Scheduled → Arrived ──────────────────────────────

    [Fact]
    public async Task Appointment_FsmTransition_Scheduled_To_Arrived_Succeeds()
    {
        await using var ctx = CreateContext();
        var appt = await SeedAppointmentAsync(ctx, AppointmentStatus.Scheduled);

        appt.Status = AppointmentStatus.Arrived;
        await ctx.SaveChangesAsync();

        var loaded = await ctx.Appointments.FindAsync(appt.Id);
        Assert.Equal(AppointmentStatus.Arrived, loaded!.Status);
    }

    // ── TC-005: Valid FSM — Arrived → Completed ──────────────────────────────

    [Fact]
    public async Task Appointment_FsmTransition_Arrived_To_Completed_Succeeds()
    {
        await using var ctx = CreateContext();
        // Seed directly as Arrived (INSERT bypasses FSM guard — verified by EC-001)
        var appt = await SeedAppointmentAsync(ctx, AppointmentStatus.Arrived);

        appt.Status = AppointmentStatus.Completed;
        await ctx.SaveChangesAsync();

        var loaded = await ctx.Appointments.FindAsync(appt.Id);
        Assert.Equal(AppointmentStatus.Completed, loaded!.Status);
    }

    // ── TC-006: Invalid FSM — Scheduled → Completed throws ──────────────────

    [Fact]
    public async Task Appointment_FsmTransition_Scheduled_To_Completed_ThrowsInvalidOperationException()
    {
        await using var ctx = CreateContext();
        var appt = await SeedAppointmentAsync(ctx, AppointmentStatus.Scheduled);

        appt.Status = AppointmentStatus.Completed;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
        Assert.Contains("Scheduled", ex.Message);
    }

    // ── EC-001: New Appointment INSERT (State = Added) skips FSM guard ───────

    [Fact]
    public async Task Appointment_NewInsert_DoesNotTriggerFsmCheck()
    {
        await using var ctx = CreateContext();
        var patient = await SeedPatientAsync(ctx);
        var slot    = await SeedSlotAsync(ctx);

        // Initial insert with any status must succeed — no prior state to transition from
        var appt = new Appointment
        {
            PatientId = patient.Id,
            SlotId    = slot.Id,
            Status    = AppointmentStatus.Scheduled
        };
        ctx.Appointments.Add(appt);

        // Must complete without exception
        await ctx.SaveChangesAsync();

        Assert.Equal(1, await ctx.Appointments.CountAsync());
    }

    // ── EC-002: Terminal state — Cancelled → Arrived throws ─────────────────

    [Fact]
    public async Task Appointment_FsmTransition_Cancelled_To_Arrived_ThrowsInvalidOperationException()
    {
        await using var ctx = CreateContext();
        // Cancelled has no allowed transitions in ValidTransitions dict
        var appt = await SeedAppointmentAsync(ctx, AppointmentStatus.Cancelled);

        appt.Status = AppointmentStatus.Arrived;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
        Assert.Contains("Cancelled", ex.Message);
    }
}
