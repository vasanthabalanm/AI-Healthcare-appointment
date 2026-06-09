# Unit Test Plan - TASK_030

## Requirement Reference
- **User Story**: us_030
- **Story Location**: `.propel/context/tasks/EP-004/us_030/us_030.md`
- **Layer**: BE
- **Related Test Plans**: `EP-004/us_032/unittest/test_plan_be_insurance-precheck-soft-validation.md` (insurance pre-check integration shares endpoint)
- **Acceptance Criteria Covered**:
  - AC-001: `POST /intake/manual` creates `IntakeRecord(Source=Manual, Version=1, IsLatest=true)` and returns 201
  - AC-002: Missing or invalid required fields return 422 with per-field validation errors
  - AC-003: Pre-populated fields from AI session included in request are preserved in the record
  - AC-004: Duplicate submission (existing IsLatest=true for same patient) returns 409; no new record inserted

## Test Plan Overview

Tests `SubmitManualIntakeEndpoint.HandleSubmitManualIntake` covering the full validation–duplicate-check–persistence pipeline. `IInsurancePreCheckService` is mocked as a no-op returning `Skipped` in most tests (insurance-specific coverage is in the US_032 test plan). EF Core In-Memory database is used for persistence assertions. Patient ID is sourced exclusively from the JWT sub claim (OWASP A01). Structural test verifies no raw SQL string interpolation exists in the endpoint source.

## Dependent Tasks

