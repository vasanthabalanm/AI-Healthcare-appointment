using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ClinicalHealthcare.Infrastructure.Jobs;

/// <summary>
/// Hangfire background job that de-duplicates <see cref="ExtractedClinicalField"/> rows for a
/// patient after OCR extraction (AC-001, AC-002, AC-003, AC-004 — TASK_042).
///
/// Pipeline:
///   1. Acquire Redis distributed lock <c>dedup-lock:{patientId}</c> (30 s TTL). If lock
///      unavailable → reschedule 10 s later and return (AC-004).
///   2. Load all active (non-deleted) <see cref="ExtractedClinicalField"/> rows from PostgreSQL.
///   3. Group by <see cref="ExtractedClinicalField.FieldName"/>.
///   4. For each group:
///      a. Same value across all rows → keep newest; soft-delete older rows (AC-001).
///      b. Two or more distinct values → insert <see cref="ConflictFlag"/> with
///         <see cref="ConflictFlagStatus.Unresolved"/> unless one already exists (AC-002).
///   5. <see cref="ClinicalDbContext.SaveChangesAsync"/> once for all changes.
///   6. Release lock (always, including on error).
///   7. Invalidate Redis <c>360view:{patientId}</c> cache key (AC-003).
///
/// <para><b>Note:</b> If step 5 throws, the lock is released but cache invalidation (step 7)
/// is skipped for that attempt. Hangfire retries will eventually complete both steps.
/// The cache TTL ensures natural expiry in the interim.</para>
/// </summary>
public sealed class DeduplicateClinicalFieldsJob
{
    private readonly ClinicalDbContext                         _pgDb;
    private readonly IConnectionMultiplexer?                  _redis;
    private readonly ICacheService                            _cache;
    private readonly IBackgroundJobClient                     _jobs;
    private readonly ILogger<DeduplicateClinicalFieldsJob>    _logger;

    private static readonly TimeSpan LockTtl         = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RescheduleDelay = TimeSpan.FromSeconds(10);

    public DeduplicateClinicalFieldsJob(
        ClinicalDbContext                      pgDb,
        IConnectionMultiplexer?                 redis,
        ICacheService                          cache,
        IBackgroundJobClient                   jobs,
        ILogger<DeduplicateClinicalFieldsJob>  logger)
    {
        _pgDb   = pgDb;
        _redis  = redis;
        _cache  = cache;
        _jobs   = jobs;
        _logger = logger;
    }

    /// <summary>
    /// Entry point invoked by Hangfire.
    /// </summary>
    /// <param name="patientId">PK of the patient whose fields are de-duplicated.</param>
    /// <param name="cancellationToken">Hangfire-supplied shutdown token.</param>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 60, 120])]
    public async Task ExecuteAsync(int patientId, IJobCancellationToken cancellationToken)
    {
        // AC-004: patient-scoped distributed lock prevents concurrent runs for same patient.
        // When Redis is not configured, skip the lock and run directly.
        var lockKey   = $"dedup-lock:{patientId}";
        var lockToken = $"{Environment.MachineName}-{Guid.NewGuid():N}";

        if (_redis is null)
        {
            await RunDeduplicationAsync(patientId, cancellationToken);
            await _cache.DeleteAsync($"360view:{patientId}");
            _logger.LogInformation(
                "DeduplicateClinicalFieldsJob: completed (no Redis lock) for patient {PatientId}.", patientId);
            return;
        }

        var db        = _redis.GetDatabase();

        bool acquired = await db.LockTakeAsync(lockKey, lockToken, LockTtl);

        if (!acquired)
        {
            _logger.LogInformation(
                "DeduplicateClinicalFieldsJob: lock busy for patient {PatientId} — rescheduling in {Delay}s.",
                patientId, RescheduleDelay.TotalSeconds);

            _jobs.Schedule<DeduplicateClinicalFieldsJob>(
                j => j.ExecuteAsync(patientId, null!),
                RescheduleDelay);
            return;
        }

        try
        {
            await RunDeduplicationAsync(patientId, cancellationToken);
        }
        finally
        {
            // Always release — even on exception (Hangfire will retry the full job).
            await db.LockReleaseAsync(lockKey, lockToken);
        }

        // AC-003: invalidate 360° view cache outside the lock so cache I/O doesn't extend TTL.
        await _cache.DeleteAsync($"360view:{patientId}");

        _logger.LogInformation(
            "DeduplicateClinicalFieldsJob: completed for patient {PatientId}.", patientId);
    }

    // ── Core deduplication logic (extracted for testability) ─────────────────

    private async Task RunDeduplicationAsync(int patientId, IJobCancellationToken cancellationToken)
    {
        var ct = cancellationToken?.ShutdownToken ?? CancellationToken.None;

        // Load all active fields for the patient.
        // Soft-delete query filter on ClinicalDbContext excludes IsDeleted=true rows.
        var fields = await _pgDb.ExtractedClinicalFields
            .Where(f => f.PatientId == patientId)
            .OrderByDescending(f => f.ExtractedAt)
            .ThenByDescending(f => f.Id)
            .ToListAsync(ct);

        if (fields.Count == 0)
        {
            _logger.LogDebug(
                "DeduplicateClinicalFieldsJob: no active fields for patient {PatientId}.", patientId);
            return;
        }

        // Load existing Unresolved conflict flags to avoid duplicates (idempotency).
        var existingUnresolvedFlags = (await _pgDb.ConflictFlags
            .Where(c => c.PatientId == patientId && c.Status == ConflictFlagStatus.Unresolved)
            .Select(c => c.FieldName)
            .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var group in fields.GroupBy(f => f.FieldName))
        {
            var groupList      = group.ToList(); // ordered newest-first by the query above
            var distinctValues = groupList.Select(f => f.FieldValue).Distinct().ToList();

            if (distinctValues.Count == 1)
            {
                // AC-001: all rows share the same value — keep newest; soft-delete older.
                foreach (var stale in groupList.Skip(1))
                {
                    stale.IsDeleted = true;
                    _logger.LogDebug(
                        "DeduplicateClinicalFieldsJob: soft-deleting field Id={FieldId} ({FieldName}) for patient {PatientId}.",
                        stale.Id, stale.FieldName, patientId);
                }
            }
            else
            {
                // AC-002: conflicting values — insert ConflictFlag if none already open.
                if (!existingUnresolvedFlags.Contains(group.Key))
                {
                    _pgDb.ConflictFlags.Add(new ConflictFlag
                    {
                        PatientId = patientId,
                        FieldName = group.Key,
                        Value1    = distinctValues[0],
                        Value2    = distinctValues[1],
                        Status    = ConflictFlagStatus.Unresolved,
                    });

                    _logger.LogInformation(
                        "DeduplicateClinicalFieldsJob: ConflictFlag created — patient {PatientId}, field '{FieldName}', values: '{V1}' vs '{V2}'.",
                        patientId, group.Key, distinctValues[0], distinctValues[1]);
                }
                else
                {
                    _logger.LogDebug(
                        "DeduplicateClinicalFieldsJob: ConflictFlag already open for patient {PatientId}, field '{FieldName}' — skipping.",
                        patientId, group.Key);
                }
            }
        }

        await _pgDb.SaveChangesAsync(ct);
    }
}

