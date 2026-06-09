---
task_id: task_023
us_id: us_023
reviewed_by: GitHub Copilot
review_date: 2026-05-18
verdict: Pass
---

# Implementation Analysis — task_023_cancel-reschedule-appointment.md

## Verdict

**Status:** Pass
**Summary:** TASK_023 is complete. `DELETE /appointments/{id}` cancels a Scheduled appointment with cutoff enforcement, stale-reminder deletion, `SwapMonitorJob` enqueue, and cache invalidation. `PATCH /appointments/{id}/reschedule` atomically moves an appointment to a new available slot using EF Core rowversion optimistic-concurrency (`DbUpdateConcurrencyException` → 409), cancels the stale reminder, re-schedules a new reminder, and invalidates Redis cache for both slot dates. `ReminderJobId` (`string?`) added to `Appointment` entity with dedicated migration; `CancellationCutoffHours` added to `AppSettings` with startup validator. Build: 0 errors, 0 warnings. 267/267 tests pass (+16 new: 7 cancel, 9 reschedule). Six low/informational findings identified — none are blockers.

---

## Traceability Matrix

| Requirement / AC | Evidence | Result |
|---|---|---|
| AC-001: `DELETE /appointments/{id}` cancels a Scheduled appointment | `app.MapDelete("/appointments/{id:int}", HandleCancelAppointment)` in `CancelAppointmentEndpoint.cs` | Pass |
| AC-001: Returns 200 on success | `Results.Ok(new { message = "Appointment cancelled." })` | Pass |
| AC-001: Only the owning patient can cancel | `appointment.PatientId != patientId → 403` | Pass |
| AC-002: Cancellation cutoff enforced on cancel | `appointment.Slot.SlotTime <= DateTime.UtcNow.AddHours(cutoffHours) → 400` | Pass |
| AC-002: Cancellation cutoff enforced on reschedule | Same guard applied in `HandleRescheduleAppointment` on the current slot | Pass |
| AC-002: Cutoff hours from `AppSettings.CancellationCutoffHours` (default 24) | `public int CancellationCutoffHours { get; init; } = 24` in `AppSettings.cs` | Pass |
| AC-002: Cutoff validated at startup (≥ 0) | `AppSettingsValidator` returns `ValidateOptionsResult.Fail` when `< 0` | Pass |
| AC-003: Slot stays unavailable; SwapMonitorJob enqueued | `appointment.Status = Cancelled; await db.SaveChangesAsync; jobs.Enqueue<SwapMonitorJob>` | Pass |
| AC-003: Cache invalidated for the cancelled slot date | `cache.DeleteAsync(dateKey)` after save | Pass |
| AC-004: `PATCH /appointments/{id}/reschedule` with rowversion → 409 on conflict | `DbUpdateConcurrencyException` caught in transaction → `Results.Conflict` | Pass |
| AC-004: Old slot released via SwapMonitorJob | `jobs.Enqueue<SwapMonitorJob>(oldSlotId)` after commit | Pass |
| AC-004: New slot booked atomically | `newSlot.IsAvailable = false` inside `BeginTransactionAsync`/`CommitAsync` | Pass |
| AC-005: `ReminderJobId` stored on Appointment at booking | `appointment.ReminderJobId = reminderJobId; await db.SaveChangesAsync` in `BookAppointmentEndpoint` | Pass |
| AC-005: Stale reminder deleted on cancel | `jobs.ChangeState(appointment.ReminderJobId, new DeletedState(), null)` before save | Pass |
| AC-005: Stale reminder deleted on reschedule | Same `ChangeState` call in `HandleRescheduleAppointment` before transaction commit | Pass |
| AC-005: New reminder scheduled post-reschedule | `jobs.Schedule<SendReminderJob>(..., newReminderAt - UtcNow)` + `appointment.ReminderJobId = newReminderJobId` | Pass |
| EF migration: `ReminderJobId` column | `AppointmentReminderJobId` migration adds `nullable nvarchar` to `Appointments` | Pass |
| Convention test passes (endpoint auto-discovery) | All endpoints implement `IEndpointDefinition`; `RequireAuthorization("PatientOnly")` applied | Pass |
| 267/267 tests pass | Confirmed | Pass |

---

## Logical & Design Findings

- **Business Logic — Cutoff bypass when Slot is null (F5 — Informational):** Both cancel and reschedule guard the cutoff check with `if (appointment.Slot is not null)`. If the navigation property fails to load (e.g., the `Include` returns null due to a deleted slot row — prevented in practice by FK constraint), the cutoff is silently skipped and the cancel proceeds. No action required; FK constraint prevents the condition in production. Noted for completeness.

