# Task - TASK_037

## Requirement Reference

- **User Story**: US_037 — No-show risk alerts + outreach recording
- **Story Location**: `.propel/context/tasks/EP-005/us_037/us_037.md`
- **Parent Epic**: EP-005

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `GET /schedule/high-risk?date=` returns appointments where `IsHighRisk=true` |
| AC-002 | `POST /appointments/{id}/outreach` records outreach attempt with notes |
| AC-003 | `PATCH /appointments/{id}/status {status:"NoShow"}` transitions to NoShow; releases slot; writes AuditLog |
| AC-004 | Slot released on NoShow triggers `SwapMonitorJob` |

### Edge Cases

- Marking NoShow on already-Arrived appointment → FSM interceptor blocks; 409
- Marking NoShow on Cancelled appointment → FSM interceptor blocks; 409

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
| Backend | EF Core | 8.x | Appointment FSM |
| Backend | Hangfire | 1.8.x | SwapMonitorJob on NoShow |

---

## Task Overview

Implement high-risk appointment listing, outreach recording, and NoShow status transition. NoShow releases slot and triggers `SwapMonitorJob`.

---

## Dependent Tasks

- **TASK_001 (us_022)** — `Appointment.IsHighRisk`
- **TASK_001 (us_021)** — `SwapMonitorJob`
- **TASK_001 (us_007)** — `AppointmentFsmInterceptor`

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Staff/GetHighRiskAppointmentsEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Staff/RecordOutreachEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Staff/UpdateAppointmentStatusEndpoint.cs`
- `src/ClinicalHealthcare.Infrastructure/Entities/OutreachRecord.cs`

---

## Implementation Plan

1. Implement `GET /schedule/high-risk?date=` (`[Authorize(Roles="Staff,Admin")]`): query `Appointments` where `IsHighRisk=true` and `Slot.SlotTime.Date = date`; return `[{appointmentId, patientId, patientName, slotTime, riskScore}]`.
2. Create `OutreachRecord` entity: `Id`, `AppointmentId`, `StaffId`, `Notes`, `AttemptedAt`; register + migration.
3. Implement `POST /appointments/{id}/outreach` (`[Authorize(Roles="Staff,Admin")]`): insert `OutreachRecord`; return 201.
4. Implement `PATCH /appointments/{id}/status` (`[Authorize(Roles="Staff,Admin")]`): accept `{status:"NoShow"}`; load Appointment; set `Status=NoShow` (FSM allows `Scheduled→NoShow`); release slot (`IsAvailable=true`); write AuditLog; enqueue `SwapMonitorJob`; invalidate Redis slot cache; return 200.

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Staff/
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/Staff/GetHighRiskAppointmentsEndpoint.cs` | GET /schedule/high-risk |
| CREATE | `src/ClinicalHealthcare.Api/Features/Staff/RecordOutreachEndpoint.cs` | POST /appointments/{id}/outreach |
| CREATE | `src/ClinicalHealthcare.Api/Features/Staff/UpdateAppointmentStatusEndpoint.cs` | PATCH /appointments/{id}/status |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Entities/OutreachRecord.cs` | Outreach attempt record |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs` | Register OutreachRecord |
| CREATE | `src/ClinicalHealthcare.Infrastructure.SqlMigrations/Migrations/*_OutreachRecord.cs` | Migration |

---

## External References

- [Hangfire Background Jobs](https://docs.hangfire.io/en/latest/)

---

## Build Commands

```bash
dotnet ef migrations add OutreachRecord --project src/ClinicalHealthcare.Infrastructure.SqlMigrations --startup-project src/ClinicalHealthcare.Api --context ApplicationDbContext
dotnet build
```

---

## Implementation Validation Strategy

- `GET /schedule/high-risk` → only `IsHighRisk=true` appointments returned.
- `POST /outreach` → `OutreachRecord` in DB.
- `PATCH /status {status:"NoShow"}` → `Status=NoShow`; Slot `IsAvailable=true`; `SwapMonitorJob` in Hangfire.
- NoShow on Arrived appointment → FSM blocks → 409.

---

## Implementation Checklist

- [x] **[AC-001]** `GET /schedule/high-risk?date=` returns `IsHighRisk=true` appointments
- [x] **[AC-002]** `POST /outreach` creates `OutreachRecord` with notes
- [x] **[AC-003]** `PATCH /status {NoShow}` transitions FSM; writes AuditLog
- [x] **[AC-004]** Slot released and `SwapMonitorJob` enqueued on NoShow
- [x] FSM blocks invalid transitions (Arrived→NoShow, Cancelled→NoShow)
- [x] `OutreachRecord` entity created + migration
- [x] All endpoints require `[Authorize(Roles="Staff,Admin")]`
- [x] `dotnet build` passes with 0 errors
