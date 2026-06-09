using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Interceptors;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Entities;

/// <summary>
/// Unit tests for <see cref="WaitlistEntry"/> filtered unique index behaviour
/// and <see cref="IntakeRecord"/> versioning + default query filter.
///
/// EF Core InMemory is used: both the seed and test operations share a single
/// context per test so change tracking is consistent.
/// </summary>
public sealed class WaitlistEntryIntakeRecordTests
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

    // ══════════════════════════════════════════════════════════════════════════
    // WaitlistEntry — filtered unique index
    // (InMemory provider does not enforce DB unique constraints, so we validate
    //  the application-layer invariant by checking the data directly.)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task WaitlistEntry_SingleActive_PerPatient_IsAllowed()
    {
        await using var ctx = CreateContext();
        var patient = await SeedPatientAsync(ctx);

        ctx.WaitlistEntries.Add(new WaitlistEntry { PatientId = patient.Id });
        await ctx.SaveChangesAsync();

        var count = await ctx.WaitlistEntries
            .Where(w => w.PatientId == patient.Id && w.Status == WaitlistStatus.Active)
            .CountAsync();

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task WaitlistEntry_SecondActive_SamePatient_ThrowsInvalidOperationException()
    {
        // Validates the WaitlistGuardInterceptor rejects a second Active entry
        // before the DB roundtrip (mirrors DB-level filtered unique index enforcement).
        await using var ctx = CreateContext();
        var patient = await SeedPatientAsync(ctx);

        ctx.WaitlistEntries.Add(new WaitlistEntry { PatientId = patient.Id });
        await ctx.SaveChangesAsync();

        ctx.WaitlistEntries.Add(new WaitlistEntry { PatientId = patient.Id, Status = WaitlistStatus.Active });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task WaitlistEntry_ExpiredEntry_DoesNotBlockNewActive()
    {
        // AC-002 — Expired entries are NOT constrained; a patient can have one
        // Active entry even after having an Expired one.
        await using var ctx = CreateContext();
        var patient = await SeedPatientAsync(ctx);

        // Add an Expired entry
        ctx.WaitlistEntries.Add(new WaitlistEntry
        {
            PatientId = patient.Id,
            Status    = WaitlistStatus.Expired
        });
        await ctx.SaveChangesAsync();

        // Add an Active entry — must be allowed (different status value)
        ctx.WaitlistEntries.Add(new WaitlistEntry
        {
            PatientId = patient.Id,
            Status    = WaitlistStatus.Active
        });
        await ctx.SaveChangesAsync();

        var activeCount = await ctx.WaitlistEntries
            .Where(w => w.PatientId == patient.Id && w.Status == WaitlistStatus.Active)
            .CountAsync();

        Assert.Equal(1, activeCount);
    }

    [Fact]
    public async Task WaitlistEntry_FulfilledEntry_DoesNotBlockNewActive()
    {
        await using var ctx = CreateContext();
        var patient = await SeedPatientAsync(ctx);

        ctx.WaitlistEntries.Add(new WaitlistEntry
        {
            PatientId = patient.Id,
            Status    = WaitlistStatus.Fulfilled
        });
        ctx.WaitlistEntries.Add(new WaitlistEntry
        {
            PatientId = patient.Id,
            Status    = WaitlistStatus.Active
        });
        await ctx.SaveChangesAsync();

        var activeCount = await ctx.WaitlistEntries
            .Where(w => w.PatientId == patient.Id && w.Status == WaitlistStatus.Active)
            .CountAsync();

        Assert.Equal(1, activeCount);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // IntakeRecord — versioning
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task IntakeRecord_VersionIncrement_CreatesNewRowWithHigherVersion()
    {
        // AC-003 — each PATCH creates a new row with incremented version
        await using var ctx = CreateContext();
        var patient = await SeedPatientAsync(ctx);

        var groupId = Guid.NewGuid();

        // v1
        ctx.IntakeRecords.Add(new IntakeRecord
        {
            IntakeGroupId = groupId,
            PatientId     = patient.Id,
            Version       = 1,
            IsLatest      = true,
            Source        = IntakeSource.Manual
        });
        await ctx.SaveChangesAsync();

        // Simulate PATCH: retire v1, insert v2
        var v1 = await ctx.IntakeRecords.FirstAsync(r => r.IntakeGroupId == groupId);
        v1.IsLatest = false;

        ctx.IntakeRecords.Add(new IntakeRecord
        {
            IntakeGroupId = groupId,
            PatientId     = patient.Id,
            Version       = 2,
            IsLatest      = true,
            Source        = IntakeSource.Manual,
            ChiefComplaint = "Updated complaint"
        });
        await ctx.SaveChangesAsync();

        var allVersions = await ctx.IntakeRecords
            .IgnoreQueryFilters()
            .Where(r => r.IntakeGroupId == groupId)
            .OrderBy(r => r.Version)
            .ToListAsync();

        Assert.Equal(2, allVersions.Count);
        Assert.Equal(1, allVersions[0].Version);
        Assert.False(allVersions[0].IsLatest);
        Assert.Equal(2, allVersions[1].Version);
        Assert.True(allVersions[1].IsLatest);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // IntakeRecord — default query filter
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task IntakeRecord_DefaultQueryFilter_ReturnsOnlyLatestVersion()
    {
        // AC-004 — normal query returns only IsLatest=true rows
        await using var ctx = CreateContext();
        var patient = await SeedPatientAsync(ctx);

        var groupId = Guid.NewGuid();

        ctx.IntakeRecords.Add(new IntakeRecord
        {
            IntakeGroupId = groupId, PatientId = patient.Id,
            Version = 1, IsLatest = false
        });
        ctx.IntakeRecords.Add(new IntakeRecord
        {
            IntakeGroupId = groupId, PatientId = patient.Id,
            Version = 2, IsLatest = true
        });
        await ctx.SaveChangesAsync();

        var filtered = await ctx.IntakeRecords
            .Where(r => r.IntakeGroupId == groupId)
            .ToListAsync();

        Assert.Single(filtered);
        Assert.Equal(2, filtered[0].Version);
    }

    [Fact]
    public async Task IntakeRecord_IgnoreQueryFilters_ReturnsAllVersions()
    {
        // AC-004 — IgnoreQueryFilters() returns full version history
        await using var ctx = CreateContext();
        var patient = await SeedPatientAsync(ctx);

        var groupId = Guid.NewGuid();

        ctx.IntakeRecords.Add(new IntakeRecord
        {
            IntakeGroupId = groupId, PatientId = patient.Id,
            Version = 1, IsLatest = false
        });
        ctx.IntakeRecords.Add(new IntakeRecord
        {
            IntakeGroupId = groupId, PatientId = patient.Id,
            Version = 2, IsLatest = false
        });
        ctx.IntakeRecords.Add(new IntakeRecord
        {
            IntakeGroupId = groupId, PatientId = patient.Id,
            Version = 3, IsLatest = true
        });
        await ctx.SaveChangesAsync();

        var all = await ctx.IntakeRecords
            .IgnoreQueryFilters()
            .Where(r => r.IntakeGroupId == groupId)
            .ToListAsync();

        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task IntakeRecord_MultiplePatients_FilterAppliesPerQuery()
    {
        // Default filter does not bleed across patients — each patient sees only
        // their own latest version.
        await using var ctx = CreateContext();
        var p1 = await SeedPatientAsync(ctx);
        var p2 = await SeedPatientAsync(ctx);

        var g1 = Guid.NewGuid();
        var g2 = Guid.NewGuid();

        ctx.IntakeRecords.Add(new IntakeRecord
        {
            IntakeGroupId = g1, PatientId = p1.Id, Version = 1, IsLatest = false
        });
        ctx.IntakeRecords.Add(new IntakeRecord
        {
            IntakeGroupId = g1, PatientId = p1.Id, Version = 2, IsLatest = true
        });
        ctx.IntakeRecords.Add(new IntakeRecord
        {
            IntakeGroupId = g2, PatientId = p2.Id, Version = 1, IsLatest = true
        });
        await ctx.SaveChangesAsync();

        // Query without filter — should return one row per patient (the latest)
        var results = await ctx.IntakeRecords.ToListAsync();
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.IsLatest));
    }
}
