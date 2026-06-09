---
task_id: task_021
us_id: us_021
reviewed_by: GitHub Copilot
review_date: 2026-05-18
verdict: Pass
---

# Implementation Analysis — task_021_slot-swap-hangfire-swapmonitor.md

## Verdict

**Status:** Pass
**Summary:** TASK_021 is fully resolved. All five acceptance criteria are implemented. Three post-review findings have been addressed: F1 — `CancelAppointmentEndpoint` no longer releases the slot; `SwapMonitorJob` now owns slot release and cache invalidation (releases slot only when no Active waitlist entry exists); F2 — `SwapMonitorJob` now sends the offer email *before* `SaveChangesAsync`, ensuring Hangfire retries find the entry still `Active` if the email fails; F3 — 15 new unit tests added across `CancelAppointmentEndpointTests`, `AcceptSwapOfferEndpointTests`, `SwapMonitorJobTests`, and `ExpireSwapOfferJobTests`. `NoOpEmailService` stub registered in `Program.cs` as the `IEmailService` implementation until MailKit is wired in US_026. Build clean: 0 errors, 0 warnings. 246/246 tests pass.

---

## Traceability Matrix

| Requirement / AC | Evidence | Result |
|---|---|---|
| AC-001: `SwapMonitorJob` enqueued when slot released | `CancelAppointmentEndpoint.HandleCancelAppointment` L84: `jobs.Enqueue<SwapMonitorJob>(j => j.ExecuteAsync(appointment.SlotId, null!))` | Pass |
| AC-002: Job finds oldest Active WaitlistEntry | Two-query strategy: `PreferredSlotId == releasedSlotId` first, then `PreferredSlotId == null` — both `OrderBy(w.QueuedAt)` | Pass |
| AC-002: Sends offer email | `IEmailService.SendAsync(...)` after status transition; patient email fetched from `db.UserAccounts.FindAsync` | Pass (see F2) |
| AC-003: Offer window = `AppSettings.SwapOfferWindowHours` | `entry.OfferExpiresAt = DateTime.UtcNow.AddHours(_appSettings.Value.SwapOfferWindowHours)`; default = 2 | Pass |
| AC-003: `AppSettings` config class created | `AppSettings.cs` with `SectionName = "AppSettings"`, `SwapOfferWindowHours = 2` | Pass |
| AC-004: `POST /waitlist/{id}/accept` atomic EF Core transaction | `BeginTransactionAsync` → `SaveChangesAsync` → `CommitAsync`; `DbUpdateConcurrencyException` → 409 + `RollbackAsync` | Pass |
| AC-004: WaitlistEntry set to `Fulfilled` | `entry.Status = WaitlistStatus.Fulfilled` inside transaction | Pass |
| AC-004: New Appointment created | `db.Appointments.Add(appointment)` inside transaction | Pass |
| AC-005: `ExpireSwapOfferJob` recurring job registered | `RecurringJob.AddOrUpdate<ExpireSwapOfferJob>("expire-swap-offers", ..., Cron.Minutely)` in `Program.cs` | Pass |
| AC-005: Expired entries set `Status=Expired`; slot re-released | `entry.Status = WaitlistStatus.Expired`; `slot.IsAvailable = true`; `db.SaveChangesAsync()` | Pass |
| AC-005: Redis slot cache invalidated after expiry | `_cache.DeleteAsync($"slots:date:{DateOnly...}")` per re-released slot | Pass |
| Rowversion prevents double-accept race | `[Timestamp] RowVersion` on `Slot`; caught as `DbUpdateConcurrencyException` → 409 | Pass |
| Redis cache invalidated after accept | `cache.DeleteAsync(dateKey)` after `CommitAsync` | Pass |
| `WaitlistEntry.OfferSent` status added | `WaitlistStatus.OfferSent = 3` in enum; `OfferExpiresAt` and `OfferedSlotId` fields added | Pass |
| `IEndpointDefinition` pattern | Both new endpoints implement `AddServices` + `MapEndpoints` with double-reg guard | Pass |
| `public static` handlers | Both `HandleAcceptSwapOffer` and `HandleCancelAppointment` are `public static async Task<IResult>` | Pass |
| Convention test (all endpoints have auth) | 231/231 tests pass | Pass |
| `dotnet build` 0 errors | Confirmed | Pass |

---

## Logical & Design Findings

- **Business Logic (F1 — Medium) — RESOLVED:** ~~`CancelAppointmentEndpoint` sets `slot.IsAvailable = true` and commits to the DB, *then* enqueues `SwapMonitorJob`...~~ `CancelAppointmentEndpoint` no longer touches slot availability. `SwapMonitorJob.ExecuteAsync` now owns the slot release: when no Active entry is found the job sets `slot.IsAvailable = true`, calls `SaveChangesAsync`, and invalidates the Redis cache. The availability window is eliminated.

- **Business Logic (F2 — Medium) — RESOLVED:** ~~`SwapMonitorJob` commits the DB state... then sends the email...~~ Email is now sent *before* `SaveChangesAsync`. If `IEmailService.SendAsync` throws, the DB is not committed, the entry remains `Active`, and Hangfire retries with a clean state. A `LogWarning` is emitted if the patient account is not found so the offer is not silently lost.

- **Security (OWASP A01):** `AcceptSwapOfferEndpoint` validates `entry.PatientId == patientId` before processing — correct. `CancelAppointmentEndpoint` validates `appointment.PatientId == patientId` — correct. Neither accepts patient identity from the request body. ✅

- **Security (OWASP A03):** All queries use EF Core parameterised LINQ. ✅

