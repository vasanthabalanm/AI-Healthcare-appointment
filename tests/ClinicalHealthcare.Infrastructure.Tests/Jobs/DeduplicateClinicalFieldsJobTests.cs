using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Jobs;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Jobs;

public sealed class DeduplicateClinicalFieldsJobTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static ClinicalDbContext CreatePgDb()
    {
        var opts = new DbContextOptionsBuilder<ClinicalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ClinicalDbContext(opts);
    }

    /// <summary>Returns a mock Redis multiplexer whose LockTakeAsync returns the given result.</summary>
    private static Mock<IConnectionMultiplexer> RedisMock(bool lockAcquired = true)
    {
        var dbMock  = new Mock<IDatabase>();
        dbMock.Setup(d => d.LockTakeAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CommandFlags>()))
              .ReturnsAsync(lockAcquired);

        dbMock.Setup(d => d.LockReleaseAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<CommandFlags>()))
              .ReturnsAsync(true);

        var mux = new Mock<IConnectionMultiplexer>();
        mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
           .Returns(dbMock.Object);

        return mux;
    }

    private static Mock<ICacheService> CacheMock() => new(MockBehavior.Loose);
    private static Mock<IBackgroundJobClient> JobsMock() => new(MockBehavior.Loose);

    private static DeduplicateClinicalFieldsJob CreateJob(
        ClinicalDbContext      pgDb,
        Mock<IConnectionMultiplexer>? redisMock = null,
        Mock<ICacheService>?          cacheMock = null,
        Mock<IBackgroundJobClient>?   jobsMock  = null)
        => new(
            pgDb,
            (redisMock ?? RedisMock()).Object,
            (cacheMock ?? CacheMock()).Object,
            (jobsMock  ?? JobsMock()).Object,
            NullLogger<DeduplicateClinicalFieldsJob>.Instance);

    private static ExtractedClinicalField Field(
        int patientId,
        string fieldName,
        string fieldValue,
        DateTime? extractedAt = null)
        => new()
        {
            PatientId       = patientId,
            DocumentId      = 1,
            FieldType       = ClinicalFieldType.VitalSign,
            FieldName       = fieldName,
            FieldValue      = fieldValue,
            ConfidenceScore = 0.90,
            ExtractionJobId = "job-1",
            ExtractedAt     = extractedAt ?? DateTime.UtcNow,
        };

    // ── Lock not acquired → reschedule ─────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_LockNotAcquired_ReschedulesAndReturnsWithoutChanges()
    {
        var pg    = CreatePgDb();
        var redis = RedisMock(lockAcquired: false);
        var jobs  = JobsMock();
        var cache = CacheMock();
        var sut   = CreateJob(pg, redis, cacheMock: cache, jobsMock: jobs);

        await sut.ExecuteAsync(1, null!);

        // Must schedule a delayed retry.
        jobs.Verify(j => j.Create(
            It.Is<Job>(job => job.Type == typeof(DeduplicateClinicalFieldsJob)),
            It.IsAny<ScheduledState>()),
            Times.Once);

        // Must NOT invalidate cache — nothing was processed (AC-003 boundary).
        cache.Verify(c => c.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        // Must not write anything to PG.
        Assert.Empty(await pg.ExtractedClinicalFields.IgnoreQueryFilters().ToListAsync());
    }

    // ── No fields → exits cleanly ──────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NoFields_ExitsWithoutError()
    {
        var pg    = CreatePgDb();
        var cache = CacheMock();
        var sut   = CreateJob(pg, cacheMock: cache);

        await sut.ExecuteAsync(99, null!);

        // Cache must still be invalidated even when no fields exist.
        cache.Verify(c => c.DeleteAsync("360view:99", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── AC-001: same value → soft-delete older ──────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SameFieldValue_KeepsNewestSoftDeletesOlder()
    {
        var pg = CreatePgDb();

        var older  = Field(10, "BP", "120/80", DateTime.UtcNow.AddMinutes(-5));
        var newer  = Field(10, "BP", "120/80", DateTime.UtcNow);
        pg.ExtractedClinicalFields.AddRange(older, newer);
        await pg.SaveChangesAsync();

        var sut = CreateJob(pg);
        await sut.ExecuteAsync(10, null!);

        // With query filter: only 1 active row remains.
        var active = await pg.ExtractedClinicalFields.ToListAsync();
        Assert.Single(active);
        Assert.Equal("120/80", active[0].FieldValue);
        Assert.False(active[0].IsDeleted);

        // The older row should be soft-deleted.
        var all = await pg.ExtractedClinicalFields.IgnoreQueryFilters().ToListAsync();
        var deleted = all.Single(f => f.IsDeleted);
        Assert.Equal(older.Id == 0 ? all.Min(f => f.Id) : older.Id, deleted.Id);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleStaleValues_KeepsNewestByExtractedAt()
    {
        var pg = CreatePgDb();

        var oldest = Field(20, "HR", "70",  DateTime.UtcNow.AddMinutes(-10));
        var middle = Field(20, "HR", "70",  DateTime.UtcNow.AddMinutes(-5));
        var newest = Field(20, "HR", "70",  DateTime.UtcNow);
        pg.ExtractedClinicalFields.AddRange(oldest, middle, newest);
        await pg.SaveChangesAsync();

        var sut = CreateJob(pg);
        await sut.ExecuteAsync(20, null!);

        var active = await pg.ExtractedClinicalFields.ToListAsync();
        Assert.Single(active);
        Assert.Equal(newest.ExtractedAt, active[0].ExtractedAt, TimeSpan.FromSeconds(1));
    }

    // ── AC-002: conflicting values → ConflictFlag inserted ─────────────────────

    [Fact]
    public async Task ExecuteAsync_ConflictingValues_InsertsConflictFlag()
    {
        var pg = CreatePgDb();

        pg.ExtractedClinicalFields.AddRange(
            Field(30, "BP", "120/80"),
            Field(30, "BP", "130/90"));
        await pg.SaveChangesAsync();

        var sut = CreateJob(pg);
        await sut.ExecuteAsync(30, null!);

        var flags = await pg.ConflictFlags.ToListAsync();
        Assert.Single(flags);
        Assert.Equal(30, flags[0].PatientId);
        Assert.Equal("BP", flags[0].FieldName);
        Assert.Equal(ConflictFlagStatus.Unresolved, flags[0].Status);
    }

    [Fact]
    public async Task ExecuteAsync_ConflictingValues_BothFieldRowsRemainActive()
    {
        var pg = CreatePgDb();

        pg.ExtractedClinicalFields.AddRange(
            Field(31, "BP", "120/80"),
            Field(31, "BP", "130/90"));
        await pg.SaveChangesAsync();

        var sut = CreateJob(pg);
        await sut.ExecuteAsync(31, null!);

        // Both rows must survive — staff needs both to resolve the conflict.
        var active = await pg.ExtractedClinicalFields.ToListAsync();
        Assert.Equal(2, active.Count);
    }

    [Fact]
    public async Task ExecuteAsync_DuplicateConflictFlag_NotInsertedIfUnresolvedExists()
    {
        var pg = CreatePgDb();

        pg.ExtractedClinicalFields.AddRange(
            Field(40, "HR", "70"),
            Field(40, "HR", "85"));

        // Pre-existing unresolved flag for same patient+field.
        pg.ConflictFlags.Add(new ConflictFlag
        {
            PatientId = 40,
            FieldName = "HR",
            Value1    = "70",
            Value2    = "85",
            Status    = ConflictFlagStatus.Unresolved,
        });
        await pg.SaveChangesAsync();

        var sut = CreateJob(pg);
        await sut.ExecuteAsync(40, null!);

        // Still only 1 flag.
        Assert.Single(await pg.ConflictFlags.ToListAsync());
    }

    // ── AC-003: cache invalidation ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Always_Invalidates360ViewCache()
    {
        var pg    = CreatePgDb();
        var cache = CacheMock();
        var sut   = CreateJob(pg, cacheMock: cache);

        await sut.ExecuteAsync(50, null!);

        cache.Verify(
            c => c.DeleteAsync("360view:50", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── AC-004: lock released in finally ──────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_LockAlwaysReleased_EvenWhenNoFields()
    {
        var pg       = CreatePgDb();
        var redisMock = RedisMock(lockAcquired: true);
        var sut      = CreateJob(pg, redisMock);

        await sut.ExecuteAsync(60, null!);

        var dbMock = Mock.Get(redisMock.Object.GetDatabase());
        dbMock.Verify(d => d.LockReleaseAsync(
            It.Is<RedisKey>(k => k == "dedup-lock:60"),
            It.IsAny<RedisValue>(),
            It.IsAny<CommandFlags>()),
            Times.Once);
    }

    // ── Mixed patient isolation ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_OnlyProcessesTargetPatient()
    {
        var pg = CreatePgDb();

        // Patient 70: duplicate BP → should be deduped
        pg.ExtractedClinicalFields.AddRange(
            Field(70, "BP", "120/80", DateTime.UtcNow.AddMinutes(-5)),
            Field(70, "BP", "120/80", DateTime.UtcNow));

        // Patient 71: different patient, should be untouched
        pg.ExtractedClinicalFields.AddRange(
            Field(71, "BP", "110/70", DateTime.UtcNow.AddMinutes(-5)),
            Field(71, "BP", "110/70", DateTime.UtcNow));

        await pg.SaveChangesAsync();

        var sut = CreateJob(pg);
        await sut.ExecuteAsync(70, null!);

        // Patient 70: 1 active after dedup
        var p70 = await pg.ExtractedClinicalFields.Where(f => f.PatientId == 70).ToListAsync();
        Assert.Single(p70);

        // Patient 71: still 2 active (untouched)
        var p71 = await pg.ExtractedClinicalFields.Where(f => f.PatientId == 71).ToListAsync();
        Assert.Equal(2, p71.Count);
    }
}
