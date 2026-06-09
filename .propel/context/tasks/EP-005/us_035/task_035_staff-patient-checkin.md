# Task - TASK_035

## Requirement Reference

- **User Story**: US_035 — Staff patient arrival check-in
- **Story Location**: `.propel/context/tasks/EP-005/us_035/us_035.md`
- **Parent Epic**: EP-005

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `PATCH /appointments/{id}/checkin` transitions `Appointment.Status` from `Scheduled` to `Arrived` |
| AC-002 | FSM enforced: only `Scheduled→Arrived` is a valid check-in transition |
| AC-003 | `QueueEntry` for the patient is removed (or marked `CheckedIn`) on check-in |
| AC-004 | Rowversion prevents double check-in (concurrent check-in → 409) |
| AC-005 | AuditLog entry written on successful check-in |

### Edge Cases

- Appointment already `Arrived` or `Completed` → FSM interceptor throws; return 409
- Patient has no QueueEntry (booked online, not walk-in) → check-in still succeeds; no QueueEntry mutation

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
| Backend | EF Core | 8.x | FSM interceptor + rowversion |
| Database | SQL Server | 2022 / Express | Appointment + QueueEntry storage |

---

## Task Overview

Implement `PATCH /appointments/{id}/checkin`. Transition `Appointment.Status` to `Arrived` via FSM interceptor. Remove/update `QueueEntry`. Use rowversion concurrency token to prevent double check-in. Write AuditLog.

---

## Dependent Tasks

- **TASK_001 (us_007)** — `Appointment` entity + `AppointmentFsmInterceptor`
- **TASK_001 (us_033)** — `QueueEntry` entity

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Staff/CheckInPatientEndpoint.cs`

---

## Implementation Plan

1. Implement `PATCH /appointments/{id}/checkin` (`[Authorize(Roles="Staff,Admin")]`): load `Appointment` with rowversion; check `Status=Scheduled` → if not, return 409; set `Status=Arrived` (FSM interceptor validates in `SavingChanges`); find `QueueEntry` for patient with today's date + `Status=Waiting` → set `Status=CheckedIn`; write AuditLog (`Action=CheckIn`, `EntityType=Appointment`); `SaveChangesAsync` → catch `DbUpdateConcurrencyException` → 409; return 200.
2. Ensure `AppointmentFsmInterceptor` (us_007) allows `Scheduled→Arrived` transition.

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Staff/
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/Staff/CheckInPatientEndpoint.cs` | PATCH /appointments/{id}/checkin |

---

## External References

- [EF Core Concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)

---

## Build Commands

```bash
dotnet build
```

---

## Implementation Validation Strategy

- `PATCH /checkin` on `Scheduled` appointment → 200; `Status=Arrived`; QueueEntry = `CheckedIn`.
- `PATCH /checkin` on `Arrived` appointment → 409 (FSM violation).
- Concurrent `PATCH /checkin` → second returns 409 (rowversion).
- AuditLog entry present after check-in.
- No QueueEntry for patient → check-in succeeds.

---

## Implementation Checklist

- [x] **[AC-001]** `PATCH /checkin` transitions `Status` from `Scheduled` to `Arrived`
- [x] **[AC-002]** FSM interceptor enforces `Scheduled→Arrived` as only valid transition
- [x] **[AC-003]** `QueueEntry` set to `CheckedIn` on successful check-in
- [x] **[AC-004]** Rowversion concurrency: concurrent check-in → 409
- [x] **[AC-005]** AuditLog entry written with `Action=CheckIn`
- [x] No QueueEntry case handled gracefully
- [x] Endpoint requires `[Authorize(Roles="Staff,Admin")]`
- [x] `dotnet build` passes with 0 errors
