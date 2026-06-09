---
task_id: task_037
us_id: us_037
epic_id: EP-005
review_date: 2026-05-19
reviewer: GitHub Copilot (analyze-implementation)
---

# Implementation Analysis -- task_037_noshow-risk-alerts-outreach.md

## Verdict

**Status:** Pass
**Summary:** All four acceptance criteria are fully implemented across three vertical-slice endpoints
(`GetHighRiskAppointmentsEndpoint`, `RecordOutreachEndpoint`, `UpdateAppointmentStatusEndpoint`) and
backed by the `OutreachRecord` entity with migration. The FSM guard in `UpdateAppointmentStatusEndpoint`
correctly blocks `Arrived → NoShow` and `Cancelled → NoShow` with 409 responses. Security compliance is
complete: all endpoints are gated behind `StaffOrAdmin`, staff identity is sourced exclusively from the
JWT `sub` claim, and no raw SQL is used. Thirteen unit tests cover every AC and edge case; all 455
project tests pass with zero failures.

---

## Traceability Matrix

| Requirement / Acceptance Criterion | Evidence (file: fn / line) | Result |
|---|---|---|
| AC-001: `GET /schedule/high-risk?date=` returns `IsHighRisk=true` appointments | `GetHighRiskAppointmentsEndpoint.HandleGetHighRisk` L38-60 — `.Where(a => a.IsHighRisk && a.Status != Cancelled && DateOnly.FromDateTime(a.Slot!.SlotTime) == targetDate)` | Pass |
| AC-001: Excludes Cancelled status | Same handler L40 — `a.Status != AppointmentStatus.Cancelled` | Pass |
| AC-001: Defaults to today UTC; `?date=` overrides | L38 — `date ?? DateOnly.FromDateTime(DateTime.UtcNow)` | Pass |
| AC-001: Returns ordered results with RiskScore | L41 — `.OrderBy(a => a.Slot!.SlotTime)`; DTO includes `NoShowRiskScore` L52 | Pass |
| AC-002: `POST /outreach` inserts `OutreachRecord` with notes | `RecordOutreachEndpoint.HandleRecordOutreach` L56-65 — `db.OutreachRecords.Add(record)` | Pass |
| AC-002: Returns 201 with `outreachRecordId` and Location header | L67 — `Results.Created($"/appointments/{id}/outreach/{record.Id}", ...)` | Pass |
| AC-002: Staff ID from JWT `sub` only (OWASP A01) | L47-50 — `httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)` | Pass |
| AC-002: 404 on unknown appointment | L53-54 — `AnyAsync` check | Pass |
| AC-003: `PATCH /status {NoShow}` validates status value | `UpdateAppointmentStatusEndpoint.HandleUpdateStatus` L68-70 — `StringComparison.OrdinalIgnoreCase` guard → 400 | Pass |
| AC-003: FSM guard `Scheduled → NoShow` only | L79-83 — `appointment.Status != Scheduled → Results.Conflict` | Pass |
| AC-003: Sets `Status = NoShow` | L87 — `appointment.Status = AppointmentStatus.NoShow` | Pass |
| AC-003: Writes AuditLog (same `SaveChangesAsync`) | L93-100 — `db.AuditLogs.Add(new AuditLog { Action = "NoShow" })` before `SaveChangesAsync` | Pass |
| AC-003: Catches FSM interceptor exception → 409 | L104-107 — `catch (InvalidOperationException ex) when (ex.Message.Contains(...))` | Pass |
| AC-004: Slot released (`IsAvailable=true`) | L89-91 — `appointment.Slot.IsAvailable = true` in same `SaveChangesAsync` | Pass |
| AC-004: `SwapMonitorJob` enqueued on NoShow | L111 — `jobs.Enqueue<SwapMonitorJob>(j => j.ExecuteAsync(appointment.SlotId, null!))` | Pass |
| AC-004: Redis slot cache invalidated | L114-119 — `cache.DeleteAsync($"{GetSlotsEndpoint.CacheKeyPrefix}{date:yyyy-MM-dd}", ct)` | Pass |
| Edge: `Arrived → NoShow` → 409 | FSM guard L79-83; test `UpdateStatus_ArrivedAppointment_Returns409` | Pass |
| Edge: `Cancelled → NoShow` → 409 | Same guard; test `UpdateStatus_CancelledAppointment_Returns409` | Pass |
| Security: `StaffOrAdmin` on all three endpoints | `GetHighRiskAppointmentsEndpoint` L27, `RecordOutreachEndpoint` L31, `UpdateAppointmentStatusEndpoint` L38 | Pass |
| Infrastructure: `OutreachRecord` entity + migration | `OutreachRecord.cs`; migration `20260519142247_OutreachRecord.cs` | Pass |

---

## Logical & Design Findings

- **Business Logic:** No misinterpretations detected. The "NoShow-only" gate
  (`StringComparison.OrdinalIgnoreCase`) correctly rejects any other status string with 400.
  The `.Include(a => a.Slot)` ensures the slot navigation property is loaded before release.
  Cache key construction uses the same `GetSlotsEndpoint.CacheKeyPrefix` constant as the rest
  of the codebase — no magic string duplication.

- **Security:** All three endpoints enforce `RequireAuthorization("StaffOrAdmin")`. Staff identity
  is always extracted from `JwtRegisteredClaimNames.Sub` in the token, never from the request body.
  Route constraint `{id:int}` on both `/outreach` and `/status` endpoints prevents non-integer
  path segment injection. No raw SQL or string-interpolated queries.

