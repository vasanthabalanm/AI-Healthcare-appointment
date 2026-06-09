# Task - TASK_033

## Requirement Reference

- **User Story**: US_033 — Staff walk-in patient registration + queue
- **Story Location**: `.propel/context/tasks/EP-005/us_033/us_033.md`
- **Parent Epic**: EP-005

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `GET /staff/patients/search?q=&dob=` returns matching patients |
| AC-002 | Walk-in patient registration creates minimal profile with `WalkIn=true` |
| AC-003 | Queue capacity default 20; exceeding capacity returns 409 unless `Override=true` |
| AC-004 | `Override=true` bypasses capacity and writes AuditLog entry |
| AC-005 | Queue entry created for walk-in patient |

### Edge Cases

- Search by partial name + DOB → case-insensitive LIKE search using EF Core
- Walk-in registration for existing patient (same name + DOB found) → link to existing `UserAccount` rather than creating duplicate

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
| Backend | EF Core | 8.x | Patient search + queue management |
| Database | SQL Server | 2022 / Express | UserAccount + QueueEntry storage |

---

## Task Overview

Implement staff patient search (`GET /staff/patients/search`), walk-in registration (`POST /staff/patients/walkin`), and queue entry creation. Enforce queue capacity limit (configurable default 20) with override capability.

---

## Dependent Tasks

- **TASK_001 (us_007)** — `UserAccount` entity
- **TASK_001 (us_011)** — `AuditLog` entity

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Staff/SearchPatientsEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Staff/RegisterWalkInEndpoint.cs`
- `src/ClinicalHealthcare.Infrastructure/Entities/QueueEntry.cs`
- `src/ClinicalHealthcare.Infrastructure/Configuration/AppSettings.cs`

---

## Implementation Plan

1. Create `QueueEntry` entity: `Id`, `PatientId`, `QueueDate`, `Position`, `Status` (Waiting/CheckedIn/Removed), `IsWalkIn`, `AddedByStaffId`; register in `ApplicationDbContext`; create migration.
2. Implement `GET /staff/patients/search?q=name&dob=` (`[Authorize(Roles="Staff,Admin")]`): EF Core parameterised `EF.Functions.Like` search on `FirstName + LastName`; optional DOB filter; return `[{id, fullName, dob, email}]`.
3. Implement `POST /staff/patients/walkin` (`[Authorize(Roles="Staff,Admin")]`): check for existing patient (same name+DOB) to avoid duplicate; if not found → create `UserAccount` with `Role=Patient`, `WalkIn=true`, `IsActive=true`; count today's `QueueEntry` rows; if ≥ capacity and `Override!=true` → 409; if Override → write AuditLog; create `QueueEntry` with next `Position`; return 201.
4. Add `QueueCapacity = 20` to `AppSettings`.

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Staff/
└── README.md
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/Staff/SearchPatientsEndpoint.cs` | GET /staff/patients/search |
| CREATE | `src/ClinicalHealthcare.Api/Features/Staff/RegisterWalkInEndpoint.cs` | POST /staff/patients/walkin |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Entities/QueueEntry.cs` | Queue entry entity |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs` | Register QueueEntry |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Configuration/AppSettings.cs` | Add QueueCapacity |
| CREATE | `src/ClinicalHealthcare.Infrastructure.SqlMigrations/Migrations/*_QueueEntry.cs` | Migration |

---

## External References

- [EF Core LIKE Queries](https://learn.microsoft.com/en-us/ef/core/querying/client-eval)

---

## Build Commands

```bash
dotnet ef migrations add QueueEntry --project src/ClinicalHealthcare.Infrastructure.SqlMigrations --startup-project src/ClinicalHealthcare.Api --context ApplicationDbContext
dotnet build
```

---

## Implementation Validation Strategy

- `GET /staff/patients/search?q=smith` → matching patients returned.
- Walk-in under capacity → 201; QueueEntry created.
- Walk-in at capacity without Override → 409.
- Walk-in at capacity with `Override=true` → 201; AuditLog entry written.
- Existing patient found by name+DOB → linked, not duplicated.

---

## Implementation Checklist

- [x] **[AC-001]** `GET /staff/patients/search` returns matching patients (EF Core LIKE)
- [x] **[AC-002]** Walk-in creates minimal `UserAccount` with `WalkIn=true`
- [x] **[AC-003]** Queue capacity default 20; exceeding without override → 409
- [x] **[AC-004]** `Override=true` bypasses capacity + writes AuditLog
- [x] **[AC-005]** `QueueEntry` created for walk-in patient
- [x] Duplicate patient check (name+DOB) before new registration
- [x] Both endpoints require `[Authorize(Roles="Staff,Admin")]`
- [x] `dotnet build` passes with 0 errors