- **Error Handling:** `AcceptSwapOfferEndpoint` wraps the write in an explicit `BeginTransactionAsync` / `CommitAsync` / `RollbackAsync` with `catch { rollback; throw; }` — correct pattern. ✅

- **Data Access:** `SwapMonitorJob` uses two focused single-row queries with `OrderBy(QueuedAt)` — benefits from the existing `IX_WaitlistEntries_PatientId_Status` index. `ExpireSwapOfferJob` batches all expired entries in two queries (one for entries, one for slots) and a single `SaveChangesAsync` — no N+1. ✅

- **Patterns & Standards (F3) — RESOLVED:** 15 unit tests added across four new files: `CancelAppointmentEndpointTests` (5), `AcceptSwapOfferEndpointTests` (5), `SwapMonitorJobTests` (3), `ExpireSwapOfferJobTests` (2). All tests pass: 246/246.

- **`WaitlistGuardInterceptor` compatibility:** `SwapMonitorJob` transitions entries from `Active` to `OfferSent` — the interceptor only checks `EntityState.Added` entries with `Status == Active`. The modified entry is `EntityState.Modified`, so no false positive. ✅

- **Performance:** `ExpireSwapOfferJob` loads all expired entries and their slots in two batch queries before a single `SaveChangesAsync`. Redis invalidation is per-slot (not per-entry), deduplicated by slot ID. ✅

---

## Test Review

**Existing Tests:** 15 unit tests added for TASK_021 components (all pass).

**Tests Added (F3 resolved):**

- [x] Unit: `CancelAppointment_Scheduled_Returns204_SlotRemainsUnavailable`
- [x] Unit: `CancelAppointment_Scheduled_EnqueuesSwapMonitorJob`
- [x] Unit: `CancelAppointment_WrongPatient_Returns403`
- [x] Unit: `CancelAppointment_AlreadyCancelled_Returns400`
- [x] Unit: `CancelAppointment_NotFound_Returns404`
- [x] Unit: `AcceptSwapOffer_ValidOffer_Returns200_AppointmentCreated_EntryFulfilled`
- [x] Unit: `AcceptSwapOffer_ExpiredOffer_Returns400`
- [x] Unit: `AcceptSwapOffer_WrongPatient_Returns403`
- [x] Unit: `AcceptSwapOffer_EntryNotOfferSent_Returns400`
- [x] Unit: `AcceptSwapOffer_MissingSubClaim_Returns401`
- [x] Unit: `SwapMonitorJob_WithActiveEntry_SetsOfferSent_SendsEmail_SlotKeptUnavailable`
- [x] Unit: `SwapMonitorJob_NoActiveEntry_ReleasesSlot_InvalidatesCache`
- [x] Unit: `SwapMonitorJob_SlotNotFound_NoSideEffects`
- [x] Unit: `ExpireSwapOfferJob_ExpiredOffer_SetsExpired_ReleasesSlot_InvalidatesCache`
- [x] Unit: `ExpireSwapOfferJob_ActiveOffer_NoChanges`

---

## Validation Results

**Commands Executed:**

```bash
dotnet build --no-restore
dotnet test --no-build
```

| Command | Result |
|---------|--------|
| `dotnet build --no-restore` | ✅ Build succeeded — 0 errors, 0 warnings |
| `dotnet test --no-build` | ✅ 246/246 pass (13 Api.Tests + 233 Infrastructure.Tests) |

---

## Fix Plan (Prioritized)

1. **F1 — RESOLVED** — Removed `slot.IsAvailable = true` + cache invalidation from `CancelAppointmentEndpoint`. `SwapMonitorJob` now releases slot and invalidates cache in the no-entry branch.

2. **F2 — RESOLVED** — Email moved before `SaveChangesAsync` in `SwapMonitorJob`. Retry semantics are now clean: email failure leaves DB unchanged, entry stays `Active`.

3. **F3 — RESOLVED** — 15 unit tests added across 4 new test files. `NoOpEmailService` stub created and registered in `Program.cs`. 246/246 pass.

---

## Appendix

**Rules Applied:**

- `rules/security-standards-owasp.md` — A01 patientId from JWT, A03 no raw SQL
- `rules/backend-development-standards.md` — vertical-slice handler conventions, Hangfire job DI pattern
- `rules/dotnet-architecture-standards.md` — public-static handler, IEndpointDefinition, EF Core transaction
- `rules/code-anti-patterns.md` — early returns, no nested conditionals
- `rules/language-agnostic-standards.md` — KISS; two-query strategy for clarity over a single complex LINQ expression

**Search Evidence:**

| Pattern | File | Lines |
|---------|------|-------|
| `ExecuteAsync` | `SwapMonitorJob.cs` | L52–109 |
| `SaveChangesAsync` (before email) | `SwapMonitorJob.cs` | L88 |
| `BeginTransactionAsync` | `AcceptSwapOfferEndpoint.cs` | L91 |
| `slot.IsAvailable = true` | `CancelAppointmentEndpoint.cs` | L76 |
| `jobs.Enqueue<SwapMonitorJob>` | `CancelAppointmentEndpoint.cs` | L84 |
| `RecurringJob.AddOrUpdate<ExpireSwapOfferJob>` | `Program.cs` | L269 |
| `OfferSent = 3` | `WaitlistEntry.cs` | L12 |
| `OfferExpiresAt`, `OfferedSlotId` | `WaitlistEntry.cs` | L51, L57 |
| `WaitlistGuardInterceptor` — only guards `EntityState.Added` | `WaitlistGuardInterceptor.cs` | L43 |
