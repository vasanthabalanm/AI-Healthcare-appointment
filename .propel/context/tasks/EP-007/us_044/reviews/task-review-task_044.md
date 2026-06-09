---
task: TASK_044
epic: EP-007
us: US_044
reviewed: 2026-05-20
reviewer: GitHub Copilot (Claude Sonnet 4.6)
verdict: Conditional Pass
---

# Implementation Analysis — TASK_044 — 360° Patient View + Redis Cache + Trust-First Verify

## Verdict

**Status:** Conditional Pass  
**Summary:** All five acceptance criteria and both edge cases are functionally implemented and verified by 12 new passing unit tests (550 total suite, 0 failures). `GET /patients/{id}/view360` correctly implements the cache-aside Redis pattern (TTL=300s, miss→assemble from PostgreSQL, hit→return cached). `PATCH /patients/{id}/verify` correctly guards against unresolved conflicts and already-verified patients. Document upload correctly invalidates the 360° cache key. Three findings require remediation: (1) `Get360ViewEndpoint.MapEndpoints` is missing the `Produces(403)` OpenAPI declaration—a client-contract gap consistent with the pattern already fixed in TASK_043; (2) `VerifyPatientEndpoint` bypasses `IConflictService` in favour of a direct `pgDb.ConflictFlags.CountAsync` call, deviating from the task spec's explicit service-layer contract; (3) `ILogger<Get360ViewEndpoint>` is injected but never called—dead injection that should be removed to keep the handler signature honest.

---

## Traceability Matrix

| Requirement / Acceptance Criterion | Evidence (file : method : line) | Result |
|---|---|---|
| AC-001: GET endpoint returns assembled view | `Get360ViewEndpoint.cs:HandleGet360View` — returns `PatientView360Dto` via `Results.Ok(view)` | Pass |
| AC-001: Cache TTL = 300 s | `Get360ViewEndpoint.cs:28` — `CacheTtlSeconds = 300`; `L105` — `TimeSpan.FromSeconds(CacheTtlSeconds)` | Pass |
| AC-002: Cache miss → PostgreSQL assembly | `Get360ViewEndpoint.cs:62–103` — queries sqlDb + pgDb, builds dto, calls `SetAsync` | Pass |
| AC-002: Cache hit → return cached (no DB) | `Get360ViewEndpoint.cs:55–57` — early return on `cached is not null` | Pass |
| AC-003: PATCH /verify sets VerificationStatus = Verified | `VerifyPatientEndpoint.cs:HandleVerifyPatient:68` — `patient.VerificationStatus = VerificationStatus.Verified` | Pass |
| AC-003: Sets VerifiedById from JWT sub | `VerifyPatientEndpoint.cs:58–61,69` — JWT sub extracted; `patient.VerifiedById = staffId` | Pass |
| AC-003: Sets VerifiedAt = UtcNow | `VerifyPatientEndpoint.cs:70` — `patient.VerifiedAt = DateTime.UtcNow` | Pass |
| AC-004: Verify blocked (409) if Unresolved ConflictFlag | `VerifyPatientEndpoint.cs:64–73` — `CountAsync(Unresolved) > 0` → `Results.Conflict(...)` | Pass |
| AC-005: Upload invalidates `view360:{patientId}` | `UploadDocumentEndpoint.cs:144` — `cache.DeleteAsync($"{Get360ViewEndpoint.CacheKeyPrefix}{id}", ct)` | Pass |
| Edge: Already-Verified → 409 | `VerifyPatientEndpoint.cs:75–76` — `VerificationStatus == Verified` → `Results.Conflict` | Pass |
| Edge: No fields → empty sections; verify allowed | `Get360ViewEndpoint.cs:84–89` — empty `grouped` dict returned; verify checks conflict count only | Pass |
| All endpoints require StaffOrAdmin auth | `Get360ViewEndpoint.cs:37`, `VerifyPatientEndpoint.cs:37` — `.RequireAuthorization("StaffOrAdmin")` | Pass |

---

## Logical & Design Findings

### Business Logic

