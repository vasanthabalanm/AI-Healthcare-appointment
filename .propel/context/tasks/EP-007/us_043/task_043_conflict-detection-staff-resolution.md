# Task - TASK_043

## Requirement Reference

- **User Story**: US_043 — Conflict detection + Staff resolution
- **Story Location**: `.propel/context/tasks/EP-007/us_043/us_043.md`
- **Parent Epic**: EP-007

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `GET /patients/{id}/conflicts` returns all `ConflictFlag` rows (Unresolved/Resolved/Dismissed) |
| AC-002 | `PATCH /conflicts/{id}/resolve` sets `Status=Resolved` with `ResolvedByStaffId` |
| AC-003 | `PATCH /conflicts/{id}/dismiss` sets `Status=Dismissed` |
| AC-004 | Verification (`POST /patients/{id}/verify`) blocked if any `ConflictFlag` is Unresolved |

### Edge Cases

- Resolve non-existent conflict → 404
- Dismiss already-Resolved conflict → 409

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
| Database | PostgreSQL | 16.x | `ConflictFlag` in `ClinicalDbContext` |
| Backend | EF Core | 8.x | Npgsql provider |

---

## Task Overview

Implement `GET /patients/{id}/conflicts`, `PATCH /conflicts/{id}/resolve`, `PATCH /conflicts/{id}/dismiss`. Block `POST /patients/{id}/verify` if any unresolved conflict exists for the patient.

---

## Dependent Tasks

- **TASK_001 (us_010)** — `ConflictFlag` entity in PostgreSQL
- **TASK_001 (us_044)** — `POST /patients/{id}/verify` endpoint uses the unresolved check

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Patients/GetConflictsEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Patients/ResolveConflictEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Patients/DismissConflictEndpoint.cs`

---

## Implementation Plan

1. Implement `GET /patients/{id}/conflicts` (`[Authorize(Roles="Staff,Admin")]`): query `ConflictFlag` by `PatientId` from `ClinicalDbContext`; return all (any status); 200.
2. Implement `PATCH /conflicts/{id}/resolve` (`[Authorize(Roles="Staff,Admin")]`): load `ConflictFlag` → 404 if not found; check `Status!=Resolved` and `Status!=Dismissed` → 409 if already terminal; set `Status=Resolved`, `ResolvedByStaffId=JWT.sub`, `ResolvedAt=UtcNow`; save; return 200.
3. Implement `PATCH /conflicts/{id}/dismiss` (`[Authorize(Roles="Staff,Admin")]`): load flag → 404; check `Status=Unresolved` → else 409; set `Status=Dismissed`; save; return 200.
4. Create `IConflictService.HasUnresolvedConflicts(patientId) → bool` helper used by verify endpoint (us_044).

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Patients/
└── README.md
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/Patients/GetConflictsEndpoint.cs` | GET /patients/{id}/conflicts |
| CREATE | `src/ClinicalHealthcare.Api/Features/Patients/ResolveConflictEndpoint.cs` | PATCH /conflicts/{id}/resolve |
| CREATE | `src/ClinicalHealthcare.Api/Features/Patients/DismissConflictEndpoint.cs` | PATCH /conflicts/{id}/dismiss |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Services/ConflictService.cs` | HasUnresolvedConflicts helper |

---

## External References

- [Npgsql EF Core](https://www.npgsql.org/efcore/)

---

## Build Commands

```bash
dotnet build
```

---

## Implementation Validation Strategy

- `GET /patients/{id}/conflicts` → returns all ConflictFlag rows including Resolved/Dismissed.
- `PATCH /resolve` → `Status=Resolved`; `ResolvedByStaffId` populated.
- `PATCH /dismiss` → `Status=Dismissed`.
- `PATCH /resolve` on already-resolved → 409.
- `POST /patients/{id}/verify` with unresolved conflict → 409 (tested in us_044).

---

## Implementation Checklist

- [x] **[AC-001]** `GET /patients/{id}/conflicts` returns all conflict flags for patient
- [x] **[AC-002]** `PATCH /conflicts/{id}/resolve` sets Resolved + ResolvedByStaffId
- [x] **[AC-003]** `PATCH /conflicts/{id}/dismiss` sets Dismissed
- [x] **[AC-004]** `HasUnresolvedConflicts` helper available for verify endpoint guard
- [x] Resolve/dismiss on non-existent conflict → 404
- [x] Re-resolving or dismissing terminal-state conflict → 409
- [x] All endpoints require `[Authorize(Roles="Staff,Admin")]`
- [x] `dotnet build` passes with 0 errors
