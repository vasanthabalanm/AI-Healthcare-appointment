using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ClinicalHealthcare.Api.Features.Appointments;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for TASK_020: POST /waitlist — patient join waitlist.
/// Covers AC-001, AC-002, AC-003, AC-004 and all edge cases.
/// </summary>
public sealed class JoinWaitlistEndpointTests
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

    private static HttpContext BuildPatientContext(int userId = 42)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())],
            "TestAuth"));
        return ctx;
    }

    private static UserAccount SeedPatient(ApplicationDbContext db)
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

    // ── AC-001: future date, no existing entry → 201 with Active entry ────────

    [Fact]
    public async Task JoinWaitlist_FutureDate_Returns201_ActiveEntry()
    {
        var db      = CreateDb();
        var patient = SeedPatient(db);
        var ctx     = BuildPatientContext(patient.Id);
        var request = new JoinWaitlistRequest(null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)));

        var result = await JoinWaitlistEndpoint.HandleJoinWaitlist(request, ctx, db, CancellationToken.None);

        Assert.Equal(201, StatusCode(result));

        db.ChangeTracker.Clear();
        var entry = await db.WaitlistEntries.SingleAsync();
        Assert.Equal(patient.Id, entry.PatientId);
        Assert.Equal(WaitlistStatus.Active, entry.Status);
        Assert.Null(entry.PreferredSlotId);
    }

    // ── AC-001: PreferredSlotId stored on entry when provided ─────────────────

    [Fact]
    public async Task JoinWaitlist_WithSlotId_StoresPreferredSlotId()
    {
        var db      = CreateDb();
        var patient = SeedPatient(db);
        var ctx     = BuildPatientContext(patient.Id);

        // Seed a real slot so the F1 existence check passes.
        var slot = new Slot
        {
            SlotTime        = DateTime.UtcNow.AddDays(1).AddHours(9),
            DurationMinutes = 30,
            IsAvailable     = true,
        };
        db.Slots.Add(slot);
        db.SaveChanges();

        var request = new JoinWaitlistRequest(slot.Id, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)));

        var result = await JoinWaitlistEndpoint.HandleJoinWaitlist(request, ctx, db, CancellationToken.None);

        Assert.Equal(201, StatusCode(result));

        db.ChangeTracker.Clear();
        var entry = await db.WaitlistEntries.SingleAsync();
        Assert.Equal(slot.Id, entry.PreferredSlotId);
    }

    // ── AC-001: today is accepted (boundary: not strictly past) ───────────────

    [Fact]
    public async Task JoinWaitlist_TodayDate_Returns201()
    {
        var db      = CreateDb();
        var patient = SeedPatient(db);
        var ctx     = BuildPatientContext(patient.Id);
        var today   = DateOnly.FromDateTime(DateTime.UtcNow);
        var request = new JoinWaitlistRequest(null, today);

        var result = await JoinWaitlistEndpoint.HandleJoinWaitlist(request, ctx, db, CancellationToken.None);

        Assert.Equal(201, StatusCode(result));
    }

    // ── AC-003: past date → 400 with spec-defined message body ────────────────────────────

    [Fact]
    public async Task JoinWaitlist_PastDate_Returns400()
    {
        var db      = CreateDb();
        var patient = SeedPatient(db);
        var ctx     = BuildPatientContext(patient.Id);
        var request = new JoinWaitlistRequest(null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));

        var result = await JoinWaitlistEndpoint.HandleJoinWaitlist(request, ctx, db, CancellationToken.None);

        Assert.Equal(400, StatusCode(result));

        // F2 fix: assert body matches the spec-specified message.
        var body   = result.GetType().GetProperty("Value")?.GetValue(result);
        var message = body?.GetType().GetProperty("error")?.GetValue(body)?.ToString();
        Assert.Equal("Cannot join waitlist for a past slot.", message);
    }

    // ── F1 fix: non-existent PreferredSlotId → 400 (not 409) ──────────────────────────────

    [Fact]
    public async Task JoinWaitlist_InvalidPreferredSlotId_Returns400()
    {
        var db      = CreateDb();
        var patient = SeedPatient(db);
        var ctx     = BuildPatientContext(patient.Id);
        // Slot ID 9999 does not exist in the empty InMemory DB.
        var request = new JoinWaitlistRequest(9999, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)));

        var result = await JoinWaitlistEndpoint.HandleJoinWaitlist(request, ctx, db, CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── AC-002 / AC-004: duplicate Active entry → 409 (application guard) ─────

    [Fact]
    public async Task JoinWaitlist_DuplicateActiveEntry_Returns409()
    {
        var db      = CreateDb();
        var patient = SeedPatient(db);
        var ctx     = BuildPatientContext(patient.Id);

        // Seed an existing Active entry for this patient.
        db.WaitlistEntries.Add(new WaitlistEntry
        {
            PatientId = patient.Id,
            Status    = WaitlistStatus.Active,
            QueuedAt  = DateTime.UtcNow.AddMinutes(-10),
        });
        await db.SaveChangesAsync();

        var request = new JoinWaitlistRequest(null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)));
        var result = await JoinWaitlistEndpoint.HandleJoinWaitlist(request, ctx, db, CancellationToken.None);

        Assert.Equal(409, StatusCode(result));
    }

    // ── Edge: Fulfilled/Expired entries do NOT block a new Active entry ────────

    [Fact]
    public async Task JoinWaitlist_FulfilledEntryExists_Returns201()
    {
        var db      = CreateDb();
        var patient = SeedPatient(db);
        var ctx     = BuildPatientContext(patient.Id);

        // A non-Active historical entry should not count as a duplicate.
        db.WaitlistEntries.Add(new WaitlistEntry
        {
            PatientId = patient.Id,
            Status    = WaitlistStatus.Fulfilled,
            QueuedAt  = DateTime.UtcNow.AddDays(-5),
        });
        await db.SaveChangesAsync();

        var request = new JoinWaitlistRequest(null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)));
        var result = await JoinWaitlistEndpoint.HandleJoinWaitlist(request, ctx, db, CancellationToken.None);

        Assert.Equal(201, StatusCode(result));
    }

    // ── Edge: missing JWT sub claim → 401 ────────────────────────────────────

    [Fact]
    public async Task JoinWaitlist_MissingSubClaim_Returns401()
    {
        var db  = CreateDb();
        var ctx = new DefaultHttpContext(); // no claims
        var request = new JoinWaitlistRequest(null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)));

        var result = await JoinWaitlistEndpoint.HandleJoinWaitlist(request, ctx, db, CancellationToken.None);

        Assert.Equal(401, StatusCode(result));
    }

    // ── Edge: QueuedAt is set to server time ─────────────────────────────────

    [Fact]
    public async Task JoinWaitlist_QueuedAt_SetToServerUtcNow()
    {
        var db      = CreateDb();
        var patient = SeedPatient(db);
        var ctx     = BuildPatientContext(patient.Id);
        var before  = DateTime.UtcNow;
        var request = new JoinWaitlistRequest(null, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)));

        await JoinWaitlistEndpoint.HandleJoinWaitlist(request, ctx, db, CancellationToken.None);

        db.ChangeTracker.Clear();
        var entry = await db.WaitlistEntries.SingleAsync();
        Assert.InRange(entry.QueuedAt, before, DateTime.UtcNow.AddSeconds(5));
    }
}
