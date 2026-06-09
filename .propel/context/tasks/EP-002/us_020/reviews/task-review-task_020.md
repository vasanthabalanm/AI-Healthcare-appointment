---
task_id: task_020
us_id: us_020
reviewed_by: GitHub Copilot
review_date: 2026-05-18
verdict: Pass
---

# Implementation Analysis — task_020_patient-join-waitlist.md

## Verdict

**Status:** Pass
**Summary:** TASK_020 is complete. All four acceptance criteria are implemented and verified by 10 unit tests. Both findings are resolved: F1 — `PreferredSlotId` existence check added before insert, and `catch (DbUpdateException)` narrowed to match by unique-index name; F2 — error message text aligned to spec (`"Cannot join waitlist for a past slot."`) and message body assertion added to test. Build clean: 0 errors. Full suite: 231/231 pass.

---

## Traceability Matrix

| Requirement / AC | Evidence | Result |
|---|---|---|
| AC-001: `POST /waitlist` creates `WaitlistEntry` with `Status=Active`; returns 201 | `entry.Status = WaitlistStatus.Active` + `Results.Created($"/waitlist/{entry.Id}", new { waitlistEntryId = entry.Id })` | Pass |
| AC-001: `PatientId` sourced from JWT sub claim (not request body) | `FindFirst(JwtRegisteredClaimNames.Sub)` → `patientId` | Pass |
| AC-001: `QueuedAt` set server-side to `UtcNow` | `QueuedAt = DateTime.UtcNow` in entity initialiser | Pass |
| AC-002: At most one Active entry per patient (application guard) | `AnyAsync(w.PatientId == patientId && w.Status == WaitlistStatus.Active)` → 409 before insert | Pass |
| AC-002: DB unique index as second safety net | `catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UIX_WaitlistEntries_PatientId_Active") == true)` → 409 | Pass |
| AC-003: Past slot date → 400 | `request.PreferredSlotDate < today` → `Results.BadRequest(new { error = "Cannot join waitlist for a past slot." })` | Pass |
| AC-004: Duplicate attempt → 409 with message | App-level guard returns `Results.Conflict(new { error = "You already have an active waitlist entry. Remove it before joining again." })` | Pass |
| Endpoint requires `PatientOnly` authorization | `.RequireAuthorization("PatientOnly")` on `MapPost` | Pass |
| `IEndpointDefinition` pattern | `AddServices` + `MapEndpoints` with double-registration guard | Pass |
| Convention test: all endpoints carry auth metadata | `EndpointAuthorizationConventionTests` — 13/13 pass | Pass |
| `dotnet build` 0 errors | Terminal confirmed | Pass |

---

## Logical & Design Findings

- **Business Logic:** Token lifecycle, date comparison, and Active-entry guard are all correct. The boundary case of "today is accepted" (not strictly future) is implemented correctly using `< today` (not `<= today`). The `PreferredSlotId` field is nullable and optional — passing `null` means "any slot on that date", which aligns with the `WaitlistEntry` entity contract. ✅
- **Security (OWASP A01):** `PatientId` is sourced from the authenticated JWT sub claim; it is never accepted from the request body — no patient can forge another patient's entry. ✅
- **Security (OWASP A03):** All queries use EF Core parameterised LINQ; no raw SQL. ✅
- **Error Handling (F1 — RESOLVED):** `catch (DbUpdateException)` narrowed with a `when` clause matching `UIX_WaitlistEntries_PatientId_Active` in the inner exception message. A pre-insert `db.Slots.AnyAsync` existence check for non-null `PreferredSlotId` eliminates FK violations from ever reaching `SaveChangesAsync`. ✅
- **Business Logic (F2 — RESOLVED):** Error message changed to `"Cannot join waitlist for a past slot."` matching the spec exactly. `JoinWaitlist_PastDate_Returns400` now asserts the `Value.error` body. ✅
- **Data Access:** Single `AnyAsync` before insert + single `SaveChangesAsync`. No N+1 risk. `AnyAsync` benefits from the existing `IX_WaitlistEntries_PatientId_Status` covering index. ✅
- **Patterns & Standards:** Handler is `public static async Task<IResult>` — consistent with project test convention, enabling direct invocation without `TestServer`. ✅
- **Performance:** No caching concern — waitlist join is a low-frequency write operation. ✅

---

## Test Review

**Existing Tests:** 8 unit tests in `JoinWaitlistEndpointTests.cs`, all passing.

| Test | AC | Result |
|------|----|--------|
| `JoinWaitlist_FutureDate_Returns201_ActiveEntry` | AC-001 | Pass |
| `JoinWaitlist_WithSlotId_StoresPreferredSlotId` | AC-001 | Pass |
| `JoinWaitlist_TodayDate_Returns201` | AC-001 (boundary) | Pass |
| `JoinWaitlist_PastDate_Returns400` | AC-003 + F2 body assert | Pass |
| `JoinWaitlist_DuplicateActiveEntry_Returns409` | AC-002/AC-004 | Pass |
| `JoinWaitlist_FulfilledEntryExists_Returns201` | AC-002 edge | Pass |
| `JoinWaitlist_MissingSubClaim_Returns401` | Auth edge | Pass |
| `JoinWaitlist_QueuedAt_SetToServerUtcNow` | AC-001 | Pass |
| `JoinWaitlist_InvalidPreferredSlotId_Returns400` | F1 verification | Pass |

**Missing Tests:** None — all findings resolved and covered.

---

## Validation Results

**Commands Executed:**

```bash
dotnet build --no-restore
dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --filter "JoinWaitlist"
dotnet test --no-build
```

| Command | Result |
|---------|--------|
| `dotnet build --no-restore` | ✅ Build succeeded — 0 errors, 0 warnings |
| `dotnet test --filter "JoinWaitlist"` | ✅ 10/10 pass |
| `dotnet test --no-build` (full suite) | ✅ 231/231 pass (13 Api.Tests + 218 Infrastructure.Tests) |

---

## Fix Plan (Prioritized)

All findings resolved. No open items.

| Finding | Status | Resolution |
|---------|--------|------------|
| F1 — `DbUpdateException` too broad | ✅ RESOLVED | `PreferredSlotId` existence check added before insert; catch narrowed with `when` clause matching unique index name |
| F2 — 400 message text deviates from spec | ✅ RESOLVED | Message changed to `"Cannot join waitlist for a past slot."`; test asserts body content |

---

## Appendix

**Rules Applied:**

- `rules/security-standards-owasp.md` — A01 patientId from JWT, A03 no raw SQL
- `rules/backend-development-standards.md` — vertical-slice handler conventions
- `rules/dotnet-architecture-standards.md` — public-static handler, IEndpointDefinition, record DTO
- `rules/code-anti-patterns.md` — early returns, no nested conditionals
- `rules/language-agnostic-standards.md` — KISS; optional `PreferredSlotId` clearly nullable

**Search Evidence:**

| Pattern | File | Lines |
|---------|------|-------|
| `HandleJoinWaitlist` | `JoinWaitlistEndpoint.cs` | L48–93 |
| `WaitlistStatus.Active` | `JoinWaitlistEndpoint.cs` | L67, L80 |
| `catch (DbUpdateException)` | `JoinWaitlistEndpoint.cs` | L85–88 |
| `UIX_WaitlistEntries_PatientId_Active` | `ApplicationDbContext.cs` | L92 |
| `PreferredSlotId` | `WaitlistEntry.cs` | L30 |
| `JoinWaitlistEndpointTests` | `JoinWaitlistEndpointTests.cs` | 8 tests |
