---
task_id: TASK_033
us_id: us_033
epic_id: EP-005
reviewed_at: 2026-05-19
reviewer: analyze-implementation (automated)
verdict: Pass
findings_count: 2
findings_severity: MEDIUM × 1 (resolved), LOW × 1 (resolved)
---

# Implementation Analysis — TASK_033

## Verdict

**Status:** Pass
**Summary:** TASK_033 delivers all five acceptance criteria and both edge cases with well-structured code.
The `SearchPatientsEndpoint` performs correctly scoped EF Core LIKE queries, and the `RegisterWalkInEndpoint`
enforces queue capacity, deduplication, and AuditLog writing as specified.
One MEDIUM defect is raised: the AuditLog EntityId back-fill pattern issues an EF Core UPDATE against a
table documented as INSERT-only (UPDATE permissions revoked at the SQL Server GRANT level). This is
undetectable with InMemory tests but will throw a database permission exception in production on any
capacity-override request. One LOW finding is raised for the missing EntityId assertion in the override test.

---

## Traceability Matrix

| Requirement / Acceptance Criterion | Evidence (file : fn / line) | Result |
|---|---|---|
| **AC-001** — `GET /staff/patients/search` returns matching patients via LIKE | `SearchPatientsEndpoint.cs: HandleSearch` — `EF.Functions.Like(u.FirstName + " " + u.LastName, pattern)` + reverse; optional DOB filter; 50-result safety cap | **Pass** |
| **AC-002** — Walk-in creates minimal `UserAccount` with `WalkIn=true` | `RegisterWalkInEndpoint.cs: HandleRegisterWalkIn L88-101` — `new UserAccount { Role="patient", WalkIn=true, IsActive=true, ... }` | **Pass** |
| **AC-003** — Queue capacity default 20; exceeding → 409 unless Override | `AppSettings.cs: QueueCapacity = 20`; `RegisterWalkInEndpoint.cs L113-120` — `CountAsync + capacity check` | **Pass** |
| **AC-004** — `Override=true` bypasses capacity + writes AuditLog | `RegisterWalkInEndpoint.cs L123-131` — AuditLog added with `Action="QUEUE_OVERRIDE"` | **Pass (logic)** / **Fail (DB constraint)** — see F1 |
| **AC-005** — `QueueEntry` created for walk-in patient | `RegisterWalkInEndpoint.cs L134-143` — `new QueueEntry { IsWalkIn=true, Position=nextPosition, ... }` | **Pass** |
| Edge — existing patient → linked, not duplicated | `RegisterWalkInEndpoint.cs L72-78` — `FirstOrDefaultAsync` by name+DOB; reuse ID if found | **Pass** |
| `StaffOrAdmin` authorization on both endpoints | `SearchPatientsEndpoint.cs L27`, `RegisterWalkInEndpoint.cs L43` — `.RequireAuthorization("StaffOrAdmin")` | **Pass** |
| `QueueCapacity ≥ 1` validated at startup | `AppSettingsValidator.cs` — `if (options.QueueCapacity < 1) → Fail` | **Pass** |
| `dotnet build` 0 errors / 411 tests passing | Terminal: 411 passed, 0 failed | **Pass** |

**Requirements: 8 / 9 AC assertions pass outright; 1 has a production defect (F1).**

---

## Logical & Design Findings

- **Business Logic:** Capacity count includes all QueueStatus values (Waiting, CheckedIn, Removed). This is intentional — headcount is per-day regardless of check-in state. Correct.
- **Security:** Staff ID sourced from JWT `sub` claim (OWASP A01 ✅). `EF.Functions.Like` is fully parameterised — no SQL injection surface (OWASP A03 ✅). `FirstName`/`LastName` bounded to 100 chars by `[MaxLength(100)]`.
- **Error Handling:** DTO validation returns 422 with structured `ValidationProblem`. No-sub-claim guard returns 401. Capacity-exceeded returns 409 with structured body including `capacity` and `current`. ✅
- **Data Access (F1 — MEDIUM):** The AuditLog entity is documented as INSERT-only: *"UPDATE and DELETE permissions are revoked at the SQL Server GRANT level in the `AuditLogPhiRetention` migration."* The back-fill pattern in `HandleRegisterWalkIn` issues an `UPDATE AuditLogs SET EntityId = @p1 WHERE Id = @p2`, which will be rejected by the database permission in production. The InMemory provider has no GRANT enforcement, so all 12 tests pass. Fix: save `QueueEntry` before adding the `AuditLog`; then `EntityId` is available at insert time — no UPDATE needed.
- **Performance:** `EF.Functions.Like` is server-side translated; `AsNoTracking()` applied on search queries; `CountAsync` for capacity is O(1) with the `(QueueDate, Status)` index. ✅
- **Patterns & Standards:** Vertical-slice `IEndpointDefinition`; `IOptions<AppSettings>` injected; `StaffOrAdmin` policy reuses existing global policy. ✅

