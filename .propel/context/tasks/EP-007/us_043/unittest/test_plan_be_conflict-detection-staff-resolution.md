# Unit Test Plan - TASK_043

## Requirement Reference
- **User Story**: us_043
- **Story Location**: `.propel/context/tasks/EP-007/us_043/us_043.md`
- **Layer**: BE
- **Related Test Plans**:
  - `EP-007/us_042/unittest/test_plan_be_deduplication-hangfire-aggregation-job.md` (DeduplicateClinicalFieldsJob inserts ConflictFlag rows)
  - `EP-007/us_044/unittest/test_plan_be_360-patient-view-redis-trust-first-verify.md` (VerifyPatient uses IConflictService)
- **Acceptance Criteria Covered**:
  - AC-001: `GET /patients/{id}/conflicts` returns all ConflictFlags for the patient (Unresolved / Resolved / Dismissed)
  - AC-002: `PATCH /conflicts/{id}/resolve` → Status=Resolved, ResolvedByStaffId=staffId from JWT sub
  - AC-003: `PATCH /conflicts/{id}/dismiss` → Status=Dismissed (no staff ID stored in source)
  - AC-004: ConflictService helpers used by VerifyPatient — `HasUnresolvedConflictsAsync` and `GetUnresolvedCountAsync`

## Test Plan Overview

Tests `GetConflictsEndpoint.HandleGetConflicts`, `ResolveConflictEndpoint.HandleResolveConflict`,
`DismissConflictEndpoint.HandleDismissConflict`, and `ConflictService`.

`ClinicalDbContext` uses InMemory EF Core with per-test `Guid.NewGuid().ToString()` (no transactions config).
`HttpContext` is built with `DefaultHttpContext` + `ClaimsPrincipal` carrying a `sub` JWT claim.
`ILogger` is not injected by these endpoints — no logger mock required.

**Gap noted:**
- AC-003 specifies `{staffId, reason}` for dismiss and stores the dismiss reason; the source
  `DismissConflictEndpoint` does not read JWT sub and does not store `staffId` or `reason` — only sets
  `Status=Dismissed`. Tests `TC-010`, `TC-011` verify source behaviour as `[SOURCE:INPUT]`.

**Status:** All 16 tests exist and pass in `ConflictEndpointsTests.cs`. Test plan documents them as `[x]`.

## Dependent Tasks