- **PASS** — Cache-aside pattern is correctly implemented: `GetAsync` checked first; 404 returned for unknown patient on cache miss only; `SetAsync` called after successful assembly; `DeleteAsync` called from both `VerifyPatientEndpoint` (on verification) and `UploadDocumentEndpoint` (on new upload).
- **PASS** — Conflict guard uses `CountAsync` rather than `AnyAsync`, enabling the 409 body to include `unresolvedCount`. This is correct behaviour even though it reads one extra integer from the DB.
- **PASS** — `VerificationStatus` guard is checked *after* the conflict guard, meaning a patient with both unresolved conflicts and already-verified status returns the conflict 409, not the already-verified 409. This ordering is defensible (block the actionable condition first).
- **GAP (MEDIUM — M-003)** — Task spec §Implementation Plan step 2 explicitly states: *"Call `IConflictService.HasUnresolvedConflicts(patientId)`"*. The implementation bypasses this service abstraction and queries `pgDb.ConflictFlags` directly. `IConflictService` is no longer injected into `HandleVerifyPatient`. While the runtime behaviour is identical, this deviates from the specified service-layer contract and reduces the testability boundary. **Recommended fix:** extend `IConflictService` with `GetUnresolvedCountAsync(int patientId, CancellationToken ct)` returning `Task<int>`, use it in `VerifyPatientEndpoint`, and restore injection; or accept the deviation with an explicit ADR noting the interface gap.

### Security

- **PASS (OWASP A01)** — `VerifyPatientEndpoint` extracts staff identity exclusively from `httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)` with a `"sub"` fallback. Staff ID is never accepted from the request body or route parameters.
- **PASS (OWASP A03)** — All database operations use EF Core parameterized queries (`FirstOrDefaultAsync`, `CountAsync`, `AnyAsync`). No string-concatenated SQL.
- **PASS** — `PatientView360Dto` exposes `Email` (PII). Since both endpoints require the `StaffOrAdmin` role, this data is only reachable by authorized personnel. No bearer-token data is included in the cached DTO — the same serialized object is safe to serve to any authorized staff member.
- **GAP (MEDIUM — M-004) ✅ FIXED** — `Get360ViewEndpoint.MapEndpoints` now declares all five status codes including `Produces(StatusCodes.Status403Forbidden)` (added during this review). `VerifyPatientEndpoint` already had 403 declared.

### Error Handling

- **PASS** — 401 returned via `Results.Unauthorized()` when JWT sub claim is absent or non-integer in `VerifyPatientEndpoint`.
- **PASS** — 404 returned via `Results.NotFound(new { error = "..." })` for missing patient in both endpoints.
- **PASS** — 409 returned via `Results.Conflict(new { error = "...", unresolvedCount = N })` for conflict guard; `Results.Conflict(new { error = "..." })` for already-verified guard.
- **PASS** — `CancellationToken` propagated to all `FirstOrDefaultAsync`, `CountAsync`, `AnyAsync`, `ToListAsync`, `SaveChangesAsync`, and `cache.*Async` calls.

### Data Access

- **PASS** — `Get360ViewEndpoint` issues 4 discrete queries on cache miss: (1) `FirstOrDefaultAsync` on `UserAccounts`, (2) `ToListAsync` on `ExtractedClinicalFields`, (3) `CountAsync` on `ConflictFlags`, (4) `AnyAsync` on `ClinicalDocuments`. No N+1 risk; all are single-table bounded queries.
- **PASS** — `ClinicalDbContext` `HasQueryFilter(f => !f.IsDeleted)` on `ExtractedClinicalFields` is automatically applied — soft-deleted field rows are excluded from the 360° view without requiring explicit `.Where(!f.IsDeleted)` in the handler.
- **PASS** — The `UploadDocumentEndpoint` cache invalidation is placed *after* `db.SaveChangesAsync(ct)` and before `Results.Created(...)`, ensuring the document row is committed before the cache key is evicted. No eviction on a rolled-back transaction.
- **ACCEPTABLE** — The assembled `PatientView360Dto` is stored as a single Redis value. For patients with very large numbers of extracted fields, JSON serialization size may become significant. No pagination is implemented in the 360° view, which is acceptable given the bounded cardinality of clinical fields per patient.

### Patterns & Standards

- **PASS** — Vertical-slice `IEndpointDefinition` pattern applied consistently: empty `AddServices`, static handler, `RequireAuthorization`, `WithName`, `WithTags`, `Produces`.
- **PASS** — `CacheKeyPrefix = "view360:"` declared as `internal const string` in `Get360ViewEndpoint` and referenced by both `VerifyPatientEndpoint` and `UploadDocumentEndpoint` via `Get360ViewEndpoint.CacheKeyPrefix` — single source of truth (DRY).
- **PASS** — `sealed class` + `public static` handler aligns with codebase conventions established in TASK_038–043.
- **GAP (LOW — L-001) ✅ FIXED** — `ILogger<Get360ViewEndpoint>` removed from `HandleGet360View` handler signature and from all 6 test call sites during this review. Unused `using Microsoft.Extensions.Logging.Abstractions` also removed from test file.

---

## Test Review

**Existing Tests (12 new, 550 total, all passing):**

