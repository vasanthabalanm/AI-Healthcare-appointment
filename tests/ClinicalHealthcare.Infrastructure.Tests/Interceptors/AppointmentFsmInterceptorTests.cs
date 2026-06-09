using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Interceptors;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Interceptors;

/// <summary>
/// Unit tests for <see cref="AppointmentFsmInterceptor"/>.
///
/// Uses EF Core InMemory with a single context per test.  After the first
/// <c>SaveChangesAsync</c>, EF Core resets the entity to <c>Unchanged</c> and
/// sets <c>OriginalValues</c> to the persisted state.  Modifying <c>Status</c>
/// and calling <c>SaveChangesAsync</c> a second time puts the entity into
/// <c>Modified</c> — exactly what the interceptor inspects.
/// </summary>
public sealed class AppointmentFsmInterceptorTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .AddInterceptors(new AppointmentFsmInterceptor())
            .Options;

        return new ApplicationDbContext(options);
    }

    /// <summary>
    /// Creates a persisted <see cref="Appointment"/> with <paramref name="initialStatus"/>
    /// in the returned context.  After this call the appointment is tracked as
    /// <c>Unchanged</c> with <c>OriginalValues.Status == initialStatus</c>.
    /// </summary>
    private static async Task<(Appointment appt, ApplicationDbContext ctx)> SeedAppointmentAsync(
        AppointmentStatus initialStatus)
    {
        var ctx = CreateContext();

        var patient = new UserAccount { Email = $"{Guid.NewGuid()}@test.com", Role = "patient" };
        var slot    = new Slot { SlotTime = DateTime.UtcNow.AddDays(1), DurationMinutes = 30 };
        ctx.UserAccounts.Add(patient);
        ctx.Slots.Add(slot);
        await ctx.SaveChangesAsync();

        var appt = new Appointment
        {
            PatientId = patient.Id,
            SlotId    = slot.Id,
            Status    = initialStatus,
            BookedAt  = DateTime.UtcNow
        };
        ctx.Appointments.Add(appt);
        // After this SaveChanges, EF Core marks the entity Unchanged and sets
        // OriginalValues[Status] = initialStatus — the interceptor reads those values.
        await ctx.SaveChangesAsync();

        return (appt, ctx);
    }

    // ── Happy-path: valid transitions should NOT throw ────────────────────

    [Theory]
    [InlineData(AppointmentStatus.Scheduled, AppointmentStatus.Arrived)]
    [InlineData(AppointmentStatus.Scheduled, AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Scheduled, AppointmentStatus.NoShow)]
    [InlineData(AppointmentStatus.Arrived,   AppointmentStatus.Completed)]
    public async Task ValidTransition_DoesNotThrow(AppointmentStatus from, AppointmentStatus to)
    {
        var (appt, ctx) = await SeedAppointmentAsync(from);
        await using (ctx)
        {
            appt.Status = to;
            // Should complete without exception
            await ctx.SaveChangesAsync();
        }
    }

    // ── Invalid transitions: should throw InvalidOperationException ────────

    [Theory]
    [InlineData(AppointmentStatus.Completed, AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.Completed, AppointmentStatus.Arrived)]
    [InlineData(AppointmentStatus.Cancelled, AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.Cancelled, AppointmentStatus.Arrived)]
    [InlineData(AppointmentStatus.NoShow,    AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.NoShow,    AppointmentStatus.Arrived)]
    [InlineData(AppointmentStatus.Arrived,   AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Arrived,   AppointmentStatus.Scheduled)]
    public async Task InvalidTransition_ThrowsInvalidOperationException(
        AppointmentStatus from, AppointmentStatus to)
    {
        var (appt, ctx) = await SeedAppointmentAsync(from);
        await using (ctx)
        {
            appt.Status = to;
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => ctx.SaveChangesAsync());
        }
    }

    // ── No-op guard: same-status update should not throw ──────────────────

    [Theory]
    [InlineData(AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.Arrived)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    public async Task SameStatus_NoOp_DoesNotThrow(AppointmentStatus status)
    {
        var (appt, ctx) = await SeedAppointmentAsync(status);
        await using (ctx)
        {
            // Touch another field; leave Status unchanged — FSM guard must not fire
            appt.BookedAt = DateTime.UtcNow.AddSeconds(1);
            appt.Status   = status;
            await ctx.SaveChangesAsync();
        }
    }

    // ── Insert bypass: new appointments bypass FSM (EntityState.Added) ────

    [Fact]
    public async Task NewAppointment_WithNonScheduledStatus_BypassesFsmGuard()
    {
        // New inserts are EntityState.Added — interceptor only validates Modified entries.
        // This ensures new rows can be seeded freely without FSM constraints.
        await using var ctx = CreateContext();

        var patient = new UserAccount { Email = "seed@test.com", Role = "patient" };
        var slot    = new Slot { SlotTime = DateTime.UtcNow.AddDays(1), DurationMinutes = 15 };
        ctx.UserAccounts.Add(patient);
        ctx.Slots.Add(slot);
        await ctx.SaveChangesAsync();

        // Status = Completed on a brand-new entity — must NOT throw (insert, not update)
        var appt = new Appointment
        {
            PatientId = patient.Id,
            SlotId    = slot.Id,
            Status    = AppointmentStatus.Completed,
            BookedAt  = DateTime.UtcNow
        };
        ctx.Appointments.Add(appt);
        await ctx.SaveChangesAsync(); // must not throw
    }

    // ── Error message: exception text includes from/to states ─────────────

    [Fact]
    public async Task InvalidTransition_ExceptionMessage_ContainsFromAndToStates()
    {
        var (appt, ctx) = await SeedAppointmentAsync(AppointmentStatus.Completed);
        await using (ctx)
        {
            appt.Status = AppointmentStatus.Scheduled;

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => ctx.SaveChangesAsync());

            Assert.Contains("Completed", ex.Message);
            Assert.Contains("Scheduled", ex.Message);
        }
    }
}

