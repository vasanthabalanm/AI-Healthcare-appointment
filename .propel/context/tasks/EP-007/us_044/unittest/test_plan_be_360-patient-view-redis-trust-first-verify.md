# Unit Test Plan - TASK_044

## Requirement Reference
- **User Story**: us_044
- **Story Location**: `.propel/context/tasks/EP-007/us_044/us_044.md`
- **Layer**: BE
- **Related Test Plans**:
  - `EP-007/us_042/unittest/test_plan_be_deduplication-hangfire-aggregation-job.md` (cache key `360view:{patientId}` invalidated after dedup)
  - `EP-007/us_043/unittest/test_plan_be_conflict-detection-staff-resolution.md` (IConflictService used by VerifyPatient)
- **Acceptance Criteria Covered**:
  - AC-001: Cache hit (`view360:{patientId}`) → return cached view from `ICacheService`; no PostgreSQL query; HTTP 200
  - AC-002: Cache miss → assemble from `ApplicationDbContext` + `ClinicalDbContext`; populate cache (TTL=300s); HTTP 200
  - AC-003: VerifyPatient — no unresolved conflicts + Unverified → HTTP 200; `VerificationStatus=Verified`; `VerifiedById=staffId`; `VerifiedAt=UtcNow`; cache invalidated
  - AC-004: VerifyPatient — unresolved conflicts → HTTP 409 with `{error, unresolvedCount}`
  - AC-005: Patient with no documents → HTTP 200 with `Hint="No clinical documents uploaded yet"` (not 404)

## Test Plan Overview

Tests `Get360ViewEndpoint.HandleGet360View` and `VerifyPatientEndpoint.HandleVerifyPatient`.

`ApplicationDbContext` (SQL Server side) and `ClinicalDbContext` (PostgreSQL side) both use InMemory EF Core
with per-test `Guid.NewGuid().ToString()` database names; `ApplicationDbContext` uses
`ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))`.
`ICacheService` is mocked via Moq (`GetAsync<PatientView360Dto>` returns null for cache-miss or a
pre-populated DTO for cache-hit).
`IConflictService` is mocked via Moq for `GetUnresolvedCountAsync`.
`HttpContext` carries a `sub` JWT claim for VerifyPatient.

**Note on AI tagging (AIR-006 — Trust-First pattern):** AIR-006 is a workflow pattern, not an LLM component.
The AI Component Test Cases section does not apply; skip that section.

**Status:** All 13 tests exist and pass across `Get360ViewEndpointTests.cs` (6) and `VerifyPatientEndpointTests.cs` (7). Test plan documents them as `[x]`.

## Dependent Tasks

