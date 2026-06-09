using ClinicalHealthcare.Api.Features.Staff;
using ClinicalHealthcare.Infrastructure.Configuration;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for TASK_033 — Staff walk-in registration and queue.
/// Covers AC-001 to AC-005 and edge cases.
/// </summary>
public sealed class StaffWalkInEndpointTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ApplicationDbContext BuildDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static HttpContext BuildStaffContext(int staffId)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [
                    new System.Security.Claims.Claim(
                        System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub,
                        staffId.ToString()),
                    new System.Security.Claims.Claim(
                        System.Security.Claims.ClaimTypes.Role, "staff"),
                ],
                "TestAuth"));
        return ctx;
    }

    private static IOptions<AppSettings> Settings(int capacity = 20) =>
        Options.Create(new AppSettings { QueueCapacity = capacity });

    private static int StatusCode(IResult result)
    {
        if (result is IStatusCodeHttpResult sc && sc.StatusCode is not null)
            return sc.StatusCode.Value;
        return (int)(result.GetType().GetProperty("StatusCode")?.GetValue(result) ?? 0);
    }

    private static WalkInRequest ValidWalkIn(string first = "Jane", string last = "Smith", bool @override = false) =>
        new() { FirstName = first, LastName = last, DateOfBirth = new DateOnly(1990, 6, 15), Override = @override };

    // ══════════════════════════════════════════════════════════════════════════
    // SearchPatientsEndpoint tests (AC-001)
    // ══════════════════════════════════════════════════════════════════════════

    // ── AC-001: empty query + no dob → empty list (avoids full-table scan) ─────

    [Fact]
    public async Task Search_NoQueryNoDob_ReturnsEmpty()
    {
        await using var db = BuildDb();

        var result = await SearchPatientsEndpoint.HandleSearch(null, null, db, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));
        var ok = (Microsoft.AspNetCore.Http.HttpResults.Ok<PatientSearchResult[]>)result;
        Assert.Empty(ok.Value!);
    }

    // ── AC-001: partial name match returns matching patients ──────────────────

    [Fact]
    public async Task Search_PartialName_ReturnsMatchingPatients()
    {
        await using var db = BuildDb();
        db.UserAccounts.AddRange(
            new UserAccount { Email = "alice@test.com", PasswordHash = "", Role = "patient", FirstName = "Alice", LastName = "Smith" },
            new UserAccount { Email = "bob@test.com",   PasswordHash = "", Role = "patient", FirstName = "Bob",   LastName = "Jones"  },
            new UserAccount { Email = "carol@test.com", PasswordHash = "", Role = "staff",   FirstName = "Carol", LastName = "Smith"  }
        );
        await db.SaveChangesAsync();

        var result = await SearchPatientsEndpoint.HandleSearch("smith", null, db, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));
        var list = Assert.IsAssignableFrom<IReadOnlyList<PatientSearchResult>>(
            result.GetType().GetProperty("Value")?.GetValue(result));
        // Only the patient Alice Smith matches — Carol Smith is staff, Bob Jones doesn't match.
        Assert.Single(list, r => r.FullName == "Alice Smith");
    }

    // ── AC-001: DOB filter narrows results ────────────────────────────────────

    [Fact]
    public async Task Search_DobFilter_ReturnsMatchingPatient()
    {
        await using var db = BuildDb();
        var dob = new DateOnly(1985, 3, 22);
        db.UserAccounts.AddRange(
            new UserAccount { Email = "p1@test.com", PasswordHash = "", Role = "patient", FirstName = "Tom", LastName = "Brown", DateOfBirth = dob },
            new UserAccount { Email = "p2@test.com", PasswordHash = "", Role = "patient", FirstName = "Tom", LastName = "Brown", DateOfBirth = new DateOnly(1990, 1, 1) }
        );
        await db.SaveChangesAsync();

        var result = await SearchPatientsEndpoint.HandleSearch("Tom Brown", dob, db, CancellationToken.None);

        var list = Assert.IsAssignableFrom<IReadOnlyList<PatientSearchResult>>(
            result.GetType().GetProperty("Value")?.GetValue(result));
        Assert.Single(list);
        Assert.Equal(dob, list[0].Dob);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // RegisterWalkInEndpoint tests (AC-002 to AC-005)
    // ══════════════════════════════════════════════════════════════════════════

    // ── AC-002: walk-in creates patient with WalkIn=true + QueueEntry ─────────

    [Fact]
    public async Task RegisterWalkIn_NewPatient_Returns201AndCreatesAccount()
    {
        await using var db = BuildDb();

        var result = await RegisterWalkInEndpoint.HandleRegisterWalkIn(
            ValidWalkIn(), BuildStaffContext(1), db, Settings(), CancellationToken.None);

        Assert.Equal(201, StatusCode(result));
        var patient = db.UserAccounts.IgnoreQueryFilters().Single(u => u.WalkIn);
        Assert.True(patient.WalkIn);
        Assert.Equal("patient", patient.Role);
        Assert.Single(db.QueueEntries);
    }

    // ── AC-005: QueueEntry created with Position=1 for first entry ────────────

    [Fact]
    public async Task RegisterWalkIn_FirstEntry_Position1()
    {
        await using var db = BuildDb();

        await RegisterWalkInEndpoint.HandleRegisterWalkIn(
            ValidWalkIn(), BuildStaffContext(1), db, Settings(), CancellationToken.None);

        var entry = db.QueueEntries.Single();
        Assert.Equal(1, entry.Position);
        Assert.Equal(QueueStatus.Waiting, entry.Status);
        Assert.True(entry.IsWalkIn);
    }

    // ── AC-005: second entry gets Position=2 ─────────────────────────────────

    [Fact]
    public async Task RegisterWalkIn_SecondEntry_Position2()
    {
        await using var db = BuildDb();

        await RegisterWalkInEndpoint.HandleRegisterWalkIn(
            ValidWalkIn("Alice", "Brown"), BuildStaffContext(1), db, Settings(), CancellationToken.None);
        await RegisterWalkInEndpoint.HandleRegisterWalkIn(
            ValidWalkIn("Bob", "Green"), BuildStaffContext(1), db, Settings(), CancellationToken.None);

        var entries = db.QueueEntries.OrderBy(q => q.Position).ToList();
        Assert.Equal(1, entries[0].Position);
        Assert.Equal(2, entries[1].Position);
    }

    // ── AC-003: exceeding capacity without override → 409 ─────────────────────

    [Fact]
    public async Task RegisterWalkIn_AtCapacity_NoOverride_Returns409()
    {
        await using var db = BuildDb();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Seed queue to exactly capacity (2).
        var staffAccount = new UserAccount { Email = "staff@test.com", PasswordHash = "", Role = "staff", FirstName = "S", LastName = "T" };
        var patAccount   = new UserAccount { Email = "p@test.com",     PasswordHash = "", Role = "patient", FirstName = "X", LastName = "Y" };
        db.UserAccounts.AddRange(staffAccount, patAccount);
        await db.SaveChangesAsync();

        db.QueueEntries.AddRange(
            new QueueEntry { PatientId = patAccount.Id, QueueDate = today, Position = 1, AddedByStaffId = staffAccount.Id },
            new QueueEntry { PatientId = patAccount.Id, QueueDate = today, Position = 2, AddedByStaffId = staffAccount.Id }
        );
        await db.SaveChangesAsync();

        var result = await RegisterWalkInEndpoint.HandleRegisterWalkIn(
            ValidWalkIn(), BuildStaffContext(staffAccount.Id), db, Settings(capacity: 2), CancellationToken.None);

        Assert.Equal(409, StatusCode(result));
    }

    // ── AC-004: capacity override → 201; AuditLog entry written ──────────────

    [Fact]
    public async Task RegisterWalkIn_AtCapacity_WithOverride_Returns201AndAuditLog()
    {
        await using var db = BuildDb();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var staffAccount = new UserAccount { Email = "staff@test.com", PasswordHash = "", Role = "staff", FirstName = "S", LastName = "T" };
        var patAccount   = new UserAccount { Email = "p@test.com",     PasswordHash = "", Role = "patient", FirstName = "X", LastName = "Y" };
        db.UserAccounts.AddRange(staffAccount, patAccount);
        await db.SaveChangesAsync();

        db.QueueEntries.Add(
            new QueueEntry { PatientId = patAccount.Id, QueueDate = today, Position = 1, AddedByStaffId = staffAccount.Id }
        );
        await db.SaveChangesAsync();

        var result = await RegisterWalkInEndpoint.HandleRegisterWalkIn(
            ValidWalkIn(@override: true), BuildStaffContext(staffAccount.Id), db, Settings(capacity: 1), CancellationToken.None);

        Assert.Equal(201, StatusCode(result));
        var audit = db.AuditLogs.Single(a => a.Action == "QUEUE_OVERRIDE");
        Assert.Equal(nameof(QueueEntry), audit.EntityType);
        Assert.Equal(staffAccount.Id, audit.ActorId);
        Assert.True(audit.EntityId > 0, "AuditLog.EntityId must reference the inserted QueueEntry — back-fill UPDATE is not permitted on this table.");
    }

    // ── Edge: duplicate patient (same name+DOB) → linked, not duplicated ──────

    [Fact]
    public async Task RegisterWalkIn_ExistingPatient_LinksToExistingAccount()
    {
        await using var db = BuildDb();
        var dob = new DateOnly(1990, 6, 15);
        var existing = new UserAccount
        {
            Email = "existing@test.com", PasswordHash = "", Role = "patient",
            FirstName = "Jane", LastName = "Smith", DateOfBirth = dob,
        };
        db.UserAccounts.Add(existing);
        await db.SaveChangesAsync();

        var req = new WalkInRequest { FirstName = "Jane", LastName = "Smith", DateOfBirth = dob };
        await RegisterWalkInEndpoint.HandleRegisterWalkIn(
            req, BuildStaffContext(1), db, Settings(), CancellationToken.None);

        // Only one patient account should exist — no duplicate created.
        Assert.Single(db.UserAccounts.IgnoreQueryFilters().Where(u => u.Role == "patient"));
        // QueueEntry should point to the existing patient.
        Assert.Equal(existing.Id, db.QueueEntries.Single().PatientId);
    }

    // ── Validation: missing firstName → 422 ──────────────────────────────────

    [Fact]
    public async Task RegisterWalkIn_MissingFirstName_Returns422()
    {
        await using var db = BuildDb();
        var req = new WalkInRequest { FirstName = "", LastName = "Smith" };

        var result = await RegisterWalkInEndpoint.HandleRegisterWalkIn(
            req, BuildStaffContext(1), db, Settings(), CancellationToken.None);

        Assert.Equal(422, StatusCode(result));
    }

    // ── Validation: firstNameTooLong → 422 ───────────────────────────────────

    [Fact]
    public async Task RegisterWalkIn_FirstNameTooLong_Returns422()
    {
        await using var db = BuildDb();
        var req = new WalkInRequest { FirstName = new string('A', 101), LastName = "Smith" };

        var result = await RegisterWalkInEndpoint.HandleRegisterWalkIn(
            req, BuildStaffContext(1), db, Settings(), CancellationToken.None);

        Assert.Equal(422, StatusCode(result));
    }

    // ── Auth: missing sub claim → 401 ─────────────────────────────────────────

    [Fact]
    public async Task RegisterWalkIn_NoSubClaim_Returns401()
    {
        await using var db = BuildDb();
        var ctx = new DefaultHttpContext();   // no sub claim

        var result = await RegisterWalkInEndpoint.HandleRegisterWalkIn(
            ValidWalkIn(), ctx, db, Settings(), CancellationToken.None);

        Assert.Equal(401, StatusCode(result));
    }
}