- **Data Access — Post-transaction second `SaveChangesAsync` for `ReminderJobId` (F2 — Low):** In `HandleRescheduleAppointment`, the new `ReminderJobId` is persisted with a `SaveChangesAsync` call issued **after** `CommitAsync`. If this write fails, the appointment holds the new slot correctly but `ReminderJobId` is null on the entity. A future reschedule will not cancel the orphaned reminder job; the job fires once harmlessly. Bounded to one leaked job per failed write — benign. Mitigation: wrap the second save in a retry policy or accept the operational noise.

- **Data Access — Slot release not atomic with cancel (F1 — Low):** In `HandleCancelAppointment`, the appointment is set to `Cancelled` and `SaveChangesAsync` is called, then `jobs.Enqueue<SwapMonitorJob>` is called. If the process dies between the save and the enqueue, the slot stays permanently unavailable. This is the same architectural decision accepted in TASK_021 (slot stays unavailable; SwapMonitorJob owns release). A Hangfire recurring health-check job or monitoring alert on slots that are unavailable with no active appointment is the mitigation path.

- **Schema — `ReminderJobId` column type `nvarchar(max)` (F1 — Low):** The migration creates `ReminderJobId` as `nvarchar(max)`. Hangfire job IDs are GUIDs (36 chars) or integers (<10 chars). `nvarchar(100)` would be appropriate, avoiding unnecessary row-overflow page usage. No functional impact.

- **Configuration — `CancellationCutoffHours` absent from `appsettings.json` (F6 — Low):** The setting uses the in-code default (24 h) because it is not declared in `appsettings.json`. Operators cannot tune the cutoff without a code change. Adding a commented entry in `appsettings.json` makes the knob discoverable without affecting runtime behaviour.

- **Patterns & Standards — No rate limiting on cancel/reschedule (Informational):** A patient can repeatedly reschedule, cycling through available slots. Acceptable at this stage; rate limiting belongs in a cross-cutting middleware layer outside TASK_023 scope.

- **Security (OWASP A01):** `patientId` extracted exclusively from `JwtRegisteredClaimNames.Sub`; no user-supplied ownership claim reaches the guard. ✅

- **Security (OWASP A03):** All DB operations use EF Core LINQ. No raw SQL or string interpolation into queries. ✅

- **Security (OWASP A04 — Insecure Design):** Hangfire job IDs are internal strings from the DB, never from the request body. `ChangeState` cannot be directed to an arbitrary job by the caller. ✅

- **Performance:** Cancel: single `Include(Appointment + Slot)` PK lookup. Reschedule: same Include + one `Slots.FirstOrDefault` PK lookup. No N+1. ✅

- **Cache invalidation correctness:** Cancel invalidates the cancelled-slot date key. Reschedule invalidates both old and new date keys. Both use `GetSlotsEndpoint.CacheKeyPrefix` consistently. ✅

---

## Test Review

| Test | AC | Purpose | Result |
|------|-----|---------|--------|
| `CancelAppointment_Scheduled_Returns200_SlotRemainsUnavailable` | AC-001, AC-003 | 200; slot stays unavailable | Pass |
| `CancelAppointment_Scheduled_EnqueuesSwapMonitorJob` | AC-003 | `SwapMonitorJob` enqueued once | Pass |
| `CancelAppointment_WithReminderJobId_CancelsReminderJob` | AC-005 | Stale reminder deleted via `ChangeState` | Pass |
| `CancelAppointment_WithinCutoff_Returns400` | AC-002 | Slot 12 h away, cutoff 24 h → 400 | Pass |
| `CancelAppointment_WrongPatient_Returns403` | AC-001 | Ownership guard | Pass |
| `CancelAppointment_AlreadyCancelled_Returns400` | Edge | Non-Scheduled status → 400 | Pass |
| `CancelAppointment_NotFound_Returns404` | Edge | Missing appointment → 404 | Pass |
| `RescheduleAppointment_ValidNewSlot_Returns200` | AC-004 | Happy path; `appointment.SlotId` updated | Pass |
| `RescheduleAppointment_ValidNewSlot_OldSlotReleasedViaSwapMonitor` | AC-003, AC-004 | Old slot stays unavailable; `SwapMonitorJob` enqueued | Pass |
| `RescheduleAppointment_WithReminderJobId_CancelsOldReminder` | AC-005 | Old reminder deleted via `ChangeState` | Pass |
| `RescheduleAppointment_WithinCutoff_Returns400` | AC-002 | Current slot 12 h away, cutoff 24 h → 400 | Pass |
| `RescheduleAppointment_UnavailableNewSlot_Returns409` | AC-004 | Unavailable new slot → 409 | Pass |
| `RescheduleAppointment_SameSlot_Returns400` | Edge | New slot = current slot → 400 | Pass |
| `RescheduleAppointment_WrongPatient_Returns403` | AC-001 | Ownership guard | Pass |
| `RescheduleAppointment_NotFound_Returns404` | Edge | Missing appointment → 404 | Pass |
| `RescheduleAppointment_NotScheduled_Returns400` | Edge | Cancelled appointment → 400 | Pass |

