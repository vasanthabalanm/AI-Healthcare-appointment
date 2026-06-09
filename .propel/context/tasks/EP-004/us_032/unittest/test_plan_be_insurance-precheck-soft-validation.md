# Unit Test Plan - TASK_032

## Requirement Reference
- **User Story**: us_032
- **Story Location**: `.propel/context/tasks/EP-004/us_032/us_032.md`
- **Layer**: BE
- **Related Test Plans**: `EP-004/us_030/unittest/test_plan_be_manual-intake-form-submission.md` (insurance pre-check is wired into `SubmitManualIntakeEndpoint`)
- **Acceptance Criteria Covered**:
  - AC-001: Insurance pre-check is non-blocking; intake always returns 201 regardless of insurance status
  - AC-002: `InsuranceStatus` stored as `Validated`, `NotVerified`, or `Skipped` on `IntakeRecord`
  - AC-003: Pre-check queries `InsuranceReference` by `InsurerId + PlanCode`; requires `IsActive=true` for Validated
  - AC-004: Empty or missing insurance fields → `InsuranceStatus=Skipped`; no DB lookup executed

## Test Plan Overview

Tests `InsurancePreCheckService.CheckAsync` (service unit tests) and the integration between `SubmitManualIntakeEndpoint` and `IInsurancePreCheckService` (endpoint integration tests). Service tests use EF Core In-Memory database with seeded `InsuranceReference` records. Integration tests use a Moq mock of `IInsurancePreCheckService` to isolate insurance outcomes from endpoint logic. Covers all three `InsuranceStatus` states, inactive reference records, DB exception fall-back, and the non-blocking contract (intake always 201).

## Dependent Tasks