---

## Test Review

### Existing Tests (12 in `StaffWalkInEndpointTests.cs`)

| Test | Coverage |
|---|---|
| `Search_NoQueryNoDob_ReturnsEmpty` | Guard: no full-table scan |
| `Search_PartialName_ReturnsMatchingPatients` | AC-001: LIKE match; staff role excluded |
| `Search_DobFilter_ReturnsMatchingPatient` | AC-001: DOB narrows results |
| `RegisterWalkIn_NewPatient_Returns201AndCreatesAccount` | AC-002: new account created |
| `RegisterWalkIn_FirstEntry_Position1` | AC-005: Position=1 |
| `RegisterWalkIn_SecondEntry_Position2` | AC-005: sequential Position |
| `RegisterWalkIn_AtCapacity_NoOverride_Returns409` | AC-003: 409 |
| `RegisterWalkIn_AtCapacity_WithOverride_Returns201AndAuditLog` | AC-004: 201 + AuditLog exists |
| `RegisterWalkIn_ExistingPatient_LinksToExistingAccount` | Edge: deduplication |
| `RegisterWalkIn_MissingFirstName_Returns422` | Validation: required field |
| `RegisterWalkIn_FirstNameTooLong_Returns422` | Validation: MaxLength |
| `RegisterWalkIn_NoSubClaim_Returns401` | Auth guard |

### Missing Tests (must add)

- [ ] **Override — assert `AuditLog.EntityId > 0`**: The override test verifies the AuditLog row exists but does not assert `audit.EntityId > 0`. Adding this assertion would expose F1 as a runtime failure even in InMemory (EntityId stays 0 after the first SaveChangesAsync because the back-fill uses a second UPDATE, and InMemory allows it). More importantly it documents the expected contract.

---

## Validation Results

| Command | Outcome |
|---|---|
| `dotnet build` | Build succeeded — 0 Error(s) |
| `dotnet test -v q` | Passed: 411 (Api: 13, Infrastructure: 398) — Failed: 0 |

---

## Fix Plan (Prioritized)

### F1 — MEDIUM | AuditLog EntityId back-fill violates INSERT-only DB constraint — **RESOLVED**

- Reordered `RegisterWalkInEndpoint`: `QueueEntry` is now saved first (populating `entry.Id`), then `AuditLog` is INSERTed with the correct `EntityId`. The second `SaveChangesAsync` back-fill and the `ChangeTracker` UPDATE path have been removed.

---

### F2 — LOW | Override test does not assert `AuditLog.EntityId > 0` — **RESOLVED**

- Added `Assert.True(audit.EntityId > 0, "...")` to `RegisterWalkIn_AtCapacity_WithOverride_Returns201AndAuditLog`.

---

## Appendix

### Rules Applied

- `rules/ai-assistant-usage-policy.md`
- `rules/code-anti-patterns.md`
- `rules/dry-principle-guidelines.md`
- `rules/language-agnostic-standards.md`
- `rules/security-standards-owasp.md`
- `rules/backend-development-standards.md`
- `rules/dotnet-architecture-standards.md`
- `rules/database-standards.md`

### Search Evidence

| Pattern | Files Hit |
|---|---|
| `HandleRegisterWalkIn`, `HandleSearch` | `RegisterWalkInEndpoint.cs`, `SearchPatientsEndpoint.cs` |
| `INSERT-only`, `UPDATE and DELETE permissions revoked` | `AuditLog.cs` (entity doc comment) |
| `QueueEntries`, `QueueCapacity` | `ApplicationDbContext.cs`, `AppSettings.cs` |
| `StaffOrAdmin` | `Program.cs L175` (global policy) |
