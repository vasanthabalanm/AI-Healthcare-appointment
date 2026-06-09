---
task_id: task_031
us_id: us_031
epic: EP-004
review_date: 2026-05-18
reviewer: GitHub Copilot
verdict: Conditional Pass
---

# Implementation Analysis — `.propel/context/tasks/EP-004/us_031/task_031_intake-editing-version-history.md`

## Verdict

**Status:** Conditional Pass
**Summary:** TASK_031 implements all 5 acceptance criteria and both documented edge cases. `PATCH /intake/{intakeGroupId}` correctly creates a new versioned `IntakeRecord` (demoting the prior `IsLatest=false`), detects no-op patches, enforces patient ownership via JWT, and returns 422 on field-length violations. `GET /intake/{intakeGroupId}[?version=N]` returns the latest version by default (via EF Core default query filter) and any historical version via `IgnoreQueryFilters()`. Twelve unit tests cover all ACs and guard branches. Four findings are raised: one medium (F1 — no patient ownership check on `GET`; any patient-role caller can read any intake by GUID) and three low (F2 — empty-string ChiefComplaint patch creates a clinically ambiguous version; F3 — no PATCH MaxLength tests for optional fields; F4 — no GET patient-isolation test).

---

## Traceability Matrix

| Requirement / Acceptance Criterion | Evidence (file : fn / line) | Result |
|---|---|---|
| AC-001: PATCH creates new IntakeRecord version | `UpdateIntakeEndpoint.cs` : `HandleUpdateIntake()` L108–128 | Pass |
| AC-001: Prior version `IsLatest=false` | `UpdateIntakeEndpoint.cs` L108 `current.IsLatest = false` | Pass |
| AC-001: Sequential `Version = current.Version + 1` | `UpdateIntakeEndpoint.cs` L115 | Pass |
| AC-002: GET without param returns latest | `GetIntakeEndpoint.cs` : `HandleGetIntake()` L55–58 | Pass |
| AC-002: Default EF query filter (`IsLatest=true`) used | `GetIntakeEndpoint.cs` L56 — no `IgnoreQueryFilters()` | Pass |
| AC-003: GET `?version=N` uses `IgnoreQueryFilters()` | `GetIntakeEndpoint.cs` L49–52 | Pass |
| AC-003: 404 on missing version | `GetIntakeEndpoint.cs` L64 | Pass |
| AC-004: No-op PATCH returns 200 without new row | `UpdateIntakeEndpoint.cs` L97–104 | Pass |
| AC-004: Null fields treated as "no change" | `UpdateIntakeEndpoint.cs` L91–95 (null-coalescing merge) | Pass |
| AC-005: Prior versions not targetable by PATCH | Default query filter loads only `IsLatest=true` | Pass |
| Edge case: Subset PATCH — unchanged fields copied | Null-coalescing merge (`??`) for all 4 fields | Pass |
| Edge case: Sequential version numbers, no gaps | `Version = current.Version + 1` — test confirms 1→2→3 | Pass |
| Patient ownership on PATCH | `UpdateIntakeEndpoint.cs` L86–88 → 403 Forbid | Pass |
| Unauthenticated PATCH → 401 | `UpdateIntakeEndpoint.cs` L62–63 | Pass |
| `RequireAuthorization("PatientOnly")` on PATCH | `UpdateIntakeEndpoint.cs` L44 | Pass |
| `RequireAuthorization("IntakeReader")` on GET | `GetIntakeEndpoint.cs` L40 | Pass |
| Convention test (all routes have auth) | 13 Api.Tests pass | Pass |
| `dotnet build` 0 errors | Terminal: `Build succeeded. 0 Error(s)` | Pass |

---

## Logical & Design Findings

### Business Logic

