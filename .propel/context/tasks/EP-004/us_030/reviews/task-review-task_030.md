---
task_id: task_030
us_id: us_030
epic: EP-004
review_date: 2026-05-18
reviewer: GitHub Copilot
verdict: Pass
---

# Implementation Analysis — `.propel/context/tasks/EP-004/us_030/task_030_manual-intake-form-submission.md`

## Verdict

**Status:** Pass
**Summary:** TASK_030 delivers `POST /intake/manual` with all four acceptance criteria met. The DTO applies data-annotation validation returning 422 on errors, the duplicate-intake guard returns 409 on an active `IsLatest=true` record, all DB operations use EF Core parameterised queries, and the Patient ID is bound exclusively from the JWT `sub` claim. Nine unit tests cover the happy path, optional nulls, required-field and max-length violations, duplicate detection, archival edge case, unauthenticated access, and cross-patient isolation. Three minor gaps are identified: whitespace-only `ChiefComplaint` is not rejected (F1, MEDIUM), optional field `MaxLength` violations are not unit-tested (F2, LOW), and concurrent duplicate submissions have no DB-level uniqueness guard (F3, LOW — accepted by spec).

---

## Traceability Matrix

| Requirement / Acceptance Criterion | Evidence (file : fn / line) | Result |
|---|---|---|
| AC-001: `POST /intake/manual` creates `IntakeRecord` with `Source=Manual`; returns 201 | `SubmitManualIntakeEndpoint.cs` : `HandleSubmitManualIntake()` L88–103 | Pass |
| AC-001: `IntakeGroupId=Guid.NewGuid()`, `Version=1`, `IsLatest=true` | `SubmitManualIntakeEndpoint.cs` L90–95 | Pass |
| AC-002: Field validation → 422 with per-field `ValidationProblemDetails` | `SubmitManualIntakeEndpoint.cs` L65–77; `ManualIntakeRequest.cs` L15–30 | Pass |
| AC-002: `[Required(AllowEmptyStrings=false)]` on `ChiefComplaint` | `ManualIntakeRequest.cs` L16 | Pass |
| AC-002: `[MaxLength]` on all string fields | `ManualIntakeRequest.cs` L17, 23, 27, 31 | Pass |
| AC-003: Existing `IsLatest=true` intake → 409 | `SubmitManualIntakeEndpoint.cs` L80–84 | Pass |
| AC-003: Archived (`IsLatest=false`) record does not block | `SubmitManualIntakeEndpoint.cs` L79 (EF default filter) | Pass |
| AC-004: No `FromSqlRaw` / `ExecuteSqlRaw` interpolation | `SubmitManualIntakeEndpoint.cs` — LINQ only, structural test confirms | Pass |
| Patient ID from JWT `sub` claim, not request body | `SubmitManualIntakeEndpoint.cs` L58–62 | Pass |
| `RequireAuthorization("PatientOnly")` on route | `SubmitManualIntakeEndpoint.cs` L41 | Pass |
| `dotnet build` passes with 0 errors | Terminal: `Build succeeded. 0 Warning(s). 0 Error(s).` | Pass |
| OWASP A01: PatientId not accepted from body | `ManualIntakeRequest.cs` — no `PatientId` property | Pass |
| OWASP A03: EF Core parameterised queries throughout | LINQ `.AnyAsync()` + entity property assignment only | Pass |

---

## Logical & Design Findings

### Business Logic

- **F1 — MEDIUM:** `[Required(AllowEmptyStrings = false)]` rejects `null` and `""` but **allows whitespace-only strings** (e.g. `"   "`). A patient can submit `ChiefComplaint = "   "`, pass validation, and create a clinically meaningless record. The fix is to either trim the value before validation or add a `[MinLength(1)]` + custom trimming step.

- **F2 — LOW:** The duplicate-intake check (`AnyAsync(r => r.PatientId == patientId && r.IsLatest, ct)`) is redundant with the EF Core default query filter `HasQueryFilter(r => r.IsLatest)` already registered on `IntakeRecord`. The explicit `&& r.IsLatest` in the LINQ predicate is harmless but adds noise. `db.IntakeRecords.AnyAsync(r => r.PatientId == patientId, ct)` would be equivalent. Minor clarity issue only.

- **F3 — LOW:** Concurrent duplicate submissions are not protected at the database level. Two simultaneous requests can both pass the `AnyAsync` check before either `SaveChangesAsync` commits. The task explicitly scopes this as "application-level duplicate check", making a DB unique constraint out of scope. This is a **known accepted limitation** per the spec, not a defect.

### Security

- **OWASP A01 ✅:** `PatientId` is sourced from the JWT `sub` claim only; the request DTO has no `PatientId` field. `RequireAuthorization("PatientOnly")` enforces role-based access.
- **OWASP A03 ✅:** All DB interaction uses EF Core LINQ — no raw SQL interpolation anywhere in the handler or DTO. Structural test confirms.
- **OWASP A04 ✅:** All string fields are bounded by `[MaxLength]` attributes (1 000 / 2 000 / 2 000 / 4 000 chars). A 413 response would be returned by the framework for oversized JSON bodies before the endpoint handler fires.

