---
task_id: task_010
us_id: us_010
epic: EP-DATA
reviewed_on: 2026-05-14
reviewer: analyze-implementation (automated)
verdict: Conditional Pass
---

# Implementation Analysis — task_010_clinical-pg-entities-migrations.md

## Verdict

**Status:** Conditional Pass
**Summary:** All five acceptance criteria are satisfied with DDL-level evidence. Three entities are correctly created and registered in `ClinicalDbContext`. The Trust-First CHECK constraint (`"Status" != 1 OR "VerifiedById" IS NOT NULL`) and `ConfidenceScore` bounds CHECK (`"ConfidenceScore" >= 0.0 AND "ConfidenceScore" <= 1.0`) are confirmed in the applied PostgreSQL migration. 59/59 tests pass. One HIGH-priority gap exists: `ExtractedClinicalField` has an `IsDeleted` soft-delete flag but **no `HasQueryFilter`** in `ClinicalDbContext` — every default query will return soft-deleted records, which is a bug that will surface the moment the entity is queried in a service layer. `IntakeRecord` sets the precedent for `HasQueryFilter` in this project. Fix F1 must be applied inline before this task reaches full Pass.

---

## Traceability Matrix

| Requirement / Acceptance Criterion | Evidence (file : line) | Result |
|---|---|---|
| AC-001 — `ExtractedClinicalField` in PostgreSQL via `ClinicalDbContext` | `ExtractedClinicalField.cs` all fields; `ClinicalDbContext.cs` L12 DbSet; migration `CreateTable("ExtractedClinicalFields")` | **Pass** |
| AC-002 — `ConflictFlag` with `Unresolved/Resolved/Dismissed` | `ConflictFlag.cs` `ConflictFlagStatus` enum (0/1/2); `ClinicalDbContext.cs` L13; migration `CreateTable("ConflictFlags")` | **Pass** |
| AC-003 — `MedicalCodeSuggestion` with all required fields | `MedicalCodeSuggestion.cs` — all 11 fields confirmed; `ClinicalDbContext.cs` L14; migration all columns present | **Pass** |
| AC-004 — Trust-First CHECK: `verified_by IS NOT NULL` when `status = Accepted` | `ClinicalDbContext.cs` L83: `"Status" != 1 OR "VerifiedById" IS NOT NULL`; migration L90: `CheckConstraint("CK_MedicalCodeSuggestions_TrustFirst", ...)` | **Pass** |
| AC-005 — `confidence_score CHECK (0.0 ≤ x ≤ 1.0)` | `ExtractedClinicalField` migration L56: `CheckConstraint("CK_ExtractedClinicalFields_ConfidenceScore", ...)`; `MedicalCodeSuggestion` migration L88: `CheckConstraint("CK_MedicalCodeSuggestions_ConfidenceScore", ...)` | **Pass** |
| Edge — `verified_by` FK nullable | `MedicalCodeSuggestion.VerifiedById` is `int?`; verified by reflection test `MedicalCodeSuggestion_VerifiedById_IsNullableInt` | **Pass** |
| Additional — All entities target PostgreSQL via `ClinicalDbContext` | `ClinicalDbContext.cs` L12–14 three DbSets; `ClinicalDbContextMigrationFactory.cs` uses `UseNpgsql` | **Pass** |
| Additional — Migration created and applies cleanly | `20260514081740_ClinicalEntities.cs` exists; terminal: `Done. INSERT INTO "__EFMigrationsHistory"` | **Pass** |
| Additional — `dotnet build` 0 errors | Build output: `Build succeeded. 0 Error(s)` | **Pass** |
| Additional — Tests pass | `dotnet test` → `Passed: 59, Failed: 0` | **Pass** |

---

## Logical & Design Findings

- **Business Logic — HIGH — `ExtractedClinicalField` missing `HasQueryFilter` for soft-delete:** `IsDeleted` is correctly added as a soft-delete flag (`HasDefaultValue(false)`) but there is no `HasQueryFilter(f => !f.IsDeleted)` in `ClinicalDbContext.OnModelCreating`. Without it, every call to `ctx.ExtractedClinicalFields.Where(...)` returns soft-deleted records alongside live ones. The established pattern in this codebase (`IntakeRecord` uses `HasQueryFilter(r => r.IsLatest)`) proves the intent — this is an omission, not a design choice. Fix: add `e.HasQueryFilter(f => !f.IsDeleted)` and a supporting test.

