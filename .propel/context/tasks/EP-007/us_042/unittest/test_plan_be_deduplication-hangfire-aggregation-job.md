# Unit Test Plan - TASK_042

## Requirement Reference
- **User Story**: us_042
- **Story Location**: `.propel/context/tasks/EP-007/us_042/us_042.md`
- **Layer**: BE
- **Related Test Plans**:
  - `EP-007/us_041/unittest/test_plan_be_clinical-field-extraction-nlp.md` (extraction job enqueues this job)
  - `EP-007/us_043/unittest/test_plan_be_conflict-detection-staff-resolution.md` (ConflictFlag entity shared)
  - `EP-007/us_044/unittest/test_plan_be_360-patient-view-redis-trust-first-verify.md` (360° view cache key `view360:{patientId}`)
- **Acceptance Criteria Covered**:
  - AC-001: Duplicate same-value `ExtractedClinicalField` rows → soft-delete older (`IsDeleted=true`); keep newest
  - AC-002: Conflicting values for same `FieldName` → insert `ConflictFlag` (`Status=Unresolved`)
  - AC-003: Redis 360° view cache key `view360:{patientId}` deleted after deduplication completes
  - AC-004: Patient-scoped distributed lock (`dedup-lock:{patientId}`) prevents concurrent runs; lock-busy → reschedule; null Redis → run without lock

## Test Plan Overview

Tests `DeduplicateClinicalFieldsJob.ExecuteAsync(patientId, cancellationToken)`.

`ClinicalDbContext` uses InMemory EF Core with per-test `Guid.NewGuid().ToString()` (no transactions config
needed). `IConnectionMultiplexer` is passed as `null` in all unit tests — the source skips the Redis lock
when `_redis is null`, enabling direct exercising of `RunDeduplicationAsync`. `ICacheService` is mocked via
Moq. `IBackgroundJobClient` is mocked for reschedule verification. `ILogger<DeduplicateClinicalFieldsJob>`
uses `NullLogger`.

**Note on Redis lock path**: The `IConnectionMultiplexer` Redis-lock-acquired / lock-busy paths require a
`Mock<IConnectionMultiplexer>` + `Mock<IDatabase>`. Tests `TC-006` and `TC-007` cover these paths.

## Dependent Tasks