- TASK_001 (Entities) — `UserAccount`, `VerificationStatus`, `ClinicalDocument`, `ExtractedClinicalField`, `ConflictFlag`
- TASK_001 (Data) — `ApplicationDbContext.UserAccounts`, `ApplicationDbContext.ClinicalDocuments`, `ClinicalDbContext.ExtractedClinicalFields`, `ClinicalDbContext.ConflictFlags`
- TASK_044 — `Get360ViewEndpoint`, `VerifyPatientEndpoint`, `PatientView360Dto`, `IConflictService`

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `Get360ViewEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Patients/Get360ViewEndpoint.cs` | Cache check; 404 guard; assemble from PG; count unresolved; hint if no docs; cache write |
| `VerifyPatientEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Patients/VerifyPatientEndpoint.cs` | JWT sub; 404 guard; unresolved check via IConflictService; already-verified 409; set Verified + VerifiedById + VerifiedAt; cache delete |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | Get360View cache hit → return cached DTO; no DB query; cache.SetAsync never called `[SOURCE:INPUT]` | `cache.GetAsync("view360:1")` returns pre-populated `PatientView360Dto`; no patient in sqlDb | `HandleGet360View(1, sqlDb, pgDb, cache, ct)` | HTTP 200; `value.FirstName=="Cached"`; `SetAsync` not called | `StatusCode==200`; `value != null`; `cacheMock.Verify(SetAsync, Times.Never)` |
| TC-002 | positive | Get360View cache miss → assemble from DB; cache.SetAsync called once with TTL=300s `[SOURCE:INPUT]` | Seed patient in sqlDb; seed `ExtractedClinicalField`; `cache.GetAsync` returns null | `HandleGet360View(1, sqlDb, pgDb, cache, ct)` | HTTP 200; view populated; `SetAsync` called with `"view360:1"`, TTL=300s | `StatusCode==200`; `value.ClinicalFields != empty`; `cacheMock.Verify(SetAsync("view360:1", ..., TimeSpan.FromSeconds(300), ...), Times.Once)` |
| TC-003 | negative | Get360View patient not found → HTTP 404 `[SOURCE:INFERRED]` | No `UserAccount` for `id=99`; `cache.GetAsync` returns null | `HandleGet360View(99, sqlDb, pgDb, cache, ct)` | HTTP 404 | `StatusCode==404` |
| TC-004 | positive | Get360View no documents → Hint present; HTTP 200 `[SOURCE:INPUT]` | Seed patient; no `ClinicalDocuments`; cache miss | `HandleGet360View(2, sqlDb, pgDb, cache, ct)` | HTTP 200; `value.Hint=="No clinical documents uploaded yet"` | `Assert.Equal("No clinical documents uploaded yet", value.Hint)` |
| TC-005 | positive | Get360View has documents → Hint is null `[SOURCE:INPUT]` | Seed patient; seed `ClinicalDocument`; cache miss | `HandleGet360View(3, sqlDb, pgDb, cache, ct)` | HTTP 200; `value.Hint==null` | `Assert.Null(value.Hint)` |
| TC-006 | positive | Get360View unresolved conflicts reflected in UnresolvedConflicts count `[SOURCE:INPUT]` | Seed patient; seed 2 `ConflictFlag(Status=Unresolved)` in pgDb | `HandleGet360View(4, sqlDb, pgDb, cache, ct)` | HTTP 200; `value.UnresolvedConflicts==2` | `Assert.Equal(2, value.UnresolvedConflicts)` |
| TC-007 | negative | VerifyPatient returns 401 when JWT sub claim missing `[SOURCE:INFERRED]` | `DefaultHttpContext()` with no claims | `HandleVerifyPatient(1, emptyCtx, sqlDb, conflict, cache, ct)` | HTTP 401 | `StatusCode==401` |
| TC-008 | negative | VerifyPatient returns 401 when JWT sub is non-integer `[SOURCE:INFERRED]` | `sub="not-an-int"` in claims | `HandleVerifyPatient(1, ctx, sqlDb, conflict, cache, ct)` | HTTP 401 | `StatusCode==401` |
| TC-009 | negative | VerifyPatient returns 404 when patient not found `[SOURCE:INFERRED]` | No `UserAccount` for `id=999`; valid sub JWT | `HandleVerifyPatient(999, ctx, sqlDb, conflict, cache, ct)` | HTTP 404 | `StatusCode==404` |
| TC-010 | negative | VerifyPatient returns 409 when unresolved conflicts exist `[SOURCE:INPUT]` | Seed patient; `conflictService.GetUnresolvedCountAsync(1)` → 2 | `HandleVerifyPatient(1, ctx, sqlDb, conflict, cache, ct)` | HTTP 409 | `StatusCode==409` |
| TC-011 | negative | VerifyPatient returns 409 when already Verified `[SOURCE:INFERRED]` | Seed patient `VerificationStatus=Verified`; no unresolved conflicts | `HandleVerifyPatient(1, ctx, sqlDb, conflict, cache, ct)` | HTTP 409 | `StatusCode==409` |
| TC-012 | positive | VerifyPatient sets VerificationStatus=Verified + VerifiedById + VerifiedAt → HTTP 200 `[SOURCE:INPUT]` | Seed patient `VerificationStatus=Unverified`; no conflicts; JWT sub="42" | `HandleVerifyPatient(1, ctx, sqlDb, conflict, cache, ct)` | HTTP 200; `patient.VerificationStatus==Verified`; `patient.VerifiedById==42`; `patient.VerifiedAt != null` | `StatusCode==200`; DB assertions |
| TC-013 | positive | VerifyPatient invalidates `view360:{patientId}` cache on success `[SOURCE:INPUT]` | Seed patient `VerificationStatus=Unverified`; no conflicts; `patientId=5` | `HandleVerifyPatient(5, ctx, sqlDb, conflict, cache, ct)` | `cache.DeleteAsync("view360:5", ...)` called once | `cacheMock.Verify(c => c.DeleteAsync("view360:5", It.IsAny<CancellationToken>()), Times.Once)` |

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| EXISTING | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/Get360ViewEndpointTests.cs` | TC-001 through TC-006 (6 tests) — all passing |
| EXISTING | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/VerifyPatientEndpointTests.cs` | TC-007 through TC-013 (7 tests) — all passing |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ApplicationDbContext` | In-Memory EF Core | `UseInMemoryDatabase(Guid.NewGuid().ToString())`; `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` | Per-test SQL Server side |
| `ClinicalDbContext` | In-Memory EF Core | `UseInMemoryDatabase(Guid.NewGuid().ToString())` | Per-test PostgreSQL side |
| `ICacheService` (miss) | `Mock<ICacheService>` | `.Setup(c => c.GetAsync<PatientView360Dto>(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((PatientView360Dto?)null)` | Null → cache miss |
| `ICacheService` (hit) | `Mock<ICacheService>` | `.Setup(c => c.GetAsync<PatientView360Dto>("view360:1", ...)).ReturnsAsync(cachedDto)` | Pre-populated DTO |
| `IConflictService` (no conflicts) | `Mock<IConflictService>` | `.Setup(s => s.GetUnresolvedCountAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(0)` | 0 conflicts |
| `IConflictService` (has conflicts) | `Mock<IConflictService>` | `.Setup(s => s.GetUnresolvedCountAsync(1, ...)).ReturnsAsync(2)` | 2 conflicts |
| `HttpContext` (staff) | `DefaultHttpContext` | `new ClaimsIdentity([new Claim("sub", staffId.ToString())], "test")` | JWT sub present |
| `HttpContext` (no claims) | `DefaultHttpContext` | Empty `ClaimsPrincipal` | 401 path |

### Helper Patterns

```csharp
private static ApplicationDbContext BuildSqlDb()
{
    var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options;
    return new ApplicationDbContext(opts);
}

private static ClinicalDbContext BuildPgDb()
{
    var opts = new DbContextOptionsBuilder<ClinicalDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options;
    return new ClinicalDbContext(opts);
}

private static Mock<ICacheService> NullCache()
{
    var cache = new Mock<ICacheService>(MockBehavior.Loose);
    cache.Setup(c => c.GetAsync<PatientView360Dto>(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync((PatientView360Dto?)null);
    return cache;
}

private static Mock<IConflictService> NoConflicts()
{
    var svc = new Mock<IConflictService>(MockBehavior.Loose);
    svc.Setup(s => s.GetUnresolvedCountAsync(
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
       .ReturnsAsync(0);
    return svc;
}

// Cache invalidation verify pattern (VerifyPatient)
cacheMock.Verify(
    c => c.DeleteAsync($"view360:{patientId}", It.IsAny<CancellationToken>()),
    Times.Once);

// Cache write verify pattern (Get360View cache miss)
cacheMock.Verify(
    c => c.SetAsync(
        It.Is<string>(k => k == $"view360:{patientId}"),
        It.IsAny<PatientView360Dto>(),
        It.Is<TimeSpan>(t => t.TotalSeconds == 300),
        It.IsAny<CancellationToken>()),
    Times.Once);
```

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Get360View — cache hit | `cache.GetAsync("view360:1")` returns `PatientView360Dto{FirstName="Cached"}` | 200; `FirstName=="Cached"`; `SetAsync` not called |
| Get360View — cache miss | Seed patient + `ExtractedClinicalField`; cache returns null | 200; `ClinicalFields` non-empty; `SetAsync` called once with TTL=300s |
| Get360View — not found | No UserAccount for id=99 | 404 |
| Get360View — no documents | Seed patient; no ClinicalDocuments | 200; `Hint=="No clinical documents uploaded yet"` |
| Get360View — has documents | Seed patient + ClinicalDocument | 200; `Hint==null` |
| Get360View — conflict count | Seed patient + 2 `ConflictFlag(Unresolved)` | 200; `UnresolvedConflicts==2` |
| VerifyPatient — 401 no sub | `DefaultHttpContext()` no claims | 401 |
| VerifyPatient — 401 bad sub | `sub="not-an-int"` | 401 |
| VerifyPatient — 404 | No UserAccount for id=999 | 404 |
| VerifyPatient — 409 conflicts | `GetUnresolvedCountAsync` returns 2 | 409 |
| VerifyPatient — 409 verified | Seed patient `VerificationStatus=Verified`; no conflicts | 409 |
| VerifyPatient — success | Seed patient `Unverified`; no conflicts; `sub="42"` | 200; `VerificationStatus=Verified`; `VerifiedById=42`; `VerifiedAt!=null` |
| VerifyPatient — cache invalidated | Seed patient `Unverified`; `patientId=5` | `cache.DeleteAsync("view360:5")` called once |

## Test Commands

- **Run Get360View Tests**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~Get360ViewEndpointTests"`
- **Run VerifyPatient Tests**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~VerifyPatientEndpointTests"`
- **Run All US_044 Tests**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~Get360ViewEndpointTests|FullyQualifiedName~VerifyPatientEndpointTests"`
- **Run with Coverage**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~Get360ViewEndpointTests|FullyQualifiedName~VerifyPatientEndpointTests"`

## Coverage Target

- **Line Coverage**: 95%
- **Branch Coverage**: 90%
- **Critical Paths (Get360View)**: Cache hit early return; patient not found 404; IsDeleted guard; fields query; unresolved count query; hasDocuments check; hint set branch; `cache.SetAsync` with TTL=300; `cache.GetAsync` miss branch
- **Critical Paths (VerifyPatient)**: JWT sub extraction + 401 branches; patient not found 404; `GetUnresolvedCountAsync` + 409 branch; `VerificationStatus==Verified` + 409 branch; `Status=Verified` + `VerifiedById` + `VerifiedAt` set; `cache.DeleteAsync`

## Documentation References

- **Framework Docs**: [xUnit.net](https://xunit.net/docs/getting-started/netcore/cmdline)
- **Mocking Guide**: [Moq Quickstart](https://github.com/moq/moq4/wiki/Quickstart)
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/ConflictEndpointsTests.cs`
- **Trust-First Verification**: `.propel/context/tasks/EP-007/us_044/us_044.md` (AC-003)
- **ASP.NET Core IResult**: [Results Class](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/responses)

## Implementation Checklist

- [x] Create test file `tests/ClinicalHealthcare.Infrastructure.Tests/Features/Get360ViewEndpointTests.cs`
- [x] Create test file `tests/ClinicalHealthcare.Infrastructure.Tests/Features/VerifyPatientEndpointTests.cs`
- [x] Set up `BuildSqlDb()`, `BuildPgDb()`, `NullCache()`, `NoConflicts()`, `SeedPatient()` helpers
- [x] Implement Get360View test cases (TC-001 through TC-006)
- [x] Implement VerifyPatient test cases (TC-007 through TC-013)
- [x] Run test suite and validate all 13 tests pass
