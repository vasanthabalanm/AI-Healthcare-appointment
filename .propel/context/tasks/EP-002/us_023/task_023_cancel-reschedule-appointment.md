# Task - TASK_023

## Requirement Reference

- **User Story**: US_023 — Patient cancel/reschedule appointment
- **Story Location**: `.propel/context/tasks/EP-002/us_023/us_023.md`
- **Parent Epic**: EP-002

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `DELETE /appointments/{id}` cancels appointment; slot released; returns 200 |
| AC-002 | Cancellation cutoff enforced; attempts after cutoff return 400 |
| AC-003 | Slot released on cancel triggers `SwapMonitorJob` enqueue |
| AC-004 | `PATCH /appointments/{id}/reschedule` with rowversion; concurrent reschedule → 409 |
| AC-005 | Cancelled pending Hangfire reminder jobs are cancelled on appointment cancellation |

### Edge Cases

- Attempt to cancel another patient's appointment → 403
- Cancel past appointment (status = Arrived/Completed/NoShow) → 400

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
| Backend | EF Core | 8.x | Rowversion concurrency |
| Backend | Hangfire | 1.8.x | Job cancellation + SwapMonitor enqueue |

---

## Task Overview

Implement `DELETE /appointments/{id}` (cancel with cutoff check, slot release, SwapMonitor trigger, reminder job cancellation) and `PATCH /appointments/{id}/reschedule` (rowversion optimistic lock).

---

## Dependent Tasks

- **TASK_001 (us_019)** — `Appointment` and `Slot` entities; booking flow
- **TASK_001 (us_021)** — `SwapMonitorJob` exists to be enqueued
- **TASK_001 (us_004)** — Hangfire job client

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Appointments/CancelAppointmentEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Appointments/RescheduleAppointmentEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Appointments/RescheduleRequest.cs`

---

## Implementation Plan

1. Implement `DELETE /appointments/{id}` (`[Authorize(Roles="Patient")]`): load Appointment; verify `PatientId == JWT sub` → 403; check `Status=Scheduled` → else 400; check cancellation cutoff (`SlotTime > UtcNow.AddHours(AppSettings.CancellationCutoffHours)`) → else 400; set `Status=Cancelled`; set `Slot.IsAvailable=true`; cancel Hangfire reminder jobs by `JobId` stored on Appointment; enqueue `SwapMonitorJob`; invalidate Redis slot cache; return 200.
2. Store Hangfire `ReminderJobId` on `Appointment` entity (add field + migration) so it can be cancelled.
3. Implement `PATCH /appointments/{id}/reschedule` (`[Authorize(Roles="Patient")]`): validate new `SlotId`; load both Appointment and new Slot; rowversion check on new Slot (`DbUpdateConcurrencyException` → 409); transaction: release old slot, book new slot, update Appointment; re-enqueue reminder jobs for new time.
4. Add `CancellationCutoffHours` to `AppSettings` (default: 24).

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Appointments/
├── GetSlotsEndpoint.cs
├── BookAppointmentEndpoint.cs
└── JoinWaitlistEndpoint.cs
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/Appointments/CancelAppointmentEndpoint.cs` | DELETE /appointments/{id} |
| CREATE | `src/ClinicalHealthcare.Api/Features/Appointments/RescheduleAppointmentEndpoint.cs` | PATCH /appointments/{id}/reschedule |
| CREATE | `src/ClinicalHealthcare.Api/Features/Appointments/RescheduleRequest.cs` | Reschedule DTO |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Entities/Appointment.cs` | Add ReminderJobId field |
| CREATE | `src/ClinicalHealthcare.Infrastructure.SqlMigrations/Migrations/*_AppointmentReminderJobId.cs` | Migration |

---

## External References

- [Hangfire Job Cancellation](https://docs.hangfire.io/en/latest/background-jobs/deleting-jobs.html)

---

## Build Commands

```bash
dotnet ef migrations add AppointmentReminderJobId --project src/ClinicalHealthcare.Infrastructure.SqlMigrations --startup-project src/ClinicalHealthcare.Api --context ApplicationDbContext
dotnet build
```

---

## Implementation Validation Strategy

- `DELETE /appointments/{id}` within cutoff → 200; Slot `IsAvailable=true`; `SwapMonitorJob` in Hangfire.
- `DELETE /appointments/{id}` after cutoff → 400.
- `DELETE` another patient's appointment → 403.
- `PATCH /reschedule` concurrent → 409.
- Hangfire reminder job for cancelled appointment is deleted.

---

## Implementation Checklist

- [ ] **[AC-001]** `DELETE /appointments/{id}` cancels and returns 200; slot released
- [ ] **[AC-002]** Cancellation cutoff enforced; after-cutoff cancel → 400
- [ ] **[AC-003]** `SwapMonitorJob` enqueued on successful cancellation
- [ ] **[AC-004]** `PATCH /reschedule` uses rowversion; concurrent → 409
- [ ] **[AC-005]** Pending Hangfire reminder jobs cancelled by stored `ReminderJobId`
- [ ] Patient can only cancel own appointment (403 guard)
- [ ] Redis slot cache invalidated on cancel and reschedule
- [ ] `dotnet build` passes with 0 errors