- **Business Logic — MEDIUM — `ConflictFlag` has no Trust-First analogous CHECK:** `ConflictFlag.ResolvedByStaffId` is nullable. When a conflict is `Resolved` or `Dismissed`, the staff member who acted should be recorded. No CHECK constraint enforces `"Status" = 0 OR "ResolvedByStaffId" IS NOT NULL`. This was not specified in the task's ACs, but is the logical analogue of AC-004 for `MedicalCodeSuggestion`. Recommend adding as a follow-up task.

- **Design — OK — Cross-database FK pattern:** `PatientId`, `VerifiedById`, `ResolvedByStaffId` reference `UserAccount` in SQL Server but these entities live in PostgreSQL. No referential integrity is possible across database engines. Application-layer enforcement is the correct pattern; documented in entity XML comments. ✅

- **Design — OK — Column naming:** Npgsql without `UseSnakeCaseNamingConvention()` generates PascalCase column names. CHECK constraints correctly use double-quoted PascalCase identifiers (`"ConfidenceScore"`, `"Status"`, `"VerifiedById"`). This was a runtime fix applied during implementation. ✅

- **Design — OK — `ExtractionJobId` as `string`:** Flexible for external job IDs from different orchestration systems (Hangfire, Azure Service Bus, etc). Not a `Guid` by design decision. ✅

- **Security — LOW — No application-level input validation on `SuggestedCode` / `FieldValue`:** These will contain AI pipeline output. Without sanitization at the service layer, injection-adjacent risk exists (OWASP A03). Not in task scope; flag for service layer implementation.

- **Performance — OK:** `PatientId` indexed on all three tables; `DocumentId` indexed on `ExtractedClinicalField` for document-scoped queries. Primary access patterns covered. ✅

---

## Test Review

### Existing Tests (12 test methods, 17 runtime cases via Theory)

| Test | Covers | Status |
|---|---|---|
| `ExtractedClinicalField_CanBeInserted_WithAllFields` | AC-001: full insert roundtrip | ✅ Pass |
| `ExtractedClinicalField_IsDeleted_DefaultsFalse` | Soft-delete default | ✅ Pass |
| `ClinicalFieldType_AllEnumValues_AreDefined` [×5] | All 5 enum values defined | ✅ Pass |
| `ExtractedClinicalField_QueryByDocumentId_ReturnsCorrectFields` | DocumentId query isolation | ✅ Pass |
| `ConflictFlag_DefaultStatus_IsUnresolved` | AC-002: default state | ✅ Pass |
| `ConflictFlag_StatusCanBeUpdated` [×2: Resolved, Dismissed] | AC-002: transitions | ✅ Pass |
| `MedicalCodeSuggestion_DefaultStatus_IsPending` | AC-003: default state | ✅ Pass |
| `MedicalCodeSuggestion_AcceptedStatus_WithVerifiedBy_Succeeds` | AC-004: happy path | ✅ Pass |
| `MedicalCodeSuggestion_LowConfidenceFlag_CanBeSet` | LowConfidenceFlag property | ✅ Pass |
| `MedicalCodeSuggestion_ConfidenceScore_IsDouble` | AC-005: entity contract | ✅ Pass |
| `ExtractedClinicalField_ConfidenceScore_IsDouble` | AC-005: entity contract | ✅ Pass |
| `MedicalCodeSuggestion_VerifiedById_IsNullableInt` | AC-004: nullable contract | ✅ Pass |

### Missing Tests (gaps)

- [ ] **Unit (HIGH):** `ExtractedClinicalField_SoftDeleted_ExcludedByDefaultQuery` — after adding `HasQueryFilter`, verify soft-deleted records are excluded from default queries and returned with `IgnoreQueryFilters()`
- [ ] **Unit (MEDIUM):** `MedicalCodeSuggestion_AcceptedWithoutVerifiedBy_InMemoryAllows_PostgresWouldReject` — document InMemory limitation, negative test noting CHECK enforcement is DB-level only
- [ ] **Unit (MEDIUM):** `MedicalCodeSuggestion_StatusTransitions` [Theory: Modified, Rejected] — cover remaining `SuggestionStatus` enum values
- [ ] **Unit (MEDIUM):** `ConflictFlag_QueryByPatientId_ReturnsOnlyPatientFlags` — multi-patient isolation test
- [ ] **Unit (LOW):** `ExtractedClinicalField_SoftDelete_CanBeMarkedDeleted` — verify `IsDeleted = true` persists correctly