### Error Handling

- Valid request → 201 Created with `Location: /intake/{id}` ✅
- Invalid fields → 422 `ValidationProblemDetails` with per-field error arrays ✅
- Duplicate active intake → 409 Conflict with `{ "error": "..." }` ✅
- Unauthenticated / non-parseable JWT sub → 401 ✅
- No `try/catch` around `SaveChangesAsync` — DB transient faults surface as 500. Acceptable for Minimal API pattern; retry is responsibility of the caller. ✅

### Data Access

- `IsLatest` default query filter is active on `IntakeRecords`. The LINQ predicate `&& r.IsLatest` is therefore redundant (see F2) but not wrong.
- `SubmittedAt = DateTime.UtcNow` uses server time — correct for audit purposes. ✅
- `IsDeleted` defaults to `false`, `RetainUntil` to `null` — correct for new records. ✅

### Performance

- Single `AnyAsync` + single `SaveChangesAsync` — 2 round trips per request. Optimal. ✅

### Patterns & Standards

- Vertical-slice `IEndpointDefinition` pattern followed correctly. `AddServices` registers `PatientOnly` with null-guard (consistent with all other patient endpoints). ✅
- `HandleSubmitManualIntake` is `public static` — consistent with project convention for direct unit test invocation. ✅
- `ManualIntakeRequest` is a `sealed record` with `init` setters — immutable, JSON-deserialisable. ✅

---

## Test Review

### Existing Tests (9 total — all pass)

| Test | AC / Guard Covered | Result |
|---|---|---|
| `SubmitManual_Valid_ReturnsCreatedAndPersistsRecord` | AC-001 (full record assertion) | ✅ Pass |
| `SubmitManual_OptionalFieldsNull_ReturnsCreated` | AC-001 (optional nulls) | ✅ Pass |
| `SubmitManual_MissingChiefComplaint_Returns422` | AC-002 (Required) | ✅ Pass |
| `SubmitManual_ChiefComplaintTooLong_Returns422` | AC-002 (MaxLength) | ✅ Pass |
| `SubmitManual_ExistingActiveIntake_Returns409` | AC-003 (duplicate) | ✅ Pass |
| `SubmitManual_PriorRecordNotLatest_ReturnsCreated` | AC-003 (archived allowed) | ✅ Pass |
| `SubmitManual_EndpointFile_ContainsNoFromSqlRawInterpolation` | AC-004 (structural) | ✅ Pass |
| `SubmitManual_NoJwtSub_Returns401` | Auth guard | ✅ Pass |
| `SubmitManual_DifferentPatients_BothSucceed` | Patient isolation | ✅ Pass |

### Missing Tests (should add)

- [ ] **Unit — F1:** `SubmitManual_WhitespaceChiefComplaint_Returns422` — verify whitespace-only string is rejected once F1 is fixed.
- [ ] **Unit — AC-002:** `SubmitManual_CurrentMedsTooLong_Returns422` — MaxLength on optional field.
- [ ] **Unit — AC-002:** `SubmitManual_AllergiesValueErrorsContainFieldName_Returns422` — verify per-field key in `ValidationProblemDetails` errors dictionary matches field name.

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
Passed! - Failed: 0, Passed: 363, Skipped: 0, Total: 363
```

---

## Fix Plan (Prioritized)

| # | Finding | Fix | Files | Risk |
|---|---|---|---|---|
| 1 | **F1 — Whitespace-only ChiefComplaint accepted** | Trim `request.ChiefComplaint` before validation, or add `[RegularExpression(@"\S+.*", ErrorMessage = "...")]` to reject all-whitespace strings | `ManualIntakeRequest.cs`, `SubmitManualIntakeEndpoint.cs` | M |
| 2 | **F2 — Redundant `&& r.IsLatest` in LINQ** | Remove the explicit `&& r.IsLatest` from the `AnyAsync` predicate; rely on the EF Core default query filter | `SubmitManualIntakeEndpoint.cs` L79 | L |
| 3 | **Missing optional-field MaxLength tests** | Add `CurrentMedsTooLong_Returns422` and `AllergiesErrorContainsFieldName` tests | `SubmitManualIntakeEndpointTests.cs` | L |

---

## Appendix

### Rules Applied

- `rules/security-standards-owasp.md` — OWASP A01, A03, A04 review
- `rules/backend-development-standards.md` — service/endpoint patterns
- `rules/dotnet-architecture-standards.md` — vertical-slice `IEndpointDefinition`
- `rules/language-agnostic-standards.md` — KISS, naming, size
- `rules/code-anti-patterns.md` — redundant predicate (F2)
- `rules/dry-principle-guidelines.md` — redundancy review

### Search Evidence

```text
grep: FromSqlRaw — 0 matches in SubmitManualIntakeEndpoint.cs (confirmed by structural test)
grep: RequireAuthorization("PatientOnly") — SubmitManualIntakeEndpoint.cs L41
grep: IntakeSource.Manual — SubmitManualIntakeEndpoint.cs L89
grep: HasQueryFilter — ApplicationDbContext.cs (IsLatest default filter confirmed)
grep: AllowEmptyStrings = false — ManualIntakeRequest.cs L16
```
