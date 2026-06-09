# Task - TASK_030

## Requirement Reference

- **User Story**: US_030 — Manual intake form submission
- **Story Location**: `.propel/context/tasks/EP-004/us_030/us_030.md`
- **Parent Epic**: EP-004

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `POST /intake/manual` creates `IntakeRecord` with `Source=Manual`; returns 201 |
| AC-002 | Field-level validation errors return 422 with per-field error details |
| AC-003 | Duplicate intake (patient already has Active intake) → 409 |
| AC-004 | All inputs sanitised via EF Core parameterised queries (no raw SQL interpolation) |

### Edge Cases

- Empty optional fields → accepted (IntakeRecord created with nulls for optional fields)
- Concurrent duplicate submissions (same patient, same second) → 409 from application-level duplicate check

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
| Backend | EF Core | 8.x | Parameterised queries; IntakeRecord persistence |
| Database | SQL Server | 2022 / Express | IntakeRecord storage |

---

## Task Overview

Implement `POST /intake/manual`. Validate the request DTO (422 on field errors). Check for existing Active intake (409 on duplicate). Create `IntakeRecord` with `Source=Manual` and `IsLatest=true`. All DB operations use EF Core parameterised queries.

---

## Dependent Tasks

- **TASK_001 (us_008)** — `IntakeRecord` entity

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Intake/SubmitManualIntakeEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Intake/ManualIntakeRequest.cs`

---

## Implementation Plan

1. Create `ManualIntakeRequest` DTO with validation attributes: required fields annotated `[Required]`; string fields with `[MaxLength]`; date fields with `[DataType(DataType.Date)]`.
2. Implement `POST /intake/manual` (`[Authorize(Roles="Patient")]`): validate model state → 422 with `ValidationProblemDetails` if invalid; check for existing `IntakeRecord` where `PatientId=JWT.sub` and `IsLatest=true` (409 if found); create `IntakeRecord` with `Source=Manual`, `IsLatest=true`, `Version=1`, `IntakeGroupId=Guid.NewGuid()`; all field values set via EF Core entity properties (no `FromSqlRaw` interpolation); return 201.
3. Ensure no raw SQL string interpolation anywhere in the handler.

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Intake/
├── StartAiIntakeEndpoint.cs
└── ...
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/Intake/SubmitManualIntakeEndpoint.cs` | POST /intake/manual |
| CREATE | `src/ClinicalHealthcare.Api/Features/Intake/ManualIntakeRequest.cs` | Manual intake DTO with validation |

---

## External References

- [EF Core Parameterised Queries](https://learn.microsoft.com/en-us/ef/core/querying/raw-sql)
- [ASP.NET Core Model Validation](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/validation)

---

## Build Commands

```bash
dotnet build
```

---

## Implementation Validation Strategy

- `POST /intake/manual` with valid data → 201; `IntakeRecord` in DB with `Source=Manual`, `IsLatest=true`.
- Missing required field → 422 with field name in error.
- Second submission for same patient → 409.
- No `FromSqlRaw` with string interpolation in codebase (grep check).

---

## Implementation Checklist

- [x] **[AC-001]** `POST /intake/manual` creates `IntakeRecord` with `Source=Manual`; returns 201
- [x] **[AC-002]** Invalid fields → 422 with `ValidationProblemDetails` per-field errors
- [x] **[AC-003]** Existing `IsLatest=true` intake for patient → 409
- [x] **[AC-004]** All DB writes use EF Core entity properties (no raw SQL interpolation)
- [x] `IntakeRecord.IntakeGroupId` set to new GUID; `Version=1`; `IsLatest=true`
- [x] Patient ID sourced from JWT claims (not request body)
- [x] Endpoint requires `[Authorize(Roles="Patient")]`
- [x] `dotnet build` passes with 0 errors
