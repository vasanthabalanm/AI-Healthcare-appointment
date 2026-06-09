# Unit Test Plan - TASK_031

## Requirement Reference
- **User Story**: us_031
- **Story Location**: `.propel/context/tasks/EP-004/us_031/us_031.md`
- **Layer**: BE
- **Related Test Plans**: `EP-004/us_030/unittest/test_plan_be_manual-intake-form-submission.md` (initial version created by US_030)
- **Acceptance Criteria Covered**:
  - AC-001: `PATCH /intake/{intakeGroupId}` creates a new `IntakeRecord` version; prior version `IsLatest=false` (immutable)
  - AC-002: `GET /intake/{intakeGroupId}` (no version param) returns only the latest version
  - AC-003: `GET /intake/{intakeGroupId}?version=N` retrieves a specific historical version
  - AC-004: No-op PATCH (same field values) returns 200 without creating a new version row
  - AC-005: Patient can only edit their own intake; wrong patient → 403

## Test Plan Overview

Tests `UpdateIntakeEndpoint` (PATCH) and `GetIntakeEndpoint` (GET) for the intake versioning feature. EF Core In-Memory database stores multiple `IntakeRecord` versions under the same `IntakeGroupId`. Default query filter (`IsLatest=true`) is applied automatically; `IgnoreQueryFilters()` is used in historical version lookups. Patient ownership is validated via JWT sub claim. Tests cover: version creation, sequential numbering, null-field carry-forward, no-op detection, historical access, cross-patient ownership guard, and field-length validation.

## Dependent Tasks