- TASK_001 (us_008) — `IntakeRecord` entity

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `SubmitManualIntakeEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Intake/SubmitManualIntakeEndpoint.cs` | Validates DTO, checks duplicate, persists `IntakeRecord(Source=Manual)`, calls insurance pre-check |
| `ManualIntakeRequest` | record | `src/ClinicalHealthcare.Api/Features/Intake/ManualIntakeRequest.cs` | DTO with `[Required]` + `[MaxLength]` data-annotation validation |
| `IInsurancePreCheckService` | interface | `src/ClinicalHealthcare.Infrastructure/Services/InsurancePreCheckService.cs` | Mocked as no-op in this plan; integration tested in US_032 plan |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | Valid full submission creates IntakeRecord and returns 201 | Patient JWT (userId=10); all required + optional fields set | `HandleSubmitManualIntake` called | HTTP 201; `IntakeRecord` persisted with `Source=Manual`, `Version=1`, `IsLatest=true`, correct field values | `StatusCode==201`; `db.IntakeRecords.Single().Source==Manual`; `record.Version==1`; `record.IsLatest==true`; `IntakeGroupId!=Guid.Empty` |
| TC-002 | positive | Optional fields null → 201; IntakeRecord created with null optional fields | `ChiefComplaint="Fever"`; all optional fields absent | `HandleSubmitManualIntake` called | HTTP 201; `CurrentMeds`, `Allergies`, `MedicalHistory` all null in DB | `StatusCode==201`; `record.CurrentMeds==null`; `record.Allergies==null` |
| TC-003 | positive | Pre-populated fields from AI session preserved | Request includes `ChiefComplaint="headache"` from AI session switch; optional fields also set | `HandleSubmitManualIntake` called | HTTP 201; all fields (including AI-originated) persisted | `StatusCode==201`; `record.ChiefComplaint=="headache"`; `record.Source==Manual` (overrides AI) |
| TC-004 | negative | Missing ChiefComplaint → 422 with per-field error | `ChiefComplaint=""` (empty string) | `HandleSubmitManualIntake` called | HTTP 422; no IntakeRecord inserted | `StatusCode==422`; `db.IntakeRecords.Count()==0` |
| TC-005 | negative | Whitespace-only ChiefComplaint → 422 (normalised by trim before validation) | `ChiefComplaint="   "` | `HandleSubmitManualIntake` called | HTTP 422 | `StatusCode==422` |
| TC-006 | negative | ChiefComplaint exceeds 1000 chars → 422 | `ChiefComplaint=new string('x', 1001)` | `HandleSubmitManualIntake` called | HTTP 422 | `StatusCode==422` |
| TC-007 | negative | Existing IsLatest=true intake for patient → 409; no new record inserted | Seed `IntakeRecord(PatientId=20, IsLatest=true)` in DB | `HandleSubmitManualIntake` called with PatientId=20 | HTTP 409; record count remains 1 | `StatusCode==409`; `db.IntakeRecords.Count()==1` |
| TC-008 | negative | No JWT sub claim → 401 Unauthorized | `DefaultHttpContext` with no claims | `HandleSubmitManualIntake` called | HTTP 401 | `StatusCode==401` |
| EC-001 | edge_case | Prior IsLatest=false record does not block new submission | Seed `IntakeRecord(PatientId=21, IsLatest=false)` in DB | `HandleSubmitManualIntake` called with PatientId=21 | HTTP 201; new record created | `StatusCode==201`; `db.IntakeRecords.Count()==2` |
| EC-002 | edge_case | Optional field CurrentMeds exceeds 2000 chars → 422 | `CurrentMeds=new string('x', 2001)` with valid ChiefComplaint | `HandleSubmitManualIntake` called | HTTP 422 | `StatusCode==422` |
| EC-003 | edge_case | Optional field Allergies exceeds 2000 chars → 422 | `Allergies=new string('x', 2001)` with valid ChiefComplaint | `HandleSubmitManualIntake` called | HTTP 422 | `StatusCode==422` |
| ES-001 | error | No raw SQL interpolation in endpoint source (injection guard) | Endpoint source file on disk | Read file; grep for `FromSqlRaw($`, `FromSqlInterpolated(`, `ExecuteSqlRaw($` | None found | `Assert.DoesNotContain("FromSqlRaw($", source)`; `Assert.DoesNotContain("ExecuteSqlRaw($", source)` |
| ES-002 | error | InsurerId exceeds 100 chars → 422 (MaxLength boundary) | `InsurerId=new string('X', 101)` with valid ChiefComplaint | `HandleSubmitManualIntake` called | HTTP 422 | `StatusCode==422` |
| ES-003 | error | PlanCode exceeds 100 chars → 422 (MaxLength boundary) | `PlanCode=new string('Y', 101)` with valid ChiefComplaint | `HandleSubmitManualIntake` called | HTTP 422 | `StatusCode==422` |

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/SubmitManualIntakeEndpointTests.cs` | All TC-001 to ES-003 tests already implemented |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `IInsurancePreCheckService` | Mock (Moq) | `CheckAsync` returns `InsuranceStatus.Skipped` (no-op) | `InsuranceStatus.Skipped` |
| `ApplicationDbContext` | In-Memory EF Core | `UseInMemoryDatabase(Guid.NewGuid().ToString())`; `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` | Real in-memory store per test |
| `HttpContext` | `DefaultHttpContext` | JWT sub claim set via `ClaimsPrincipal` with `JwtRegisteredClaimNames.Sub` value = patientId | PatientId integer as string |

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Valid full | `ChiefComplaint="Persistent headache"`, `CurrentMeds="Ibuprofen 400mg"`, `Allergies="Penicillin"`, `MedicalHistory="Hypertension"` | `StatusCode=201`; all fields in DB |
| Optional null | `ChiefComplaint="Fever"` only | `StatusCode=201`; optional fields null |
| AI pre-populated | `ChiefComplaint="headache"` (from AI), additional manual fields | `StatusCode=201`; `Source=Manual` |
| Missing required | `ChiefComplaint=""` | `StatusCode=422` |
| Whitespace required | `ChiefComplaint="   "` | `StatusCode=422` |
| MaxLength violation | `ChiefComplaint=new string('x', 1001)` | `StatusCode=422` |
| Duplicate | Seed existing `IsLatest=true` for same patient | `StatusCode=409` |
| Archived prior | Seed `IsLatest=false` for same patient | `StatusCode=201` |

## Test Commands

- **Run Tests**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~SubmitManualIntakeEndpointTests"`
- **Run with Coverage**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~SubmitManualIntakeEndpointTests"`
- **Run Single Test**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~SubmitManualIntakeEndpointTests.TC_001"`

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**: Validation branch (422 path); duplicate check (409 path); no JWT sub (401 path); `FromSqlRaw` absence assertion

## Documentation References

- **Framework Docs**: [xUnit.net](https://xunit.net/docs/getting-started/netcore/cmdline)
- **Mocking Guide**: [Moq Quickstart](https://github.com/moq/moq4/wiki/Quickstart)
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/SubmitManualIntakeEndpointTests.cs`
- **ASP.NET Core Validation**: [Model Validation](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/validation)

## Implementation Checklist

- [x] Modify test file `tests/.../Features/SubmitManualIntakeEndpointTests.cs`
- [x] Set up `BuildDb()`, `BuildPatientContext()`, `NoOpCheck()`, `ValidRequest()` helpers
- [x] Implement positive test cases (TC-001 to TC-003)
- [x] Implement negative test cases (TC-004 to TC-008)
- [x] Implement edge case tests (EC-001 to EC-003)
- [x] Implement error scenario tests (ES-001 to ES-003)
- [x] Run test suite and validate coverage meets target