- TASK_001 (Entities) — `ExtractedClinicalField`, `ConflictFlag`, `ConflictFlagStatus`
- TASK_001 (Data) — `ClinicalDbContext.ExtractedClinicalFields`, `ClinicalDbContext.ConflictFlags`
- TASK_042 — `DeduplicateClinicalFieldsJob`
- TASK_043 — `ConflictFlag` entity (shared)

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `DeduplicateClinicalFieldsJob` | class | `src/ClinicalHealthcare.Infrastructure/Jobs/DeduplicateClinicalFieldsJob.cs` | Redis lock; group fields by FieldName; soft-delete duplicates; insert ConflictFlag; cache invalidation |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | Same-value duplicates → older soft-deleted; newer retained `[SOURCE:INPUT]` | Seed 2 `ExtractedClinicalField` rows: `FieldName="Medication"`, `FieldValue="Aspirin 100mg"`, `ExtractedAt`: today and yesterday; `IConnectionMultiplexer=null` | `job.ExecuteAsync(patientId=1, ...)` | Older row `IsDeleted==true`; newer row `IsDeleted==false` | `Assert.True(olderField.IsDeleted)`; `Assert.False(newerField.IsDeleted)` |
| TC-002 | positive | Conflicting values → ConflictFlag inserted with Status=Unresolved `[SOURCE:INPUT]` | Seed 2 rows: same `FieldName`, different `FieldValue` ("Metformin 500mg" vs "Metformin 1000mg"); null Redis | `job.ExecuteAsync(1, ...)` | `pgDb.ConflictFlags.Count()==1`; `flag.Status==Unresolved`; both field rows NOT soft-deleted | `Assert.Equal(1, flagCount)`; `Assert.Equal(ConflictFlagStatus.Unresolved, flag.Status)` |
| TC-003 | positive | Cache key `view360:{patientId}` deleted after dedup (null Redis path) `[SOURCE:INPUT]` | Seed 1 field; null Redis; `Mock<ICacheService>` | `job.ExecuteAsync(5, ...)` | `cache.DeleteAsync("360view:5", ...)` called once | `cacheMock.Verify(c => c.DeleteAsync("360view:5", It.IsAny<CancellationToken>()), Times.Once)` |
| TC-004 | positive | No fields for patient → no-op; no ConflictFlag; cache still invalidated `[SOURCE:INFERRED]` Basis: `RunDeduplicationAsync` returns early if `fields.Count==0` but `_cache.DeleteAsync` is called regardless in null-Redis path. | Empty `ExtractedClinicalFields` for patientId=7; null Redis | `job.ExecuteAsync(7, ...)` | `pgDb.ConflictFlags.Count()==0`; `cache.DeleteAsync` called | `Assert.Equal(0, flagCount)`; `cacheMock.Verify(Times.Once)` |
| TC-005 | positive | Existing unresolved ConflictFlag → no duplicate flag inserted (idempotency) `[SOURCE:INPUT]` | Seed existing `ConflictFlag(PatientId=1, FieldName="Medication", Status=Unresolved)`; seed 2 conflicting fields for same FieldName; null Redis | `job.ExecuteAsync(1, ...)` | `pgDb.ConflictFlags.Count()==1` (unchanged) | `Assert.Equal(1, pgDb.ConflictFlags.Count())` |
| TC-006 | positive | Redis present; lock acquired → RunDeduplicationAsync executes; cache deleted `[SOURCE:INFERRED]` Basis: `db.LockTakeAsync(...)` returns `true`; job runs; `LockReleaseAsync` called; cache deleted. | Seed 1 field; `Mock<IConnectionMultiplexer>` → `LockTakeAsync` returns `true`; `Mock<ICacheService>` | `job.ExecuteAsync(1, ...)` | `pgDb` mutations applied; `cache.DeleteAsync` called; `LockReleaseAsync` called | `cacheMock.Verify(Times.Once)`; `dbMock verify LockRelease Times.Once` |
| TC-007 | negative | Redis present; lock busy → reschedule job; return without running dedup `[SOURCE:INPUT]` | Seed 1 field; `Mock<IConnectionMultiplexer>` → `LockTakeAsync` returns `false`; `Mock<IBackgroundJobClient>` | `job.ExecuteAsync(1, ...)` | `jobs.Schedule<DeduplicateClinicalFieldsJob>(...)` called once; field rows unchanged | `jobsMock.Verify(j => j.Create(...Schedule...), Times.Once)`; no mutations to pgDb |
| TC-008 | positive | Three same-value duplicates → two oldest soft-deleted; newest retained `[SOURCE:INPUT]` | Seed 3 fields: same `FieldName`+`FieldValue`, extracted at T-2h, T-1h, T-0h; null Redis | `job.ExecuteAsync(1, ...)` | Latest `IsDeleted==false`; two oldest `IsDeleted==true` | `Assert.Equal(2, fields.Count(f => f.IsDeleted))`; `Assert.Equal(1, fields.Count(f => !f.IsDeleted))` |
| EC-001 | edge_case | Conflicting fields where Value1 and Value2 correctly captured in ConflictFlag `[SOURCE:INPUT]` | Seed: Field A `FieldValue="120/80"`, Field B `FieldValue="130/85"` for same `FieldName="BP"`; null Redis | `job.ExecuteAsync(1, ...)` | `flag.Value1` and `flag.Value2` contain the two values | `Assert.NotEqual(flag.Value1, flag.Value2)`; both values present in `{flag.Value1, flag.Value2}` |

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/DeduplicateClinicalFieldsJobTests.cs` | TC-001 through EC-001 (9 test methods) |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ClinicalDbContext` | In-Memory EF Core | `UseInMemoryDatabase(Guid.NewGuid().ToString())` (no transactions config required) | Per-test isolated PostgreSQL side |
| `IConnectionMultiplexer` | `null` (most tests) | Source skips lock path when `_redis is null`; no mock required for TC-001–TC-005, TC-008, EC-001 | Direct `RunDeduplicationAsync` path |
| `IConnectionMultiplexer` | `Mock<IConnectionMultiplexer>` + `Mock<IDatabase>` (TC-006, TC-007) | `.Setup(r => r.GetDatabase(...)).Returns(mockDb)`; `mockDb.Setup(d => d.LockTakeAsync(...)).ReturnsAsync(true/false)` | Lock acquired/busy |
| `ICacheService` | `Mock<ICacheService>` | `.Setup(c => c.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask)` | No-op; verify `DeleteAsync` key |
| `IBackgroundJobClient` | `Mock<IBackgroundJobClient>` | No-op default; verify `.Schedule(...)` call for TC-007 | Mock object |
| `ILogger<DeduplicateClinicalFieldsJob>` | `NullLogger<DeduplicateClinicalFieldsJob>.Instance` | No-op | — |