- TASK_001 (us_008) — `IntakeRecord` entity with `IntakeGroupId`, `Version`, `IsLatest` fields
- TASK_030 — `SubmitManualIntakeEndpoint` creates the initial version (v1) for a given `IntakeGroupId`

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `UpdateIntakeEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Intake/UpdateIntakeEndpoint.cs` | Validates ownership, detects changes, marks current `IsLatest=false`, inserts new version |
| `GetIntakeEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Intake/GetIntakeEndpoint.cs` | Returns latest (default filter) or specific version (`IgnoreQueryFilters`) |
| `UpdateIntakeRequest` | record | `src/ClinicalHealthcare.Api/Features/Intake/UpdateIntakeRequest.cs` | All-optional PATCH DTO with `[MaxLength]` validation |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | PATCH with changed value creates v2; v1 marked IsLatest=false | Seed v1 (PatientId=1, `ChiefComplaint="Headache"`); request `ChiefComplaint="Severe migraine"` | `HandleUpdateIntake` called | HTTP 200; two rows exist; v1.IsLatest=false; v2.IsLatest=true; v2.ChiefComplaint="Severe migraine" | `StatusCode==200`; `all.Count==2`; `v1.IsLatest==false`; `v2.Version==2`; unchanged fields copied from v1 |
| TC-002 | positive | Sequential PATCH calls produce Version 1→2→3 | Two successive PATCHes on same IntakeGroupId | Both `HandleUpdateIntake` calls complete | Three rows; versions [1,2,3]; only v3 IsLatest=true | `versions==new[]{1,2,3}` via `IgnoreQueryFilters().OrderBy(r=>r.Version)` |
| TC-003 | positive | Null fields in PATCH request → unchanged fields carried from prior version | Seed v1 with all fields set; request `CurrentMeds="Paracetamol"` (others null) | `HandleUpdateIntake` called | v2 created; only CurrentMeds updated; other fields copied unchanged | `v2.ChiefComplaint==v1.ChiefComplaint`; `v2.CurrentMeds=="Paracetamol"` |
| TC-004 | positive | No-op PATCH (same values) → 200; no new version row | Seed v1; request with identical field values | `HandleUpdateIntake` called | HTTP 200 with `noOp=true`; still only one row | `StatusCode==200`; `db.IntakeRecords.IgnoreQueryFilters().Count(r=>r.IntakeGroupId==groupId)==1` |
| TC-005 | positive | GET without version → returns latest version only | Seed v1 (IsLatest=false) + v2 (IsLatest=true) with same IntakeGroupId | `HandleGetIntake(groupId, null, ...)` called | HTTP 200; response contains v2 data | `StatusCode==200`; response JSON contains `"version":2`; v2 ChiefComplaint value present |
| TC-006 | positive | GET ?version=1 returns historical v1 | Same v1+v2 seed | `HandleGetIntake(groupId, 1, ...)` called | HTTP 200; response contains v1 data | `StatusCode==200`; response JSON contains `"version":1`; v1 ChiefComplaint value present |
| TC-007 | negative | PATCH wrong patient → 403 Forbidden; no new version created | Seed v1 (PatientId=5); JWT PatientId=99 | `HandleUpdateIntake` called | ForbidHttpResult; record count unchanged | `IsForbid(result)==true`; `db.IntakeRecords.IgnoreQueryFilters().Count()==1` |
| TC-008 | negative | PATCH unknown intakeGroupId → 404 Not Found | Empty DB | `HandleUpdateIntake(Guid.NewGuid(), ...)` called | HTTP 404 | `StatusCode==404` |
| EC-001 | edge_case | Empty-string ChiefComplaint treated as no change (null carry-forward) | Seed v1 with ChiefComplaint="Headache"; request `ChiefComplaint=""` | `HandleUpdateIntake` called | HTTP 200; no new version (empty string is not a change); v1.ChiefComplaint preserved | `StatusCode==200`; count remains 1; `latest.ChiefComplaint==v1.ChiefComplaint` |
| EC-002 | edge_case | GET ?version=N where N does not exist → 404 | Seed v1 only (Version=1) | `HandleGetIntake(groupId, 99, ...)` called | HTTP 404 | `StatusCode==404` |
| EC-003 | edge_case | Patient role caller cannot read another patient's intake | Seed record (PatientId=20); JWT PatientId=99 | `HandleGetIntake(groupId, null, BuildPatientContext(99), ...)` called | ForbidHttpResult | `IsForbid(result)==true` |
| ES-001 | error | PATCH without JWT sub → 401 Unauthorized | `DefaultHttpContext` with no claims | `HandleUpdateIntake` called | HTTP 401 | `StatusCode==401` |
| ES-002 | error | ChiefComplaint exceeds 1000 chars → 422; no new version | Seed v1; `ChiefComplaint=new string('x', 1001)` | `HandleUpdateIntake` called | HTTP 422; count unchanged | `StatusCode==422`; `count==1` |
| ES-003 | error | CurrentMeds or Allergies exceeds 2000 chars → 422; no new version | Seed v1; `CurrentMeds=new string('x', 2001)` | `HandleUpdateIntake` called | HTTP 422 | `StatusCode==422`; `count==1` |
| ES-004 | error | MedicalHistory exceeds 4000 chars → 422; no new version `[SOURCE:INFERRED]` | Seed v1; `MedicalHistory=new string('x', 4001)` | `HandleUpdateIntake` called | HTTP 422 | `StatusCode==422`; `count==1`. Basis: MaxLength(4000) on MedicalHistory field not covered by existing tests |

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/IntakeVersioningEndpointTests.cs` | Add ES-004 (MedicalHistory MaxLength test) — all other tests already implemented |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ApplicationDbContext` | In-Memory EF Core | `UseInMemoryDatabase(Guid.NewGuid().ToString())`; `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` | Per-test isolated store |
| `HttpContext` (patient) | `DefaultHttpContext` | JWT sub claim = patientId; no role claims | PatientId only |
| `HttpContext` (staff) | `DefaultHttpContext` | `ClaimTypes.Role="staff"` claim; staff can read any patient's intake | Staff role |

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Single v1 | `PatientId=1`, `ChiefComplaint="Headache"`, `Version=1`, `IsLatest=true` | Baseline seeded record |
| Two versions | v1 `IsLatest=false` + v2 `IsLatest=true`, same `IntakeGroupId` | GET without version → v2; GET ?version=1 → v1 |
| No-op PATCH | Request with exact same values as v1 | 200; count stays 1 |
| MaxLength boundary | `ChiefComplaint=new string('x', 1001)` | 422 |

## Test Commands

- **Run Tests**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~IntakeVersioningEndpointTests"`
- **Run with Coverage**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~IntakeVersioningEndpointTests"`
- **Run Single Test**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~IntakeVersioningEndpointTests.TC_001"`

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**: No-op PATCH detection (both true/false); `IgnoreQueryFilters()` historical fetch path; ownership guard (403); all `[MaxLength]` boundaries

## Documentation References

- **Framework Docs**: [xUnit.net](https://xunit.net/docs/getting-started/netcore/cmdline)
- **Mocking Guide**: [Moq Quickstart](https://github.com/moq/moq4/wiki/Quickstart)
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/IntakeVersioningEndpointTests.cs`
- **EF Core Query Filters**: [Global Query Filters](https://learn.microsoft.com/en-us/ef/core/querying/filters)

## Implementation Checklist

- [x] Modify test file `tests/.../Features/IntakeVersioningEndpointTests.cs`
- [x] Set up `BuildDb()`, `BuildPatientContext()`, `BuildStaffContext()`, `SeedLatestRecord()` helpers
- [x] Implement positive test cases (TC-001 to TC-006)
- [x] Implement negative test cases (TC-007 to TC-008)
- [x] Implement edge case tests (EC-001 to EC-003)
- [x] Implement error scenario tests (ES-001 to ES-003)
- [x] Implement ES-004 (MedicalHistory MaxLength boundary test)
- [x] Run test suite and validate coverage meets target