| Test Class | Test | Coverage |
|---|---|---|
| `Get360ViewEndpointTests` | `Get360View_PatientNotFound_Returns404` | 404 guard on missing patient |
| `Get360ViewEndpointTests` | `Get360View_CacheHit_ReturnsCachedView` | Cache hit early-return + SetAsync never called |
| `Get360ViewEndpointTests` | `Get360View_CacheMiss_AssemblesFromDb_And_Caches` | Full assembly path + SetAsync with correct TTL |
| `Get360ViewEndpointTests` | `Get360View_NoDocuments_SetsHint` | Hint populated when no ClinicalDocuments |
| `Get360ViewEndpointTests` | `Get360View_HasDocuments_NoHint` | Hint null when document exists |
| `Get360ViewEndpointTests` | `Get360View_UnresolvedConflicts_ReflectedInCount` | unresolvedCount assembled from ConflictFlags |
| `VerifyPatientEndpointTests` | `VerifyPatient_Returns401_WhenSubClaimMissing` | JWT sub absent → 401 |
| `VerifyPatientEndpointTests` | `VerifyPatient_Returns404_WhenPatientNotFound` | Missing patient → 404 |
| `VerifyPatientEndpointTests` | `VerifyPatient_Returns409_WhenUnresolvedConflicts` | Conflict guard → 409 |
| `VerifyPatientEndpointTests` | `VerifyPatient_Returns409_WhenAlreadyVerified` | Already-verified guard → 409 |
| `VerifyPatientEndpointTests` | `VerifyPatient_SetsVerifiedStatus_AndReturns200` | Full success path + entity state assertions |
| `VerifyPatientEndpointTests` | `VerifyPatient_InvalidatesCacheOnSuccess` | `DeleteAsync("view360:5")` called on success |

**Missing Tests (recommended):**

- [ ] `Upload_ValidPdf_InvalidatesView360Cache` — verify `cache.DeleteAsync("view360:{patientId}")` called after successful upload in `UploadDocumentEndpointTests`. AC-005 is covered by implementation inspection but has no direct test assertion.
- [ ] `Get360View_CacheHit_DoesNotQueryDb` — explicitly seed no patient in sqlDb and verify the cache-hit path returns 200 without a 404, confirming DB is truly bypassed.
- [ ] `VerifyPatient_SubClaimNonInteger_Returns401` — verify that a non-integer `sub` value (e.g., `"abc"`) returns 401.

---

## Validation Results

**Commands Executed:**

```bash
dotnet build --no-incremental -c Debug
dotnet test --no-build -c Debug
```

**Outcomes:**

| Command | Result |
|---|---|
| `dotnet build` | ✅ Build succeeded — 0 Error(s), 0 Warning(s) |
| `dotnet test` | ✅ 550 passed, 0 failed (537 Infrastructure + 13 Api) |

---

## Fix Plan (Prioritized)

| # | Severity | Finding | File | Action | Risk |
|---|---|---|---|---|---|
| 1 | MEDIUM ✅ FIXED | M-004: Missing `Produces(403)` on `Get360ViewEndpoint` | `Get360ViewEndpoint.cs:39` | Added `.Produces(StatusCodes.Status403Forbidden)` — done in this review | Low |
| 2 | MEDIUM | M-003: `VerifyPatientEndpoint` bypasses `IConflictService` | `VerifyPatientEndpoint.cs`, `IConflictService.cs` | Option A: extend interface with `GetUnresolvedCountAsync` returning `Task<int>` and use it; Option B: document deviation as ADR | Medium |
| 3 | LOW ✅ FIXED | L-001: Dead `logger` injection in `HandleGet360View` | `Get360ViewEndpoint.cs:50`, `Get360ViewEndpointTests.cs` | Removed `ILogger<Get360ViewEndpoint> logger` param + `NullLogger` from 6 test call sites — done in this review | Low |
| 4 | LOW | L-002: No test for upload-triggered cache invalidation | `UploadDocumentEndpointTests.cs` | Add `Upload_ValidPdf_InvalidatesView360Cache` test asserting `DeleteAsync` called | Low |

---

## Appendix

### Search Evidence

| Pattern | File | Purpose |
|---|---|---|
| `CacheKeyPrefix` | `Get360ViewEndpoint.cs:28` | Single-source cache key constant |
| `FieldType` | `ExtractedClinicalField.cs:28` | Confirmed property exists for grouping |
| `HasQueryFilter(f => !f.IsDeleted)` | `ClinicalDbContext.cs:42` | Soft-delete auto-applied to fields query |
| `Produces(StatusCodes.Status403Forbidden)` | `VerifyPatientEndpoint.cs:44` | Present on verify; absent on 360-view |
| `cache.DeleteAsync` | `UploadDocumentEndpoint.cs:144`, `VerifyPatientEndpoint.cs:78` | Both invalidation points confirmed |
