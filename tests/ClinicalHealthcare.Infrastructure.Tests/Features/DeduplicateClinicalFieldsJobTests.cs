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

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for <see cref="DeduplicateClinicalFieldsJob.ExecuteAsync"/> (TASK_042 AC-001–AC-004).
/// </summary>
public sealed class DeduplicateClinicalFieldsJobTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ClinicalDbContext BuildPgDb()
    {
        var opts = new DbContextOptionsBuilder<ClinicalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ClinicalDbContext(opts);
    }

    private static ExtractedClinicalField SeedField(
        ClinicalDbContext pgDb,
        int patientId,
        string fieldName,
        string fieldValue,
        DateTime? extractedAt = null)
    {
        var field = new ExtractedClinicalField
        {
            PatientId       = patientId,
            DocumentId      = 1,
            FieldType       = ClinicalFieldType.Medication,
            FieldName       = fieldName,
            FieldValue      = fieldValue,
            ConfidenceScore = 0.90,
            ExtractionJobId = Guid.NewGuid().ToString("N"),
            ExtractedAt     = extractedAt ?? DateTime.UtcNow,
            IsDeleted       = false
        };
        pgDb.ExtractedClinicalFields.Add(field);
        pgDb.SaveChanges();
        return field;
    }

    private static DeduplicateClinicalFieldsJob BuildJob(
        ClinicalDbContext          pgDb,
        IConnectionMultiplexer?    redis,
        Mock<ICacheService>        cache,
        Mock<IBackgroundJobClient> jobs)
        => new(pgDb, redis, cache.Object, jobs.Object,
               NullLogger<DeduplicateClinicalFieldsJob>.Instance);

    // ── TC-001: Same-value duplicates → older soft-deleted; newer retained ────

    [Fact]
    public async Task Dedup_SameValues_SoftDeletesOlderAndRetainsNewer()
    {
        await using var pgDb  = BuildPgDb();
        var newerField = SeedField(pgDb, 1, "Medication", "Aspirin 100mg", DateTime.UtcNow);
        var olderField = SeedField(pgDb, 1, "Medication", "Aspirin 100mg", DateTime.UtcNow.AddDays(-1));

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(pgDb, redis: null, cache, jobs);

        await job.ExecuteAsync(1, null!);

        var all = await pgDb.ExtractedClinicalFields
            .IgnoreQueryFilters()
            .OrderByDescending(f => f.ExtractedAt)
            .ToListAsync();
        Assert.False(all.First().IsDeleted);
        Assert.True(all.Last().IsDeleted);
    }

    // ── TC-002: Conflicting values → ConflictFlag inserted with Status=Unresolved ──

    [Fact]
    public async Task Dedup_ConflictingValues_InsertsUnresolvedConflictFlag()
    {
        await using var pgDb = BuildPgDb();
        SeedField(pgDb, 1, "Medication", "Metformin 500mg");
        SeedField(pgDb, 1, "Medication", "Metformin 1000mg");

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(pgDb, redis: null, cache, jobs);

        await job.ExecuteAsync(1, null!);

        var flags = await pgDb.ConflictFlags.ToListAsync();
        var flag0 = Assert.Single(flags);
        Assert.Equal(ConflictFlagStatus.Unresolved, flag0.Status);
        // Both field rows remain (not soft-deleted on conflict)
        var allFields = await pgDb.ExtractedClinicalFields.IgnoreQueryFilters().ToListAsync();
        Assert.All(allFields, f => Assert.False(f.IsDeleted));
    }

    // ── TC-003: Cache key 360view:{patientId} deleted after dedup (null Redis) ──

    [Fact]
    public async Task Dedup_NullRedis_DeletesCacheKeyAfterDedup()
    {
        await using var pgDb = BuildPgDb();
        SeedField(pgDb, 5, "BP", "120/80");

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(pgDb, redis: null, cache, jobs);

        await job.ExecuteAsync(5, null!);

        cache.Verify(
            c => c.DeleteAsync("360view:5", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── TC-004: No fields for patient → no ConflictFlag; cache still invalidated ──

    [Fact]
    public async Task Dedup_NoFields_NoConflictFlagAndCacheStillInvalidated()
    {
        await using var pgDb = BuildPgDb();

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(pgDb, redis: null, cache, jobs);

        await job.ExecuteAsync(7, null!);

        Assert.Equal(0, await pgDb.ConflictFlags.CountAsync());
        cache.Verify(
            c => c.DeleteAsync("360view:7", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── TC-005: Existing Unresolved flag → no duplicate ConflictFlag inserted ─

    [Fact]
    public async Task Dedup_ExistingUnresolvedFlag_DoesNotInsertDuplicateFlag()
    {
        await using var pgDb = BuildPgDb();
        pgDb.ConflictFlags.Add(new ConflictFlag
        {
            PatientId = 1,
            FieldName = "Medication",
            Value1    = "Metformin 500mg",
            Value2    = "Metformin 1000mg",
            Status    = ConflictFlagStatus.Unresolved
        });
        await pgDb.SaveChangesAsync();
        SeedField(pgDb, 1, "Medication", "Metformin 500mg");
        SeedField(pgDb, 1, "Medication", "Metformin 1000mg");

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(pgDb, redis: null, cache, jobs);

        await job.ExecuteAsync(1, null!);

        Assert.Single(await pgDb.ConflictFlags.ToListAsync());
    }

    // ── TC-006: Redis present; lock acquired → dedup runs; cache deleted; lock released ──

    [Fact]
    public async Task Dedup_RedisLockAcquired_RunsDedupAndDeletesCacheAndReleasesLock()
    {
        await using var pgDb = BuildPgDb();
        SeedField(pgDb, 1, "BP", "120/80");

        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(d => d.LockTakeAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan>(), It.IsAny<CommandFlags>()))
              .ReturnsAsync(true);
        mockDb.Setup(d => d.LockReleaseAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()))
              .ReturnsAsync(true);

        var mockRedis = new Mock<IConnectionMultiplexer>();
        mockRedis.Setup(r => r.GetDatabase(-1, null)).Returns(mockDb.Object);

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(pgDb, mockRedis.Object, cache, jobs);

        await job.ExecuteAsync(1, null!);

        cache.Verify(
            c => c.DeleteAsync("360view:1", It.IsAny<CancellationToken>()),
            Times.Once);
        mockDb.Verify(
            d => d.LockReleaseAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<CommandFlags>()),
            Times.Once);
    }

    // ── TC-007: Redis present; lock busy → reschedule job; no dedup mutations ─

    [Fact]
    public async Task Dedup_RedisLockBusy_ReschedulesJobWithoutRunningDedup()
    {
        await using var pgDb = BuildPgDb();
        SeedField(pgDb, 1, "Medication", "Aspirin 100mg");

        var mockDb = new Mock<IDatabase>();
        mockDb.Setup(d => d.LockTakeAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan>(), It.IsAny<CommandFlags>()))
              .ReturnsAsync(false);

        var mockRedis = new Mock<IConnectionMultiplexer>();
        mockRedis.Setup(r => r.GetDatabase(-1, null)).Returns(mockDb.Object);

        var cache = new Mock<ICacheService>();
        var jobs  = new Mock<IBackgroundJobClient>();
        var job   = BuildJob(pgDb, mockRedis.Object, cache, jobs);

        await job.ExecuteAsync(1, null!);

        jobs.Verify(j => j.Create(
            It.Is<Job>(j2 => j2.Type == typeof(DeduplicateClinicalFieldsJob)),
            It.Is<IState>(s => s is ScheduledState)), Times.Once);
        // No ConflictFlags, no soft-deletes
        Assert.Equal(0, await pgDb.ConflictFlags.CountAsync());
        var allFields = await pgDb.ExtractedClinicalFields.IgnoreQueryFilters().ToListAsync();
        Assert.All(allFields, f => Assert.False(f.IsDeleted));
    }

    // ── TC-008: Three same-value duplicates → two oldest soft-deleted; newest retained ──

    [Fact]
    public async Task Dedup_ThreeSameValueDuplicates_SoftDeletesTwoOldestAndRetainsNewest()
    {
        await using var pgDb = BuildPgDb();
        var now = DateTime.UtcNow;
        SeedField(pgDb, 1, "Medication", "Aspirin 100mg", now);
        SeedField(pgDb, 1, "Medication", "Aspirin 100mg", now.AddHours(-1));
        SeedField(pgDb, 1, "Medication", "Aspirin 100mg", now.AddHours(-2));

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(pgDb, redis: null, cache, jobs);

        await job.ExecuteAsync(1, null!);

        var all = await pgDb.ExtractedClinicalFields.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(2, all.Count(f => f.IsDeleted));
        Assert.Single(all, f => !f.IsDeleted);
    }

    // ── EC-001: ConflictFlag captures both distinct values ────────────────────

    [Fact]
    public async Task Dedup_ConflictingValues_ConflictFlagCapturesBothValues()
    {
        await using var pgDb = BuildPgDb();
        SeedField(pgDb, 1, "BP", "120/80");
        SeedField(pgDb, 1, "BP", "130/85");

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(pgDb, redis: null, cache, jobs);

        await job.ExecuteAsync(1, null!);

        var flag = await pgDb.ConflictFlags.SingleAsync();
        Assert.NotEqual(flag.Value1, flag.Value2);
        Assert.Contains(flag.Value1, new[] { "120/80", "130/85" });
        Assert.Contains(flag.Value2, new[] { "120/80", "130/85" });
    }
}
