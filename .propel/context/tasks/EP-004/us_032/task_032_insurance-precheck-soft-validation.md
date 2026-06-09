# Task - TASK_032

## Requirement Reference

- **User Story**: US_032 — Insurance pre-check soft validation
- **Story Location**: `.propel/context/tasks/EP-004/us_032/us_032.md`
- **Parent Epic**: EP-004

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | Insurance pre-check is non-blocking; intake always returns 201 regardless of insurance status |
| AC-002 | Insurance status stored as `Validated`, `NotVerified`, or `Skipped` on `IntakeRecord` |
| AC-003 | Pre-check result based on lookup in `InsuranceReference` table |
| AC-004 | Empty or missing insurance fields → status = `Skipped` |

### Edge Cases

- `InsuranceReference` table empty → all results = `NotVerified`; intake proceeds normally
- Insurance lookup throws unexpected error → catch, log WARNING, set status = `NotVerified`; intake still creates 201

---

## Design References

N/A — UI Impact: No

---

## AI References

N/A — AI Impact: No

---

## Mobile References

N/A — Mobile Impact: No

---

## Applicable Technology Stack

| Layer | Technology | Version | Justification |
|-------|-----------|---------|---------------|
| Backend | ASP.NET Core Web API | 8 LTS | Endpoint hosting |
| Backend | EF Core | 8.x | InsuranceReference lookup |
| Database | SQL Server | 2022 / Express | InsuranceReference table |

---

## Task Overview

Add soft insurance pre-check to the intake flow. Lookup insurance details in `InsuranceReference` table after intake submission. Store non-blocking status (`Validated`/`NotVerified`/`Skipped`) on `IntakeRecord`. Intake always returns 201.

---

## Dependent Tasks

- **TASK_001 (us_030)** — `SubmitManualIntakeEndpoint` (pre-check runs here)
- **TASK_001 (us_008)** — `IntakeRecord` entity (insurance status field)

---

## Impacted Components

- `src/ClinicalHealthcare.Infrastructure/Entities/InsuranceReference.cs`
- `src/ClinicalHealthcare.Infrastructure/Services/InsurancePreCheckService.cs`
- `src/ClinicalHealthcare.Api/Features/Intake/SubmitManualIntakeEndpoint.cs`
- `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs`

---

## Implementation Plan

1. Create `InsuranceReference` entity: `Id`, `InsurerId`, `InsurerName`, `PlanCode`, `IsActive`; register in `ApplicationDbContext`; create migration.
2. Add `InsuranceStatus` enum (`Validated/NotVerified/Skipped`) and `InsuranceStatus` field to `IntakeRecord`; create migration.
3. Create `InsurancePreCheckService.CheckAsync(insurerId, planCode)`: query `InsuranceReference` by `InsurerId` + `PlanCode`; return `Validated` if found and `IsActive=true`, `NotVerified` if not found, `Skipped` if input is empty.
4. In `SubmitManualIntakeEndpoint`: after `IntakeRecord` created, call `InsurancePreCheckService.CheckAsync(...)` in a try/catch; set `IntakeRecord.InsuranceStatus`; save; return 201 regardless of status.
5. Wrap pre-check in try/catch → catch → WARNING log → `InsuranceStatus=NotVerified`.

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Intake/
└── SubmitManualIntakeEndpoint.cs
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Infrastructure/Entities/InsuranceReference.cs` | Insurance reference lookup entity |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Services/InsurancePreCheckService.cs` | Soft insurance validation |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Entities/IntakeRecord.cs` | Add InsuranceStatus field |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs` | Register InsuranceReference |
| MODIFY | `src/ClinicalHealthcare.Api/Features/Intake/SubmitManualIntakeEndpoint.cs` | Call pre-check after intake creation |
| CREATE | `src/ClinicalHealthcare.Infrastructure.SqlMigrations/Migrations/*_InsurancePreCheck.cs` | Migration |

---

## External References

- [EF Core Data Seeding](https://learn.microsoft.com/en-us/ef/core/modeling/data-seeding)

---

## Build Commands

```bash
dotnet ef migrations add InsurancePreCheck --project src/ClinicalHealthcare.Infrastructure.SqlMigrations --startup-project src/ClinicalHealthcare.Api --context ApplicationDbContext
dotnet build
```

---

## Implementation Validation Strategy

- Intake with valid insurance → 201; `InsuranceStatus=Validated`.
- Intake with unknown insurance → 201; `InsuranceStatus=NotVerified`.
- Intake with empty insurance fields → 201; `InsuranceStatus=Skipped`.
- Pre-check exception → 201; `InsuranceStatus=NotVerified`; WARNING in logs.

---

## Implementation Checklist

- [x] **[AC-001]** Insurance pre-check is non-blocking; intake always returns 201
- [x] **[AC-002]** `InsuranceStatus` stored as `Validated`, `NotVerified`, or `Skipped`
- [x] **[AC-003]** Pre-check queries `InsuranceReference` table by InsurerId + PlanCode
- [x] **[AC-004]** Empty insurance fields → `InsuranceStatus=Skipped`
- [x] Pre-check exception caught; WARNING logged; `NotVerified` set; 201 returned
- [x] `InsuranceReference` entity registered; migration created
- [x] `InsurancePreCheckService` registered in DI
- [x] `dotnet build` passes with 0 errors