- **F1 — MEDIUM:** `GET /intake/{intakeGroupId}` applies the `IntakeReader` policy (patient, staff, admin) but performs no patient-ownership check for patient-role callers. Any authenticated patient who knows — or guesses — another patient's `IntakeGroupId` (a GUID, but still enumerable with effort) can read that patient's full intake record. Staff and admin cross-patient access is correct by design; patient-role callers should only read their own records. Fix: extract `sub` claim in `HandleGetIntake`; if the caller has only the `patient` role, assert `record.PatientId == patientId`. This is an **OWASP A01** (Broken Access Control) gap.

- **F2 — LOW:** Sending `ChiefComplaint = ""` (empty string) in a PATCH request is treated as an intentional update (since `""` differs from the seeded value). After trim it remains `""`. No minimum-length validation on `UpdateIntakeRequest.ChiefComplaint` means a patient can create a new version with a blank chief complaint. Consider `[MinLength(1)]` or treating `""` as `null` (no change). Not blocking — editing to clear a field may be intentional in some workflows.

- **F3 — LOW:** The no-op comparison uses ordinal string equality (`!=`). For clinical text this is correct: "headache" and "Headache" are genuinely different values and should create a new version. Documented for awareness.

### Security

- **OWASP A01 ✅ (PATCH):** PatientId from JWT; ownership guard returns 403 on mismatch.
- **OWASP A01 ⚠️ (GET):** No per-patient ownership guard for patient-role callers — see F1.
- **OWASP A03 ✅:** All DB queries via EF Core LINQ; no raw SQL.
- **OWASP A04 ✅:** All fields bounded by `[MaxLength]` on both DTOs.

### Error Handling

- PATCH: 401 (no JWT), 422 (field too long), 404 (unknown group), 403 (wrong patient), 200 (no-op or new version) — full coverage. ✅
- GET: 200 (found), 404 (not found or wrong version) — complete. ✅

### Data Access

- `IsLatest` default query filter correctly scopes both the GET-latest and the PATCH load to the current version. ✅
- `IgnoreQueryFilters()` used only for historical version query — not leaked to other paths. ✅
- `IsDeleted` and `RetainUntil` default to `false`/`null` on new version rows — correct for a live record. ✅
- Single `SaveChangesAsync` persists both `current.IsLatest = false` and the new row atomically (within the EF Core change-tracking unit of work). ✅

### Patterns & Standards

- `IEndpointDefinition` pattern: `AddServices` + `MapEndpoints`. ✅
- `public static async Task<IResult>` handler — directly unit-testable. ✅
- `PatientOnly` null-guard re-registration consistent with project convention. ✅
- `IntakeReader` policy registered by `GetIntakeEndpoint.AddServices` — new policy, correctly scoped. ✅

---

## Test Review

### Existing Tests (12 total — all pass)

| Test | AC / Guard Covered | Result |
|---|---|---|
| `UpdateIntake_ChangedValues_CreatesNewVersionAndMarksOldNotLatest` | AC-001 (full row assertion) | ✅ Pass |
| `UpdateIntake_SequentialVersions_IncrementByOne` | AC-001 (version sequence 1→2→3) | ✅ Pass |
| `UpdateIntake_SameValues_ReturnsOkWithNoOp_NoNewVersion` | AC-004 (no-op all fields) | ✅ Pass |
| `UpdateIntake_NullFields_TreatedAsNoChange` | AC-004 / Edge case (subset PATCH) | ✅ Pass |
| `UpdateIntake_WrongPatient_Returns403` | AC-005 / ownership | ✅ Pass |
| `UpdateIntake_UnknownGroupId_Returns404` | Guard — not found | ✅ Pass |
| `UpdateIntake_NoJwtSub_Returns401` | Guard — unauthenticated | ✅ Pass |
| `UpdateIntake_ChiefComplaintTooLong_Returns422` | Validation — MaxLength | ✅ Pass |
| `GetIntake_NoVersionParam_ReturnsLatest` | AC-002 (latest) | ✅ Pass |
| `GetIntake_WithVersionParam_ReturnsHistoricalVersion` | AC-003 (historical v1) | ✅ Pass |
| `GetIntake_NonExistentVersion_Returns404` | AC-003 (version not found) | ✅ Pass |
| `GetIntake_UnknownGroupId_Returns404` | Guard — not found | ✅ Pass |

