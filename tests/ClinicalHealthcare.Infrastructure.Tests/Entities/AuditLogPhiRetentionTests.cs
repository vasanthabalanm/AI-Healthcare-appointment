using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Entities;

/// <summary>
/// Unit tests for TASK_011: AuditLog entity, PHI soft-delete interception, and CacheSettings TTL constants.
///
/// PHI entity hard-delete behaviour (AC-003) is verified with InMemory database.
/// REVOKE UPDATE/DELETE enforcement (AC-001) is DB-level only and verified by
/// inspection of the generated migration SQL.
/// </summary>
public sealed class AuditLogPhiRetentionTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static UserAccount MakeUser(string email = "test@example.com") => new()
    {
        Email        = email,
        PasswordHash = "hash",
        Role         = "patient"
    };

    // ── AC-001: AuditLog entity contract ─────────────────────────────────────

    [Fact]
    public async Task AuditLog_CanBeInserted_WithAllFields()
    {
        await using var ctx = CreateContext();

        var log = new AuditLog
        {
            EntityType    = "UserAccount",
            EntityId      = 42,
            ActorId       = 1,
            Action        = "UPDATE",
            BeforeValue   = "{\"IsActive\":true}",
            AfterValue    = "{\"IsActive\":false}",
            CorrelationId = "corr-abc-123"
        };
        ctx.AuditLogs.Add(log);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.AuditLogs.FindAsync(log.Id);
        Assert.NotNull(loaded);
        Assert.Equal("UserAccount", loaded!.EntityType);
        Assert.Equal("UPDATE", loaded.Action);
        Assert.Equal("corr-abc-123", loaded.CorrelationId);
    }

    [Fact]
    public async Task AuditLog_BeforeAfterValue_CanBeNull_ForInsertAction()
    {
        // On INSERT: no before-state; AfterValue holds new snapshot.
        await using var ctx = CreateContext();

        var log = new AuditLog
        {
            EntityType  = "ClinicalDocument",
            EntityId    = 7,
            Action      = "INSERT",
            BeforeValue = null,
            AfterValue  = "{\"Id\":7,\"PatientId\":1}"
        };
        ctx.AuditLogs.Add(log);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.AuditLogs.FindAsync(log.Id);
        Assert.Null(loaded!.BeforeValue);
        Assert.NotNull(loaded.AfterValue);
    }

    [Fact]
    public async Task AuditLog_ActorId_CanBeNull_ForSystemActions()
    {
        await using var ctx = CreateContext();

        var log = new AuditLog
        {
            EntityType = "IntakeRecord",
            EntityId   = 3,
            ActorId    = null,
            Action     = "SYSTEM_PURGE"
        };
        ctx.AuditLogs.Add(log);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.AuditLogs.FindAsync(log.Id);
        Assert.Null(loaded!.ActorId);
    }

    // ── AC-002: PHI retention columns on entities ────────────────────────────

    [Fact]
    public void UserAccount_HasIsDeleted_AndRetainUntil_Properties()
    {
        var isDeletedProp   = typeof(UserAccount).GetProperty(nameof(UserAccount.IsDeleted));
        var retainUntilProp = typeof(UserAccount).GetProperty(nameof(UserAccount.RetainUntil));
        Assert.NotNull(isDeletedProp);
        Assert.NotNull(retainUntilProp);
        Assert.Equal(typeof(bool),            isDeletedProp!.PropertyType);
        Assert.Equal(typeof(DateTimeOffset?), retainUntilProp!.PropertyType);
    }

    [Theory]
    [InlineData(typeof(UserAccount))]
    [InlineData(typeof(IntakeRecord))]
    [InlineData(typeof(ClinicalDocument))]
    [InlineData(typeof(WaitlistEntry))]
    public void PhiEntity_HasIsDeletedAndRetainUntil(Type entityType)
    {
        var isDeleted   = entityType.GetProperty("IsDeleted");
        var retainUntil = entityType.GetProperty("RetainUntil");
        Assert.NotNull(isDeleted);
        Assert.NotNull(retainUntil);
        Assert.Equal(typeof(bool),            isDeleted!.PropertyType);
        Assert.Equal(typeof(DateTimeOffset?), retainUntil!.PropertyType);
    }

    [Fact]
    public async Task UserAccount_IsDeleted_DefaultsFalse()
    {
        await using var ctx = CreateContext();
        var user = MakeUser();
        ctx.UserAccounts.Add(user);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.UserAccounts.FindAsync(user.Id);
        Assert.False(loaded!.IsDeleted);
        Assert.Null(loaded.RetainUntil);
    }

    // ── AC-003: SaveChanges PHI soft-delete intercept ────────────────────────

    [Fact]
    public async Task HardDelete_OnUserAccount_ConvertedToSoftDelete()
    {
        await using var ctx = CreateContext();
        var user = MakeUser("alice@example.com");
        ctx.UserAccounts.Add(user);
        await ctx.SaveChangesAsync();

        ctx.UserAccounts.Remove(user);
        await ctx.SaveChangesAsync();

        // Row must still exist (not hard-deleted)
        var loaded = await ctx.UserAccounts.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == user.Id);
        Assert.NotNull(loaded);
        Assert.True(loaded!.IsDeleted);
    }

    [Fact]
    public async Task HardDelete_OnUserAccount_SetsRetainUntil_SevenYears()
    {
        await using var ctx = CreateContext();
        var user = MakeUser("bob@example.com");
        ctx.UserAccounts.Add(user);
        await ctx.SaveChangesAsync();

        var before = DateTimeOffset.UtcNow;
        ctx.UserAccounts.Remove(user);
        await ctx.SaveChangesAsync();
        var after = DateTimeOffset.UtcNow;

        var loaded = await ctx.UserAccounts.IgnoreQueryFilters().FirstAsync(u => u.Id == user.Id);
        Assert.NotNull(loaded.RetainUntil);
        // RetainUntil should be ~7 years from now
        var expectedMin = before.AddYears(7).AddSeconds(-1);
        var expectedMax = after.AddYears(7).AddSeconds(1);
        Assert.InRange(loaded.RetainUntil!.Value, expectedMin, expectedMax);
    }

    [Fact]
    public async Task HardDelete_OnIntakeRecord_ConvertedToSoftDelete()
    {
        await using var ctx = CreateContext();
        var user = MakeUser("carol@example.com");
        ctx.UserAccounts.Add(user);
        await ctx.SaveChangesAsync();

        var record = new IntakeRecord { PatientId = user.Id, Source = IntakeSource.Manual };
        ctx.IntakeRecords.Add(record);
        await ctx.SaveChangesAsync();

        ctx.IntakeRecords.Remove(record);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.IntakeRecords.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.Id == record.Id);
        Assert.NotNull(loaded);
        Assert.True(loaded!.IsDeleted);
        Assert.NotNull(loaded.RetainUntil);
    }

    [Fact]
    public async Task HardDelete_OnWaitlistEntry_ConvertedToSoftDelete()
    {
        await using var ctx = CreateContext();
        var user = MakeUser("dan@example.com");
        ctx.UserAccounts.Add(user);
        await ctx.SaveChangesAsync();

        var entry = new WaitlistEntry { PatientId = user.Id, Status = WaitlistStatus.Active };
        ctx.WaitlistEntries.Add(entry);
        await ctx.SaveChangesAsync();

        ctx.WaitlistEntries.Remove(entry);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.WaitlistEntries.IgnoreQueryFilters().FirstOrDefaultAsync(w => w.Id == entry.Id);
        Assert.NotNull(loaded);
        Assert.True(loaded!.IsDeleted);
        Assert.NotNull(loaded.RetainUntil);
    }

    [Fact]
    public async Task HardDelete_OnClinicalDocument_ConvertedToSoftDelete()
    {
        await using var ctx = CreateContext();
        var user = MakeUser("eve@example.com");
        ctx.UserAccounts.Add(user);
        await ctx.SaveChangesAsync();

        var doc = new ClinicalDocument
        {
            PatientId         = user.Id,
            OriginalFileName  = "ecg.pdf",
            EncryptedBlobPath = "/blobs/enc/ecg.pdf"
        };
        ctx.ClinicalDocuments.Add(doc);
        await ctx.SaveChangesAsync();

        ctx.ClinicalDocuments.Remove(doc);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.ClinicalDocuments.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == doc.Id);
        Assert.NotNull(loaded);
        Assert.True(loaded!.IsDeleted);
        Assert.NotNull(loaded.RetainUntil);
    }

    [Fact]
    public async Task HardDelete_OnSlot_ProceedsNormally_NotIntercepted()
    {
        // Slot is NOT a PHI entity — hard delete must proceed.
        await using var ctx = CreateContext();
        var slot = new Slot
        {
            SlotTime        = DateTime.UtcNow.AddDays(1),
            DurationMinutes = 30
        };
        ctx.Slots.Add(slot);
        await ctx.SaveChangesAsync();

        ctx.Slots.Remove(slot);
        await ctx.SaveChangesAsync();

        var loaded = await ctx.Slots.FindAsync(slot.Id);
        Assert.Null(loaded); // Row must be physically deleted
    }

    // ── AC-004: CacheSettings TTL constants ───────────────────────────────────

    [Fact]
    public void CacheSettings_SessionTtlSeconds_Is900()
    {
        var settings = new ClinicalHealthcare.Infrastructure.Cache.CacheSettings();
        Assert.Equal(900, settings.SessionTtlSeconds);
    }

    [Fact]
    public void CacheSettings_SlotTtlSeconds_Is60()
    {
        var settings = new ClinicalHealthcare.Infrastructure.Cache.CacheSettings();
        Assert.Equal(60, settings.SlotTtlSeconds);
    }

    [Fact]
    public void CacheSettings_View360TtlSeconds_Is300()
    {
        var settings = new ClinicalHealthcare.Infrastructure.Cache.CacheSettings();
        Assert.Equal(300, settings.View360TtlSeconds);
    }

    // ── Soft-delete query filters (F1 follow-up) ──────────────────────────────

    [Fact]
    public async Task UserAccount_SoftDeleted_ExcludedByDefaultQuery()
    {
        await using var ctx = CreateContext();
        var active  = MakeUser("active@example.com");
        var deleted = MakeUser("deleted@example.com");
        ctx.UserAccounts.AddRange(active, deleted);
        await ctx.SaveChangesAsync();

        // Simulate soft-delete via SaveChanges intercept
        ctx.UserAccounts.Remove(deleted);
        await ctx.SaveChangesAsync();

        var results = await ctx.UserAccounts.ToListAsync();

        Assert.Single(results);
        Assert.Equal("active@example.com", results[0].Email);
    }

    [Fact]
    public async Task UserAccount_SoftDeleted_VisibleWithIgnoreQueryFilters()
    {
        await using var ctx = CreateContext();
        var active  = MakeUser("active2@example.com");
        var deleted = MakeUser("deleted2@example.com");
        ctx.UserAccounts.AddRange(active, deleted);
        await ctx.SaveChangesAsync();

        ctx.UserAccounts.Remove(deleted);
        await ctx.SaveChangesAsync();

        var results = await ctx.UserAccounts.IgnoreQueryFilters().ToListAsync();

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task WaitlistEntry_SoftDeleted_ExcludedByDefaultQuery()
    {
        await using var ctx = CreateContext();
        var user = MakeUser("wl@example.com");
        ctx.UserAccounts.Add(user);
        await ctx.SaveChangesAsync();

        var live    = new WaitlistEntry { PatientId = user.Id, Status = WaitlistStatus.Active };
        var deleted = new WaitlistEntry { PatientId = user.Id, Status = WaitlistStatus.Expired };
        ctx.WaitlistEntries.AddRange(live, deleted);
        await ctx.SaveChangesAsync();

        ctx.WaitlistEntries.Remove(deleted);
        await ctx.SaveChangesAsync();

        var results = await ctx.WaitlistEntries.Where(w => w.PatientId == user.Id).ToListAsync();

        Assert.Single(results);
        Assert.Equal(WaitlistStatus.Active, results[0].Status);
    }

    [Fact]
    public async Task ClinicalDocument_SoftDeleted_ExcludedByDefaultQuery()
    {
        await using var ctx = CreateContext();
        var user = MakeUser("doc@example.com");
        ctx.UserAccounts.Add(user);
        await ctx.SaveChangesAsync();

        var live    = new ClinicalDocument { PatientId = user.Id, OriginalFileName = "live.pdf",    EncryptedBlobPath = "/blobs/live.pdf" };
        var deleted = new ClinicalDocument { PatientId = user.Id, OriginalFileName = "deleted.pdf", EncryptedBlobPath = "/blobs/del.pdf"  };
        ctx.ClinicalDocuments.AddRange(live, deleted);
        await ctx.SaveChangesAsync();

        ctx.ClinicalDocuments.Remove(deleted);
        await ctx.SaveChangesAsync();

        var results = await ctx.ClinicalDocuments.Where(d => d.PatientId == user.Id).ToListAsync();

        Assert.Single(results);
        Assert.Equal("live.pdf", results[0].OriginalFileName);
    }

    [Fact]
    public async Task IntakeRecord_SoftDeleted_ExcludedByCompositeFilter()
    {
        // IntakeRecord uses composite filter: IsLatest && !IsDeleted
        await using var ctx = CreateContext();
        var user = MakeUser("ir@example.com");
        ctx.UserAccounts.Add(user);
        await ctx.SaveChangesAsync();

        var live    = new IntakeRecord { PatientId = user.Id, Source = IntakeSource.Manual };
        var deleted = new IntakeRecord { PatientId = user.Id, Source = IntakeSource.AI };
        ctx.IntakeRecords.AddRange(live, deleted);
        await ctx.SaveChangesAsync();

        ctx.IntakeRecords.Remove(deleted);
        await ctx.SaveChangesAsync();

        var results = await ctx.IntakeRecords.Where(r => r.PatientId == user.Id).ToListAsync();

        Assert.Single(results);
        Assert.Equal(IntakeSource.Manual, results[0].Source);
    }
}