- TASK_001 (Entities) — `ConflictFlag`, `ConflictFlagStatus`
- TASK_001 (Data) — `ClinicalDbContext.ConflictFlags`
- TASK_043 — `GetConflictsEndpoint`, `ResolveConflictEndpoint`, `DismissConflictEndpoint`, `ConflictService`, `IConflictService`

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `GetConflictsEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Patients/GetConflictsEndpoint.cs` | `WHERE PatientId==id ORDER BY Id` → 200 with list |
| `ResolveConflictEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Patients/ResolveConflictEndpoint.cs` | JWT sub → staffId; find flag; guard status; set Resolved + ResolvedByStaffId |
| `DismissConflictEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Patients/DismissConflictEndpoint.cs` | Find flag; guard Unresolved; set Dismissed |
| `ConflictService` | class | `src/ClinicalHealthcare.Infrastructure/Services/ConflictService.cs` | `HasUnresolvedConflictsAsync`, `GetUnresolvedCountAsync` |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | GetConflicts returns all status flags for patient `[SOURCE:INPUT]` | Seed 3 flags: Unresolved, Resolved, Dismissed for `patientId=1` | `HandleGetConflicts(1, pgDb, ct)` | HTTP 200; list contains all 3 flags | `StatusCode==200`; `flags.Count==3`; all 3 statuses present |
| TC-002 | positive | GetConflicts returns only flags for requested patient (tenant isolation) `[SOURCE:INPUT]` | Seed 1 flag for `patientId=1`, 1 flag for `patientId=2` | `HandleGetConflicts(1, pgDb, ct)` | HTTP 200; list contains 1 flag; `PatientId==1` | `flags.Count==1`; `Assert.All(flags, f => Assert.Equal(1, f.PatientId))` |
| TC-003 | positive | GetConflicts returns empty list when no flags exist `[SOURCE:INPUT]` | Empty `ConflictFlags` table | `HandleGetConflicts(99, pgDb, ct)` | HTTP 200; empty list | `flags.Count==0` |
| TC-004 | positive | ResolveConflict sets Status=Resolved + ResolvedByStaffId from JWT sub `[SOURCE:INPUT]` | Seed `ConflictFlag(Status=Unresolved)`; `BuildStaffContext(staffId=42)` | `HandleResolveConflict(flagId, ctx, pgDb, ct)` | HTTP 200; `flag.Status==Resolved`; `flag.ResolvedByStaffId==42` | `StatusCode==200`; DB assertions match |
| TC-005 | negative | ResolveConflict returns 404 when flag not found `[SOURCE:INFERRED]` | Empty `ConflictFlags` table | `HandleResolveConflict(9999, ctx, pgDb, ct)` | HTTP 404 | `StatusCode==404` |
| TC-006 | negative | ResolveConflict returns 409 when flag already Resolved `[SOURCE:INFERRED]` | Seed `ConflictFlag(Status=Resolved)` | `HandleResolveConflict(flagId, ctx, pgDb, ct)` | HTTP 409 | `StatusCode==409` |
| TC-007 | negative | ResolveConflict returns 409 when flag already Dismissed `[SOURCE:INFERRED]` | Seed `ConflictFlag(Status=Dismissed)` | `HandleResolveConflict(flagId, ctx, pgDb, ct)` | HTTP 409 | `StatusCode==409` |
| TC-008 | negative | ResolveConflict returns 401 when JWT sub claim missing `[SOURCE:INFERRED]` | Seed flag; `DefaultHttpContext()` with no claims | `HandleResolveConflict(flagId, emptyCtx, pgDb, ct)` | HTTP 401 | `StatusCode==401` |
| TC-009 | negative | ResolveConflict returns 401 when JWT sub is non-integer `[SOURCE:INFERRED]` | Seed flag; ctx with `sub="not-an-int"` | `HandleResolveConflict(flagId, ctx, pgDb, ct)` | HTTP 401 | `StatusCode==401` |
| TC-010 | positive | DismissConflict sets Status=Dismissed (no staff ID stored) **[SOURCE:INPUT] Gap: US AC-003 expects staffId + reason stored; source stores neither** | Seed `ConflictFlag(Status=Unresolved)` | `HandleDismissConflict(flagId, pgDb, ct)` | HTTP 200; `flag.Status==Dismissed` | `StatusCode==200`; `Assert.Equal(ConflictFlagStatus.Dismissed, flag.Status)` |
| TC-011 | negative | DismissConflict returns 404 when flag not found `[SOURCE:INFERRED]` | Empty table | `HandleDismissConflict(9999, pgDb, ct)` | HTTP 404 | `StatusCode==404` |
| TC-012 | negative | DismissConflict returns 409 when flag already Resolved `[SOURCE:INFERRED]` | Seed `ConflictFlag(Status=Resolved)` | `HandleDismissConflict(flagId, pgDb, ct)` | HTTP 409 | `StatusCode==409` |
| TC-013 | negative | DismissConflict returns 409 when flag already Dismissed `[SOURCE:INFERRED]` | Seed `ConflictFlag(Status=Dismissed)` | `HandleDismissConflict(flagId, pgDb, ct)` | HTTP 409 | `StatusCode==409` |
| TC-014 | positive | ConflictService.HasUnresolvedConflictsAsync returns true when Unresolved flag exists `[SOURCE:INPUT]` | Seed `ConflictFlag(Status=Unresolved)` for `patientId=5` | `svc.HasUnresolvedConflictsAsync(5)` | `true` | `Assert.True(result)` |
| TC-015 | positive | ConflictService.HasUnresolvedConflictsAsync returns false when no flags `[SOURCE:INPUT]` | Empty table | `svc.HasUnresolvedConflictsAsync(99)` | `false` | `Assert.False(result)` |
| TC-016 | positive | ConflictService.HasUnresolvedConflictsAsync ignores Resolved and Dismissed flags `[SOURCE:INPUT]` | Seed Resolved + Dismissed flags for `patientId=7`; no Unresolved | `svc.HasUnresolvedConflictsAsync(7)` | `false` | `Assert.False(result)` |

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| EXISTING | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/ConflictEndpointsTests.cs` | All 16 tests already implemented and passing |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ClinicalDbContext` | In-Memory EF Core | `UseInMemoryDatabase(Guid.NewGuid().ToString())` | Per-test isolated store |
| `HttpContext` (staff) | `DefaultHttpContext` | `ClaimsPrincipal` with `new Claim(JwtRegisteredClaimNames.Sub, staffId.ToString())` | JWT sub present |
| `HttpContext` (no claims) | `DefaultHttpContext` | Empty `ClaimsPrincipal` | 401 path |
| `HttpContext` (bad sub) | `DefaultHttpContext` | `new Claim(JwtRegisteredClaimNames.Sub, "not-an-int")` | 401 path |
| `ConflictService` | Real implementation | `new ConflictService(pgDb)` — uses same InMemory context | Real query logic |