---

## Validation Results

**Commands Executed:**

```powershell
dotnet build ClinicalHealthcare.slnx
dotnet ef migrations add ClinicalEntities ...
dotnet ef database update ...
dotnet test tests/ClinicalHealthcare.Infrastructure.Tests/...csproj
```

**Outcomes:**

| Command | Outcome |
|---|---|
| `dotnet build` | ✅ `Build succeeded. 0 Error(s)` |
| `dotnet ef migrations add ClinicalEntities` (attempt 1) | ❌ Build fail — `HasCheckConstraint` obsolete in EF Core 8; fixed to `ToTable(t => t.HasCheckConstraint())` |
| `dotnet ef migrations add ClinicalEntities` (attempt 2) | ✅ Migration generated |
| `dotnet ef database update` (attempt 1) | ❌ PG auth fail (wrong password); ❌ PG column name error (`confidence_score` not found — PascalCase `"ConfidenceScore"` required); fixed |
| `dotnet ef database update` (final) | ✅ `Done. INSERT INTO "__EFMigrationsHistory" ('20260514081740_ClinicalEntities')` |
| `dotnet test` | ✅ `Passed: 59, Failed: 0, Skipped: 0` |

**Implementation fixes applied during execution:**
1. `HasCheckConstraint` → `ToTable(t => t.HasCheckConstraint())` (EF Core 8 API change)
2. CHECK SQL column names changed from `confidence_score` to `"ConfidenceScore"` (Npgsql PascalCase convention without snake_case config)
3. Added `PgMigrations` project reference to API `.csproj` (missing, same pattern as SQL migrations)

---

## Fix Plan (Prioritized)

| # | Fix | File(s) | Priority | Risk |
|---|---|---|---|---|
| F1 | Add `e.HasQueryFilter(f => !f.IsDeleted)` to `ExtractedClinicalField` fluent config; add `SoftDeleted_ExcludedByDefaultQuery` and `SoftDeleted_VisibleWithIgnoreQueryFilters` tests | `ClinicalDbContext.cs`; `ClinicalPgEntitiesTests.cs` | **HIGH** — apply inline | L |
| F2 | Add Trust-First CHECK to `ConflictFlag`: `"Status" = 0 OR "ResolvedByStaffId" IS NOT NULL`; add migration; add test | `ClinicalDbContext.cs`; new migration; `ClinicalPgEntitiesTests.cs` | **MEDIUM** — follow-up task | M |
| F3 | Add `SuggestionStatus.Modified` and `.Rejected` transition tests; add `ConflictFlag` patient isolation test | `ClinicalPgEntitiesTests.cs` | **MEDIUM** — test-only | L |
| F4 | Add `ExtractedClinicalField_SoftDelete_CanBeMarkedDeleted` test | `ClinicalPgEntitiesTests.cs` | **LOW** — test-only | L |

---

## Appendix

### Rules Applied

- `rules/ai-assistant-usage-policy.md` — directive compliance
- `rules/dry-principle-guidelines.md` — HasQueryFilter precedent from IntakeRecord (F1)
- `rules/security-standards-owasp.md` — OWASP A04 insecure design: Trust-First CHECK (AC-004); A03 injection risk on AI-sourced input
- `rules/backend-development-standards.md` — entity + DbContext patterns
- `rules/database-standards.md` — CHECK constraints, soft-delete, indexes
- `rules/dotnet-architecture-standards.md` — EF Core 8 `ToTable().HasCheckConstraint()` pattern
- `rules/language-agnostic-standards.md` — YAGNI, KISS

### Search Evidence

| Pattern | File | Purpose |
|---|---|---|
| `HasQueryFilter` | `ApplicationDbContext.cs` (IntakeRecord) | Soft-delete filter precedent |
| `CheckConstraint("CK_MedicalCodeSuggestions_TrustFirst")` | Migration L90 | AC-004 DDL confirmation |
| `CheckConstraint("CK_ExtractedClinicalFields_ConfidenceScore")` | Migration L56 | AC-005 DDL confirmation |
| `IsDeleted = table.Column<bool>(..., defaultValue: false)` | Migration L51 | Soft-delete column confirmed |
| `INSERT INTO "__EFMigrationsHistory"` | Terminal output | Migration applied confirmation |
