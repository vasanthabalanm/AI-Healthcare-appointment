# Task - TASK_031

## Requirement Reference

- **User Story**: US_031 — Intake data editing with version history
- **Story Location**: `.propel/context/tasks/EP-004/us_031/us_031.md`
- **Parent Epic**: EP-004

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `PATCH /intake/{intakeGroupId}` creates a new `IntakeRecord` version; prior version immutable |
| AC-002 | `GET /intake/{intakeGroupId}` returns the latest version by default |
| AC-003 | `GET /intake/{intakeGroupId}?version=N` returns historical version N |
| AC-004 | No-op PATCH (same values as current) returns 200 without creating a new version |
| AC-005 | Prior versions are read-only; no `PATCH` on old versions |

### Edge Cases

- PATCH with a subset of fields → only changed fields update; unchanged fields copied from prior version
- Version number gap not possible (versions are sequential integers: 1, 2, 3…)

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
| Backend | EF Core | 8.x | IntakeRecord versioning |
| Database | SQL Server | 2022 / Express | IntakeRecord storage |

---

## Task Overview

Implement `PATCH /intake/{intakeGroupId}` (creates new version if changes detected) and `GET /intake/{intakeGroupId}[?version=N]` (returns latest or specific version). Enforce immutability of prior versions.

---

## Dependent Tasks

- **TASK_001 (us_008)** — `IntakeRecord` entity with `IntakeGroupId`, `Version`, `IsLatest`
- **TASK_001 (us_030)** — Manual intake creates the initial version

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Intake/UpdateIntakeEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Intake/GetIntakeEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Intake/UpdateIntakeRequest.cs`

---

## Implementation Plan

1. Implement `GET /intake/{intakeGroupId}` (`[Authorize(Roles="Patient,Staff,Admin")]`): query `IntakeRecord` by `IntakeGroupId`; if `?version=N` → `IgnoreQueryFilters().Where(r => r.IntakeGroupId == id && r.Version == N)`; else use default filter (IsLatest); 404 if not found.
2. Implement `PATCH /intake/{intakeGroupId}` (`[Authorize(Roles="Patient")]`): load current `IsLatest` version; compare incoming values field by field; if no changes → return 200 (no-op); else set current `IsLatest=false`; create new `IntakeRecord` copying all fields from current, applying changes, `Version = current.Version + 1`, `IsLatest=true`, `IntakeGroupId` same; save both.
3. Validate patient owns the intake (PatientId from JWT matches).
4. Historical version query disables default query filter.

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Intake/
├── SubmitManualIntakeEndpoint.cs
└── ...
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/Intake/UpdateIntakeEndpoint.cs` | PATCH /intake/{intakeGroupId} |
| CREATE | `src/ClinicalHealthcare.Api/Features/Intake/GetIntakeEndpoint.cs` | GET /intake/{intakeGroupId}[?version=N] |
| CREATE | `src/ClinicalHealthcare.Api/Features/Intake/UpdateIntakeRequest.cs` | PATCH DTO |

---

## External References

- [EF Core Query Filters](https://learn.microsoft.com/en-us/ef/core/querying/filters)

---

## Build Commands

```bash
dotnet build
```

---

## Implementation Validation Strategy

- `PATCH` with changed values → new `IntakeRecord` version created; old `IsLatest=false`.
- `PATCH` with same values → 200; version count unchanged.
- `GET /intake/{id}` → returns latest version.
- `GET /intake/{id}?version=1` → returns version 1 (even if not latest).
- `dotnet build` → 0 errors.

---

## Implementation Checklist

- [x] **[AC-001]** `PATCH` creates new `IntakeRecord` version; sets old `IsLatest=false`
- [x] **[AC-002]** `GET` without version param returns latest via default query filter
- [x] **[AC-003]** `GET ?version=N` uses `IgnoreQueryFilters()` to retrieve historical version
- [x] **[AC-004]** No-op PATCH (same values) returns 200 without new version row
- [x] **[AC-005]** Prior versions read-only; no PATCH on old version IDs
- [x] Patient ownership validated (JWT PatientId matches IntakeRecord.PatientId)
- [x] Version increment is sequential (`current.Version + 1`)
- [x] `dotnet build` passes with 0 errors