### Helper Pattern

```csharp
private static ClinicalDbContext CreatePgDb()
{
    var opts = new DbContextOptionsBuilder<ClinicalDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options;
    return new ClinicalDbContext(opts);
}

private static HttpContext BuildStaffContext(int staffId = 10)
{
    var ctx = new DefaultHttpContext();
    ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(JwtRegisteredClaimNames.Sub, staffId.ToString())],
        "TestAuth"));
    return ctx;
}

private static ConflictFlag SeedFlag(
    ClinicalDbContext pgDb,
    int patientId,
    ConflictFlagStatus status = ConflictFlagStatus.Unresolved)
{
    var flag = new ConflictFlag
    {
        PatientId = patientId,
        FieldName = "BloodPressure",
        Value1    = "120/80",
        Value2    = "130/85",
        Status    = status
    };
    pgDb.ConflictFlags.Add(flag);
    pgDb.SaveChanges();
    return flag;
}
```

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| GetConflicts — all statuses | 3 flags: Unresolved, Resolved, Dismissed; `patientId=1` | 200; list of 3 |
| GetConflicts — patient isolation | 1 flag `patientId=1`, 1 flag `patientId=2` | 200; list of 1; `PatientId=1` |
| GetConflicts — empty | No flags | 200; empty list |
| Resolve — success | `ConflictFlag(Unresolved)`; `staffId=42` | 200; `Status=Resolved`; `ResolvedByStaffId=42` |
| Resolve — not found | No flag for id=9999 | 404 |
| Resolve — already Resolved | `ConflictFlag(Resolved)` | 409 |
| Resolve — already Dismissed | `ConflictFlag(Dismissed)` | 409 |
| Resolve — no JWT sub | `DefaultHttpContext()` no claims | 401 |
| Resolve — non-integer sub | `sub="not-an-int"` | 401 |
| Dismiss — success | `ConflictFlag(Unresolved)` | 200; `Status=Dismissed` [gap: no staffId/reason stored] |
| Dismiss — not found | No flag for id=9999 | 404 |
| Dismiss — already Resolved | `ConflictFlag(Resolved)` | 409 |
| Dismiss — already Dismissed | `ConflictFlag(Dismissed)` | 409 |
| HasUnresolved — true | 1 `Unresolved` flag for patientId=5 | `true` |
| HasUnresolved — false | No flags for patientId=99 | `false` |
| HasUnresolved — ignores closed | `Resolved` + `Dismissed` only for patientId=7 | `false` |

## Test Commands

- **Run Tests**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~ConflictEndpointsTests"`
- **Run with Coverage**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~ConflictEndpointsTests"`
- **Run Single Test**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~ConflictEndpointsTests.ResolveConflict_SetsResolvedWithStaffId"`

## Coverage Target

- **Line Coverage**: 95%
- **Branch Coverage**: 90%
- **Critical Paths**: GetConflicts patientId filter; ResolveConflict JWT sub extraction + 401 branches; FindAsync + 404 branch; status guard (Resolved/Dismissed) + 409 branch; `Status=Resolved` set + `ResolvedByStaffId` set; DismissConflict status guard (`!=Unresolved`) + 409 branch; `Status=Dismissed` set; ConflictService `AnyAsync` + `CountAsync` predicates

## Documentation References

- **Framework Docs**: [xUnit.net](https://xunit.net/docs/getting-started/netcore/cmdline)
- **Mocking Guide**: [Moq Quickstart](https://github.com/moq/moq4/wiki/Quickstart)
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/ConflictEndpointsTests.cs`
- **ASP.NET Core IResult**: [Results Class](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/responses)

## Implementation Checklist

- [x] Create test file `tests/ClinicalHealthcare.Infrastructure.Tests/Features/ConflictEndpointsTests.cs`
- [x] Set up `CreatePgDb()`, `BuildStaffContext()`, `SeedFlag()` helpers
- [x] Implement GetConflicts test cases (TC-001, TC-002, TC-003)
- [x] Implement ResolveConflict test cases (TC-004 through TC-009)
- [x] Implement DismissConflict test cases (TC-010 through TC-013)
- [x] Implement ConflictService test cases (TC-014, TC-015, TC-016)
- [x] Run test suite and validate all 16 tests pass
