# Task - TASK_019

## Requirement Reference

- **User Story**: US_019 — Patient slot browsing + booking + optimistic lock
- **Story Location**: `.propel/context/tasks/EP-002/us_019/us_019.md`
- **Parent Epic**: EP-002

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `GET /slots` returns available slots; sourced from Redis cache (TTL=60s) with DB fallback |
| AC-002 | `POST /appointments` books a slot; slot marked unavailable atomically |
| AC-003 | Concurrent booking detected via `rowversion`; second request returns 409 |
| AC-004 | Redis slot cache invalidated immediately after successful booking |
| AC-005 | Hangfire jobs enqueued: confirmation email + T-48h reminder on booking |

### Edge Cases

- `DbUpdateConcurrencyException` on booking → 409 `{"error":"Slot no longer available"}`
- Slot already booked → 409 before rowversion check
- Cache miss on `GET /slots` → DB query; cache populated for next request

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
| Infrastructure | Upstash Redis | N/A | Slot cache TTL=60s per design.md |
| Backend | EF Core | 8.x | Rowversion concurrency token per design.md |
| Backend | Hangfire | 1.8.x | Confirmation + reminder job enqueue |

---

## Task Overview

Implement `GET /slots` (Redis-cached, TTL=60s) and `POST /appointments` (atomic slot booking with `rowversion` optimistic lock). Invalidate slot cache on booking. Enqueue Hangfire jobs for confirmation email and T-48h reminder.

---

## Dependent Tasks

- **TASK_001 (us_004)** — Redis `ICacheService` + Hangfire
- **TASK_001 (us_007)** — `Slot` and `Appointment` entities

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Appointments/GetSlotsEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Appointments/BookAppointmentEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Appointments/BookAppointmentRequest.cs`

---

## Implementation Plan

1. Implement `GET /slots` (`[Authorize(Roles="Patient")]`): try `ICacheService.GetAsync<List<SlotDto>>("slots:available")`; on miss, query DB; populate cache with TTL=60s; return slots.
2. Implement `POST /appointments` (`[Authorize(Roles="Patient")]`): validate `SlotId`; load `Slot` with rowversion; check `IsAvailable`; wrap in transaction: mark `Slot.IsAvailable=false`, insert `Appointment`; call `SaveChanges` — catch `DbUpdateConcurrencyException` → 409; on success, invalidate `slots:available` cache key.
3. After successful booking, enqueue Hangfire jobs: `IBackgroundJobClient.Enqueue<SendConfirmationEmailJob>(...)` and `IBackgroundJobClient.Schedule<SendReminderJob>(..., DateTimeOffset.UtcNow.AddHours(appointmentTime - 48h))`.
4. Create Hangfire job stub classes: `SendConfirmationEmailJob`, `SendReminderJob` (full implementation in us_026/us_027).

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Appointments/
└── README.md
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/Appointments/GetSlotsEndpoint.cs` | GET /slots with Redis cache |
| CREATE | `src/ClinicalHealthcare.Api/Features/Appointments/BookAppointmentEndpoint.cs` | POST /appointments with rowversion lock |
| CREATE | `src/ClinicalHealthcare.Api/Features/Appointments/BookAppointmentRequest.cs` | Booking DTO |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Jobs/SendConfirmationEmailJob.cs` | Hangfire job stub |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Jobs/SendReminderJob.cs` | Hangfire job stub |

---

## External References

- [EF Core Concurrency Conflicts](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
- [Hangfire Scheduled Jobs](https://docs.hangfire.io/en/latest/background-jobs/scheduling-jobs.html)

---

## Build Commands

```bash
dotnet build
```

---

## Implementation Validation Strategy

- `GET /slots` with cold cache → DB query; cache populated.
- `GET /slots` with warm cache → Redis hit; no DB query.
- Concurrent `POST /appointments` for same slot → one succeeds (200/201), second returns 409.
- After booking, `GET /slots` cache miss (invalidated).
- Hangfire dashboard shows enqueued confirmation + scheduled reminder jobs.

---

## Implementation Checklist

- [x] **[AC-001]** `GET /slots` sources from Redis cache (TTL=60s) with DB fallback on miss
- [x] **[AC-002]** `POST /appointments` books slot atomically; marks `Slot.IsAvailable=false`
- [x] **[AC-003]** `DbUpdateConcurrencyException` caught; returns 409 `"Slot no longer available"`
- [x] **[AC-004]** `slots:date:{date}` Redis cache key invalidated after successful booking
- [x] **[AC-005]** `SendConfirmationEmailJob` and `SendReminderJob` enqueued via Hangfire
- [x] `GET /slots` requires Patient role; `POST /appointments` requires Patient role
- [x] Slot rowversion loaded and checked in `SaveChanges`
- [x] `dotnet build` passes with 0 errors