- **Error Handling:** Comprehensive: 400 (bad status), 401 (missing sub claim), 404 (not found),
  409 (FSM conflict), plus FSM interceptor catch block. `SaveChangesAsync` result is not checked for
  row count, which is acceptable given EF Core throws `DbUpdateException` on constraint violations.

- **Data Access:** `AsNoTracking()` applied on the read-only `GET /high-risk` path. FKs use
  `OnDelete.Restrict` — no cascade deletes. Index `IX_OutreachRecords_AppointmentId` aligns with
  the expected query pattern. The migration also auto-generated `IX_OutreachRecords_StaffId`,
  which is a bonus.

- **Frontend:** N/A — no UI impact.

- **Performance:** `GET /high-risk` is not paginated. For typical clinic operations
  (tens of high-risk appointments per day) this is acceptable. If the list grows to hundreds,
  pagination should be added. No N+1 issues — navigation properties are projected, not navigated
  in a loop. `?date=` parameter allows targeted queries.

- **Patterns & Standards:** All three endpoints follow the `IEndpointDefinition` vertical-slice
  pattern with `AddServices` + `MapEndpoints`. Public static handlers are directly testable.
  DTOs are `sealed record` types matching the project convention.

### F1 — LOW: `OutreachRecord.Notes` has no max-length constraint

| Attribute | Value |
|---|---|
| Severity | Low |
| Location | `OutreachRecord.cs` — `Notes` property; `ApplicationDbContext.cs` L167-183 |
| Finding | `Notes` is `string?` with no `[MaxLength]` attribute and no `.HasMaxLength()` in the DbContext config, resulting in `nvarchar(max)` in the migration. ASP.NET Core's default 30 MB request body limit provides a runtime backstop, but an explicit max-length (e.g. 2000 characters) would enforce the business constraint at the database level and align with the codebase convention seen on other string properties. |
| Recommendation | Add `[MaxLength(2000)]` to `OutreachRecord.Notes` **or** `.Property(o => o.Notes).HasMaxLength(2000)` in the entity config. Requires a new migration. |
| Blocking | No |

### F2 — INFORMATIONAL: Post-save enqueue is not transactional

| Attribute | Value |
|---|---|
| Severity | Informational |
| Location | `UpdateAppointmentStatusEndpoint.cs` L111-119 |
| Finding | `jobs.Enqueue<SwapMonitorJob>` and `cache.DeleteAsync` run after `SaveChangesAsync`. A process crash in the window between the DB commit and the Hangfire enqueue would leave the slot available but without a swap offer. This is a known at-least-once delivery limitation. The same pattern exists in `CancelAppointmentEndpoint` and is therefore consistent with the codebase design decision. |
| Recommendation | Accepted risk — consistent with existing pattern. No action required unless a transactional outbox pattern is adopted project-wide. |
| Blocking | No |

---

## Test Review

- **Existing Tests:**
  - `NoShowRiskOutreachTests.cs` — 13 tests, all green (455 total project tests passing)
  - AC-001 coverage: `GetHighRisk_ReturnsOnlyHighRiskAppointments`, `GetHighRisk_ExcludesCancelledAppointments`, `GetHighRisk_DateParam_FiltersToThatDate` (3 tests)
  - AC-002 coverage: `RecordOutreach_ValidRequest_Returns201AndPersistsRecord`, `RecordOutreach_UnknownAppointment_Returns404`, `RecordOutreach_MissingSubClaim_Returns401` (3 tests)
  - AC-003 coverage: `UpdateStatus_NoShow_Returns200AndStatusIsNoShow`, `UpdateStatus_NoShow_WritesAuditLog` (2 tests)
  - AC-004 coverage: `UpdateStatus_NoShow_ReleasesSlotAndEnqueuesSwapJob` — verifies `IsAvailable=true` and `j.Create(SwapMonitorJob, IState)` called (1 test)
  - FSM edge: `UpdateStatus_ArrivedAppointment_Returns409`, `UpdateStatus_CancelledAppointment_Returns409` (2 tests)
  - Error paths: `UpdateStatus_UnknownStatus_Returns400`, `UpdateStatus_UnknownAppointment_Returns404`, `UpdateStatus_MissingSubClaim_Returns401` (3 tests)

- **Missing Tests (must add):** None — all acceptance criteria and stated edge cases have direct test coverage.

  - [ ] Optional (non-blocking): Test that outreach notes are persisted correctly when `Notes` is `null` (null notes scenario).
  - [ ] Optional (non-blocking): Test `GET /high-risk` returns RiskScore in descending order if that becomes a requirement.

---

## Validation Results

- **Commands Executed:** `dotnet test -v q`
- **Outcomes:**

```text
Passed!  - Failed: 0, Passed:  13, Skipped: 0, Total:  13  -- ClinicalHealthcare.Api.Tests.dll
Passed!  - Failed: 0, Passed: 442, Skipped: 0, Total: 442  -- ClinicalHealthcare.Infrastructure.Tests.dll
```

**Total: 455 tests, 0 failed, 0 skipped.**

---

## Fix Plan (Prioritized)

1. **(Optional) Add max-length to `OutreachRecord.Notes`** — `OutreachRecord.cs` + `ApplicationDbContext.cs` + new migration — ETA 0.5 h — Risk: L

---

## Appendix

- **Context7 References:** None required — analysis based on direct source inspection.
- **Search Evidence:**
  - `grep OutreachRecord ApplicationDbContext.cs` — confirmed DbSet registration + entity config (L22, L166-183)
  - `grep SwapMonitorJob CancelAppointmentEndpointTests.cs` — confirmed `j.Create(Job, IState)` mock pattern
  - `file_search *OutreachRecord*.cs` — migration files confirmed at `20260519142247_OutreachRecord.cs`