- TASK_001 (us_030) — `SubmitManualIntakeEndpoint` (pre-check runs inside this handler)
- TASK_001 (us_008) — `IntakeRecord` entity with `InsuranceStatus` field
- `InsuranceReference` entity — `InsurerId`, `PlanCode`, `IsActive` lookup table

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `InsurancePreCheckService` | class | `src/ClinicalHealthcare.Infrastructure/Services/InsurancePreCheckService.cs` | Queries `InsuranceReference`; returns `Validated`/`NotVerified`/`Skipped`; swallows exceptions |
| `IInsurancePreCheckService` | interface | `src/ClinicalHealthcare.Infrastructure/Services/InsurancePreCheckService.cs` | Abstraction enabling mock injection into `SubmitManualIntakeEndpoint` |
| `SubmitManualIntakeEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Intake/SubmitManualIntakeEndpoint.cs` | Calls pre-check after intake creation; stores result on `IntakeRecord` |
| `InsuranceReference` | entity | `src/ClinicalHealthcare.Infrastructure/Entities/InsuranceReference.cs` | Reference table: `InsurerId`, `PlanCode`, `IsActive` |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | Matching active InsuranceReference → Validated | Seed `InsuranceReference(InsurerId="BC001", PlanCode="GOLD", IsActive=true)` | `CheckAsync("BC001", "GOLD")` | Returns `InsuranceStatus.Validated` | `Assert.Equal(Validated, result)` |
| TC-002 | positive | InsurerId + PlanCode not in reference table → NotVerified | Empty `InsuranceReferences` table | `CheckAsync("UNKNOWN", "PLAN99")` | Returns `InsuranceStatus.NotVerified` | `Assert.Equal(NotVerified, result)` |
| TC-003 | positive | All empty/null field combinations → Skipped; no DB round-trip | 7 Theory variants: null/empty/whitespace for either or both fields | `CheckAsync(insurerId, planCode)` | Returns `InsuranceStatus.Skipped` | `Assert.Equal(Skipped, result)` for all 7 variants |
| TC-004 | positive | Intake with Validated insurance → 201 + IntakeRecord.InsuranceStatus=Validated | Mock pre-check returns `Validated` | `HandleSubmitManualIntake` with InsurerId="BC001", PlanCode="GOLD" | HTTP 201; DB record has `InsuranceStatus=Validated` | `StatusCode==201`; `record.InsuranceStatus==Validated` |
| TC-005 | positive | Intake with NotVerified insurance → 201 + IntakeRecord.InsuranceStatus=NotVerified | Mock pre-check returns `NotVerified` | `HandleSubmitManualIntake` with unknown insurance | HTTP 201; DB record has `InsuranceStatus=NotVerified` | `StatusCode==201`; `record.InsuranceStatus==NotVerified` |
| TC-006 | positive | Intake with no insurance fields → 201 + InsuranceStatus=Skipped | Mock pre-check returns `Skipped` | `HandleSubmitManualIntake` with no InsurerId/PlanCode | HTTP 201; `InsuranceStatus=Skipped` | `StatusCode==201`; `record.InsuranceStatus==Skipped` |
| TC-007 | negative | Found reference but IsActive=false → NotVerified | Seed `InsuranceReference(InsurerId="BC002", PlanCode="SILVER", IsActive=false)` | `CheckAsync("BC002", "SILVER")` | Returns `InsuranceStatus.NotVerified` | `Assert.Equal(NotVerified, result)` |
| TC-008 | negative | Pre-check returns NotVerified (simulated mock) → intake still 201 | Mock pre-check returns `NotVerified` | `HandleSubmitManualIntake` called | HTTP 201 (non-blocking contract) | `StatusCode==201`; `record.InsuranceStatus==NotVerified` |
| EC-001 | edge_case | Whitespace in policy number — lookup fails (normalisation not implemented) `[SOURCE:INFERRED]` | Seed `PlanCode="GOLD"`; query with `PlanCode=" GOLD "` | `CheckAsync("BC001", " GOLD ")` | Returns `NotVerified` (no trim in service) | `Assert.Equal(NotVerified, result)`. Basis: `InsurancePreCheckService` does not trim inputs; US story says trimming should occur — gap documented for follow-up |
| ES-001 | error | DB exception during lookup → NotVerified; no throw | Disposed `DbContext` used in service | `CheckAsync("ANY", "PLAN")` | Returns `InsuranceStatus.NotVerified`; no exception thrown | `Assert.Equal(NotVerified, result)` — no `Assert.ThrowsAsync` needed |

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/InsurancePreCheckTests.cs` | Add EC-001 (whitespace normalisation gap test) — all TC/ES tests already implemented |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `IInsurancePreCheckService` | Mock (Moq) | `CheckAsync` returns configurable `InsuranceStatus` | `Validated` / `NotVerified` / `Skipped` |
| `ApplicationDbContext` | In-Memory EF Core | `UseInMemoryDatabase(Guid.NewGuid().ToString())`; `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` | Per-test isolated store |
| `ILogger<InsurancePreCheckService>` | `NullLogger<InsurancePreCheckService>.Instance` | Discards all log calls | No-op |
| `HttpContext` | `DefaultHttpContext` | JWT sub claim = patientId | PatientId as string |

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Active match | `InsuranceReference(InsurerId="BC001", PlanCode="GOLD", IsActive=true)` | `CheckAsync("BC001","GOLD") → Validated` |
| No match | Empty InsuranceReferences | `CheckAsync("UNKNOWN","PLAN99") → NotVerified` |
| Inactive match | `InsuranceReference(IsActive=false)` | `CheckAsync → NotVerified` |
| Empty/null insurerId | `null`, `""`, `"  "` paired with any planCode | `Skipped` |
| Empty/null planCode | any insurerId paired with `null`, `""`, `"  "` | `Skipped` |
| DB exception | Disposed `DbContext` | `NotVerified` (no throw) |
| Whitespace policy | `PlanCode=" GOLD "` when table has `"GOLD"` | `NotVerified` (gap — trimming not implemented) |

## Test Commands

- **Run Tests**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~InsurancePreCheckTests"`
- **Run with Coverage**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~InsurancePreCheckTests"`
- **Run Single Test**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~InsurancePreCheckTests.TC_001"`

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**: `CheckAsync` null/empty guard (Skipped path); found/not-found branches; `IsActive=true` guard; exception catch block returning `NotVerified`

## Documentation References

- **Framework Docs**: [xUnit.net](https://xunit.net/docs/getting-started/netcore/cmdline)
- **Mocking Guide**: [Moq Quickstart](https://github.com/moq/moq4/wiki/Quickstart)
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/InsurancePreCheckTests.cs`
- **EF Core In-Memory**: [InMemory Database Provider](https://learn.microsoft.com/en-us/ef/core/providers/in-memory/)

## Implementation Checklist

- [x] Modify test file `tests/.../Features/InsurancePreCheckTests.cs`
- [x] Set up `BuildDb()`, `BuildPatientContext()` helpers
- [x] Implement `InsurancePreCheckService` unit tests (TC-001 to TC-003, TC-007, ES-001)
- [x] Implement endpoint integration tests (TC-004 to TC-006, TC-008)
- [x] Implement EC-001 (whitespace normalisation gap test and document finding)
- [x] Run test suite and validate coverage meets target
