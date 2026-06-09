# Task - TASK_021

## Requirement Reference

- **User Story**: US_021 — Automated slot swap via Hangfire SwapMonitor
- **Story Location**: `.propel/context/tasks/EP-002/us_021/us_021.md`
- **Parent Epic**: EP-002

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | When a slot is released (appointment cancelled), `SwapMonitorJob` is enqueued |
| AC-002 | `SwapMonitorJob` finds the oldest Active `WaitlistEntry` for the released slot time and sends offer email/SMS |
| AC-003 | Offer window is 2 hours (`AppSettings.SwapOfferWindowHours`); after window, slot is released back to general availability |
| AC-004 | Patient accepts offer: EF Core transaction swaps slot to the new patient |
| AC-005 | `ExpireSwapOfferJob` runs on schedule to expire outstanding offers after the window |

### Edge Cases

- No waitlist entries for the released slot → slot returned to general availability immediately
- Two patients accept the same swap offer (race condition) → rowversion optimistic lock on Slot prevents double booking; second patient receives 409

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
| Backend | Hangfire | 1.8.x | SwapMonitorJob + ExpireSwapOfferJob per design.md |
| Backend | EF Core | 8.x | Atomic slot swap transaction |
| Infrastructure | Upstash Redis | N/A | Slot cache invalidation after swap |
| Backend | MailKit | 4.x | Offer email notification |

---

## Task Overview

Implement `SwapMonitorJob` (triggered on slot release) and `ExpireSwapOfferJob` (scheduled). The monitor finds the oldest Active waitlist entry for the released slot, sends an offer notification, and sets a 2-hour acceptance window. Accept endpoint performs an EF Core transaction swap. Expiry job cleans up unaccepted offers.

---

## Dependent Tasks

- **TASK_001 (us_004)** — Hangfire infrastructure
- **TASK_001 (us_008)** — `WaitlistEntry` entity
- **TASK_001 (us_019)** — `Appointment` cancel logic triggers `SwapMonitorJob`

---

## Impacted Components

- `src/ClinicalHealthcare.Infrastructure/Jobs/SwapMonitorJob.cs`
- `src/ClinicalHealthcare.Infrastructure/Jobs/ExpireSwapOfferJob.cs`
- `src/ClinicalHealthcare.Api/Features/Appointments/AcceptSwapOfferEndpoint.cs`
- `src/ClinicalHealthcare.Api/Program.cs` — `RecurringJob` registration for expiry

---

## Implementation Plan

1. Create `SwapMonitorJob`: query oldest `Active` WaitlistEntry for the released slot date/time; if none → return; send offer email (MailKit) + SMS (ISmsGateway stub); set `WaitlistEntry.Status=OfferSent`, `OfferExpiresAt=UtcNow.AddHours(AppSettings.SwapOfferWindowHours)`.
2. Create `AcceptSwapOfferEndpoint`: `POST /waitlist/{id}/accept`; load WaitlistEntry; check `Status=OfferSent` and `OfferExpiresAt > UtcNow`; EF Core transaction: create new Appointment for patient + mark slot as unavailable using rowversion; set WaitlistEntry `Status=Fulfilled`.
3. Create `ExpireSwapOfferJob`: find all WaitlistEntries where `Status=OfferSent` and `OfferExpiresAt <= UtcNow`; set `Status=Expired`; re-release slot (`IsAvailable=true`); invalidate Redis slot cache.
4. Register `ExpireSwapOfferJob` as a recurring job in `Program.cs`: `RecurringJob.AddOrUpdate<ExpireSwapOfferJob>("expire-swap-offers", j => j.Execute(), Cron.Minutely)`.
5. In `CancelAppointmentEndpoint` (us_023), after slot release → enqueue `SwapMonitorJob`.
6. Add `AppSettings.SwapOfferWindowHours` configuration (default: 2).

---

## Current Project State

```
src/ClinicalHealthcare.Infrastructure/Jobs/
├── SendConfirmationEmailJob.cs
└── SendReminderJob.cs
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Infrastructure/Jobs/SwapMonitorJob.cs` | Triggered on slot release; finds oldest waitlist entry |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Jobs/ExpireSwapOfferJob.cs` | Scheduled: expires outstanding swap offers |
| CREATE | `src/ClinicalHealthcare.Api/Features/Appointments/AcceptSwapOfferEndpoint.cs` | POST /waitlist/{id}/accept |
| MODIFY | `src/ClinicalHealthcare.Api/Program.cs` | Register `ExpireSwapOfferJob` recurring job |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Entities/WaitlistEntry.cs` | Add `OfferExpiresAt` field and `OfferSent` status |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Configuration/AppSettings.cs` | `SwapOfferWindowHours` config |

---

## External References

- [Hangfire Recurring Jobs](https://docs.hangfire.io/en/latest/background-jobs/recurring-jobs.html)
- [EF Core Transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions)

---

## Build Commands

```bash
dotnet build
```

---

## Implementation Validation Strategy

- Cancel appointment → `SwapMonitorJob` enqueued in Hangfire.
- `SwapMonitorJob` runs → oldest WaitlistEntry status = `OfferSent`; offer email sent.
- `POST /waitlist/{id}/accept` → Appointment created; WaitlistEntry = `Fulfilled`; Slot = unavailable.
- Two concurrent accepts → second returns 409 (rowversion).
- `ExpireSwapOfferJob` fires → expired offers → `Status=Expired`; Slot re-released.

---

## Implementation Checklist

- [x] **[AC-001]** `SwapMonitorJob` enqueued when slot released (cancel flow)
- [x] **[AC-002]** `SwapMonitorJob` finds oldest Active WaitlistEntry; sends offer notification
- [x] **[AC-003]** Offer window = `AppSettings.SwapOfferWindowHours` (default 2h)
- [x] **[AC-004]** `AcceptSwapOfferEndpoint` performs atomic EF Core transaction swap
- [x] **[AC-005]** `ExpireSwapOfferJob` recurring job registered; cleans up unaccepted offers
- [x] Rowversion optimistic lock prevents double-accept race condition
- [x] Redis slot cache invalidated after swap completes
- [x] `dotnet build` passes with 0 errors