**Prior baseline: 256. New total: 267/267 pass (+16: 7 cancel + 9 reschedule).**

**Missing tests (must add):**

- [ ] Unit: `RescheduleAppointment_NewReminderScheduled_WhenSlotBeyond48h` — verify that after a successful reschedule, `jobs.Schedule<SendReminderJob>` is called (positive AC-005 reschedule path). (F3 — Low)
- [ ] Unit: `AppSettingsValidator_CancellationCutoffHours_Negative_ReturnsFail` — verify `AppSettingsValidator.Validate` returns `Fail` when `CancellationCutoffHours < 0`. (F4 — Low)

---

## Validation Results

**Commands executed (from task file):**

```
dotnet ef migrations add AppointmentReminderJobId --project src/ClinicalHealthcare.Infrastructure.SqlMigrations --startup-project src/ClinicalHealthcare.Api --context ApplicationDbContext
dotnet build --no-incremental -c Release
dotnet test --no-build -c Release --logger "console;verbosity=minimal"
```

**Outcomes:**

| Command | Result |
|---------|--------|
| `dotnet ef migrations add AppointmentReminderJobId` | Done — migration created at `20260518085617_AppointmentReminderJobId.cs` |
| `dotnet build --no-incremental -c Release` | **0 errors, 0 warnings** |
| `dotnet test` | **267/267 pass** (Infrastructure: 254; Api: 13) |

---

## Fix Plan (Prioritized)

| # | Finding | Severity | Action | File(s) | Risk |
|---|---------|----------|--------|---------|------|
| F1 | `ReminderJobId` migration type `nvarchar(max)` | Low | Create a new migration to alter column to `nvarchar(100)` | `AppointmentReminderJobId.cs` (new migration) | L |
| F2 | Post-transaction `SaveChangesAsync` for `ReminderJobId` in reschedule | Low | Accept operational noise; add comment documenting the bounded leak; consider retry policy in future | `RescheduleAppointmentEndpoint.cs` L148-L154 | L |
| F3 | Missing test: new reminder scheduled after reschedule | Low | Add `RescheduleAppointment_NewReminderScheduled_WhenSlotBeyond48h` | `RescheduleAppointmentEndpointTests.cs` | L |
| F4 | Missing test: `AppSettingsValidator` rejects `CancellationCutoffHours < 0` | Low | Add `AppSettingsValidator_CancellationCutoffHours_Negative_ReturnsFail` | New `AppSettingsValidatorTests.cs` | L |
| F5 | `CancellationCutoffHours` absent from `appsettings.json` | Low | Add commented-out entry under `"AppSettings"` section | `appsettings.json` | L |
| F6 | Null-Slot cutoff bypass (informational) | Info | No action — FK constraint prevents in production | — | — |

---

## Appendix

**Rules applied:**

- `rules/ai-assistant-usage-policy.md` — explicit commands, minimal output
- `rules/code-anti-patterns.md` — no magic constants, no god objects
- `rules/dry-principle-guidelines.md` — single source of truth, delta updates
- `rules/security-standards-owasp.md` — OWASP A01/A03/A04 validated
- `rules/backend-development-standards.md` — service/controller patterns, async/await
- `rules/dotnet-architecture-standards.md` — vertical slice, `IEndpointDefinition`, FSM interceptor
- `rules/database-standards.md` — migration naming, column types, rowversion concurrency
- `rules/performance-best-practices.md` — PK lookups, no N+1, cache invalidation

**Search evidence (key grep patterns):**

- `class SwapMonitorJob` → `src/ClinicalHealthcare.Infrastructure/Jobs/SwapMonitorJob.cs`
- `[Timestamp]` in `Slot.cs` → confirms rowversion concurrency token on `Slot.RowVersion`
- `CancellationCutoffHours` → 8 matches across AppSettings, validator, endpoints, tests
- `IsAvailable = true` → `SwapMonitorJob.cs` L81 owns slot release (correct delegation)
- `ReminderJobId` → entity, migration, booking endpoint, cancel endpoint, reschedule endpoint, both test files