### Helper Pattern

```csharp
private static ClinicalDbContext BuildPgDb()
{
    var opts = new DbContextOptionsBuilder<ClinicalDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options;
    return new ClinicalDbContext(opts);
}

private static ExtractedClinicalField SeedField(
    ClinicalDbContext pgDb,
    int patientId, string fieldName, string fieldValue,
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
        ExtractedAt     = extractedAt ?? DateTime.UtcNow,
        IsDeleted       = false
    };
    pgDb.ExtractedClinicalFields.Add(field);
    pgDb.SaveChanges();
    return field;
}

// Cache deletion verify pattern
cacheMock.Verify(
    c => c.DeleteAsync($"360view:{patientId}", It.IsAny<CancellationToken>()),
    Times.Once);

// Reschedule verify pattern (TC-007)
jobsMock.Verify(j => j.Create(
    It.Is<Job>(job => job.Type == typeof(DeduplicateClinicalFieldsJob)),
    It.IsAny<ScheduledState>()), Times.Once);
```

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Same-value duplicates | 2 fields: `FieldName="Medication"`, same `FieldValue`, extracted at different times | Older `IsDeleted=true`; newer `IsDeleted=false` |
| Conflicting values | 2 fields: same `FieldName`, `FieldValue="Metformin 500mg"` vs `"Metformin 1000mg"` | `ConflictFlag(Status=Unresolved)` inserted; both fields retained |
| Cache invalidation | 1 field; null Redis; `patientId=5` | `cache.DeleteAsync("360view:5")` called |
| No fields | Empty `ExtractedClinicalFields` for patientId=7 | No ConflictFlag; cache invalidated |
| Idempotency | Existing `ConflictFlag(Status=Unresolved)` + 2 conflicting fields | `ConflictFlags.Count()==1` (no duplicate) |
| Redis lock acquired | `LockTakeAsync` returns `true` | Dedup runs; cache deleted; `LockReleaseAsync` called |
| Redis lock busy | `LockTakeAsync` returns `false` | Reschedule job; pgDb unchanged |
| Three duplicates | 3 same-value fields ordered newest-first | 2 oldest `IsDeleted=true`; 1 newest `IsDeleted=false` |
| ConflictFlag values | 2 conflicting fields: `"120/80"` vs `"130/85"` | `flag.Value1 ≠ flag.Value2`; both values present |

## Test Commands

- **Run Tests**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~DeduplicateClinicalFieldsJobTests"`
- **Run with Coverage**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~DeduplicateClinicalFieldsJobTests"`
- **Run Single Test**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~DeduplicateClinicalFieldsJobTests.Dedup_SameValues_SoftDeletesOlder"`

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**: Null Redis path (no lock); Redis lock acquire path; Redis lock busy path (reschedule); no-fields early return; same-value soft-delete; conflicting-value ConflictFlag insert; idempotency guard (`existingUnresolvedFlags` check); cache delete call; `LockReleaseAsync` in finally block

## Documentation References

- **Framework Docs**: [xUnit.net](https://xunit.net/docs/getting-started/netcore/cmdline)
- **Mocking Guide**: [Moq Quickstart](https://github.com/moq/moq4/wiki/Quickstart)
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/ConflictEndpointsTests.cs`
- **Hangfire IBackgroundJobClient**: [Hangfire Client](https://docs.hangfire.io/en/latest/background-methods/calling-methods-in-background.html)
- **StackExchange.Redis**: [Redis Distributed Lock](https://stackexchange.github.io/StackExchange.Redis/Locks.html)

## Implementation Checklist

- [x] Create test file `tests/ClinicalHealthcare.Infrastructure.Tests/Features/DeduplicateClinicalFieldsJobTests.cs`
- [x] Set up `BuildPgDb()` and `SeedField()` helpers
- [x] Set up `Mock<ICacheService>` and `Mock<IBackgroundJobClient>` helpers
- [x] Implement null-Redis path tests (TC-001 through TC-005, TC-008)
- [x] Implement Redis lock tests (TC-006, TC-007) with `Mock<IConnectionMultiplexer>`
- [x] Implement edge case test (EC-001)
- [x] Run test suite and validate all 9 tests pass