### Missing Tests (should add after findings are fixed)

- [ ] **F1 — MEDIUM:** `GetIntake_PatientRole_CannotReadOtherPatientIntake` — verify a patient-role caller receives 403 when accessing another patient's `IntakeGroupId`.
- [ ] **F2 — LOW:** `UpdateIntake_EmptyStringChiefComplaint_CreatesNewVersion` or `_Returns422` — document/enforce intended behavior for empty-string PATCH.
- [ ] **F3 — LOW:** `UpdateIntake_CurrentMedsTooLong_Returns422` — MaxLength on optional PATCH field.
- [ ] **F3 — LOW:** `UpdateIntake_AllergiesTooLong_Returns422` — MaxLength on optional PATCH field.

---

## Validation Results

**Commands Executed:**

```bash
dotnet build
dotnet test
```

**Outcomes:**

```text
Build succeeded. 0 Warning(s). 0 Error(s).
Passed! - Failed: 0, Passed: 378, Skipped: 0, Total: 378
  ClinicalHealthcare.Api.Tests:            13 passed
  ClinicalHealthcare.Infrastructure.Tests: 365 passed
```

---

## Fix Plan (Prioritized)

| # | Finding | Fix | Files | Risk |
|---|---|---|---|---|
| 1 | **F1 — MEDIUM: No patient ownership guard on GET** | In `HandleGetIntake`, extract `sub` claim; if role is `patient`, assert `record.PatientId == callerPatientId` → 403 on mismatch. Staff/admin callers skip the ownership check | `GetIntakeEndpoint.cs` | M |
| 2 | **F2 — LOW: Empty-string ChiefComplaint PATCH creates ambiguous version** | Treat `ChiefComplaint = ""` as `null` (no change) in the merge step, or add `[MinLength(1)]` to `UpdateIntakeRequest.ChiefComplaint` with a clear error message | `UpdateIntakeEndpoint.cs` or `UpdateIntakeRequest.cs` | L |
| 3 | **F3 — LOW: Missing optional-field MaxLength tests for PATCH** | Add `UpdateIntake_CurrentMedsTooLong_Returns422` and `UpdateIntake_AllergiesTooLong_Returns422` tests | `IntakeVersioningEndpointTests.cs` | L |
| 4 | **F4 — LOW: No GET patient-isolation test** | Add `GetIntake_PatientRole_CannotReadOtherPatientIntake` (pending F1 fix) | `IntakeVersioningEndpointTests.cs` | L |

---

## Appendix

### Rules Applied

- `rules/security-standards-owasp.md` — OWASP A01 review (F1 finding)
- `rules/backend-development-standards.md` — service/endpoint patterns
- `rules/dotnet-architecture-standards.md` — vertical-slice `IEndpointDefinition`
- `rules/language-agnostic-standards.md` — KISS, naming
- `rules/code-anti-patterns.md` — null-coalescing merge correctness
- `rules/dry-principle-guidelines.md` — null-guard policy registration
- `rules/markdown-styleguide.md` — formatting

### Search Evidence

```text
grep: IgnoreQueryFilters — GetIntakeEndpoint.cs L49 (historical query only)
grep: RequireAuthorization("PatientOnly") — UpdateIntakeEndpoint.cs L44
grep: RequireAuthorization("IntakeReader") — GetIntakeEndpoint.cs L40
grep: current.IsLatest = false — UpdateIntakeEndpoint.cs L108
grep: current.Version + 1 — UpdateIntakeEndpoint.cs L115
grep: PatientId != patientId — UpdateIntakeEndpoint.cs L87 (PATCH only — not GET)
grep: FromSqlRaw — 0 matches in both new endpoint files
```
