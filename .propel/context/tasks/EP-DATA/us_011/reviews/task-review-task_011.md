---
task_id: task_011
us_id: us_011
epic: EP-DATA
reviewed_on: 2026-05-14
reviewer: analyze-implementation (automated)
verdict: Pass
---

# Implementation Analysis — task_011_auditlog-phi-retention-redis-ttl.md

## Verdict

**Status:** Pass
**Summary:** All five acceptance criteria are fully satisfied. `AuditLog` entity is complete with all 9 task-specified fields; INSERT-only enforcement is applied via `REVOKE UPDATE, DELETE ON [dbo].[AuditLogs] FROM [public]` in the migration `Up()`. All four PHI entities (`UserAccount`, `IntakeRecord`, `ClinicalDocument`, `WaitlistEntry`) have `IsDeleted` + `RetainUntil` columns. `ApplicationDbContext.SaveChanges()` and `SaveChangesAsync()` both delegate to `InterceptPhiDeletes()` which converts `EntityState.Deleted` on PHI entities to a soft-delete with `RetainUntil = UtcNow.AddYears(7)`. `CacheSettings` TTL constants are confirmed at 900/60/300 seconds. Migration `20260514085336_AuditLogPhiRetention` applied cleanly to the development database. 83/83 tests pass (+18 new). Two low-priority design observations documented below; neither blocks Pass.

---

## Traceability Matrix

| Requirement / Acceptance Criterion | Evidence (file : line) | Result |
|---|---|---|
| AC-001 — `AuditLog` INSERT-only; REVOKE at SQL Server GRANT level | `AuditLog.cs` all 9 fields; `ApplicationDbContext.cs` AuditLogs DbSet + fluent config; migration L99: `REVOKE UPDATE, DELETE ON [dbo].[AuditLogs] FROM [public]` | **Pass** |
| AC-002 — All PHI entities have `IsDeleted` + `RetainUntil` | `UserAccount.cs` L23–29; `WaitlistEntry.cs` L36–42; `IntakeRecord.cs` L59–65; `ClinicalDocument.cs` L82–88; migration: 8 `AddColumn` statements for all 4 tables | **Pass** |
| AC-003 — `SaveChanges` override converts hard `DELETE` on PHI entities to soft-delete | `ApplicationDbContext.cs`: `SaveChanges()` + `SaveChangesAsync()` both call `InterceptPhiDeletes()`; sets `IsDeleted=true`, `RetainUntil=UtcNow.AddYears(7)`; `IsPhiEntity()` matches all 4 types | **Pass** |
| AC-004 — `CacheSettings` constants 900/60/300 | `CacheSettings.cs` `SessionTtlSeconds=900`, `SlotTtlSeconds=60`, `View360TtlSeconds=300`; confirmed by 3 unit tests | **Pass** |
| AC-005 — Migration `AuditLogPhiRetention` created and applies cleanly | `20260514085336_AuditLogPhiRetention.cs` exists; terminal: `Done. INSERT INTO "__EFMigrationsHistory"` | **Pass** |
| Edge — Hard-delete on PHI entity → `IsDeleted=true` + 7-year retention | Tests: `HardDelete_OnUserAccount_ConvertedToSoftDelete`, `HardDelete_OnUserAccount_SetsRetainUntil_SevenYears` (range-bound), + 3 more PHI entities | **Pass** |
| Edge — Non-PHI hard-delete proceeds normally | `HardDelete_OnSlot_ProceedsNormally_NotIntercepted` — `FindAsync` returns null after remove | **Pass** |
| Edge — AuditLog actor null for system actions | `AuditLog_ActorId_CanBeNull_ForSystemActions` | **Pass** |
| Additional — Migration `Down()` restores GRANT before DROP | Migration L104: `GRANT UPDATE, DELETE ON [dbo].[AuditLogs] TO [public]` before `DropTable` | **Pass** |
| Additional — 83/83 tests pass | Terminal: `Passed: 83, Failed: 0, Skipped: 0` | **Pass** |

---

## Logical & Design Findings

- **Design — MEDIUM — PHI entities lack `HasQueryFilter(x => !x.IsDeleted)`:** `UserAccount`, `WaitlistEntry`, and `ClinicalDocument` now have `IsDeleted` but no global query filter in `ApplicationDbContext`. After soft-delete, these records remain visible in every default EF Core query. The established precedent in this codebase: `IntakeRecord.HasQueryFilter(r => r.IsLatest)` and `ExtractedClinicalField.HasQueryFilter(f => !f.IsDeleted)` (TASK_010). Without query filters, service-layer code will require manual `.Where(x => !x.IsDeleted)` guards, which is error-prone. This was not specified in the task ACs (task states only "IsDeleted + RetainUntil columns" and "SaveChanges override"), so it cannot block Pass. Recommend adding in a follow-up task. **Note:** `IntakeRecord` already has `HasQueryFilter(r => r.IsLatest)` — adding `&& !r.IsDeleted` to that filter would be the correct composite approach for IntakeRecord.

- **Design — LOW — `AuditLog.OccurredAt` uses `DateTime` while PHI columns use `DateTimeOffset`:** The PHI retention `RetainUntil` column is correctly `DateTimeOffset?` for timezone-aware retention calculations. `AuditLog.OccurredAt` uses `DateTime` (UTC convention via `= DateTime.UtcNow`). This is not a bug — UTC `datetime2` is standard for audit timestamps — but the inconsistency in datetime type usage across the same migration is worth noting for future maintenance.

- **Design — LOW — `UserAccount` fluent config is split across two `modelBuilder.Entity<UserAccount>()` calls:** The first block (L29–37) configures `Email`, `PasswordHash`, `Role`, `CreatedAt`, and the email index. The second block (L130–133) adds `IsDeleted` and `RetainUntil`. EF Core accumulates both calls correctly, but the split is unconventional. No functional impact; the pattern was introduced to minimize diff surface against existing code — acceptable.

- **Security — OK — OWASP A09 (Security Logging): No FK constraints on `AuditLog`:** Audit records must survive deletion of the entities they reference. Intentionally no FK constraints on `EntityId` or `ActorId`. Documented in entity XML comments. ✅

- **Security — OK — OWASP A04 (Insecure Design): Soft-delete prevents PHI data loss:** Hard-delete is intercepted at the context level so no application code path can bypass it via EF Core without using raw SQL. SQL-level REVOKE prevents direct UPDATE/DELETE on `AuditLogs` from the application DB user. ✅

- **Performance — OK:** `AuditLog` has two indexes: composite `(EntityType, EntityId)` for entity-scoped queries and `OccurredAt` for time-range queries. `IsDeleted` on PHI tables has no index — appropriate since soft-delete is a boolean column used in bulk scans, not selective lookups. ✅

---

## Test Review

### Existing Tests (18 test methods, 22 runtime cases via Theory)

| Test | Covers | Status |
|---|---|---|
| `AuditLog_CanBeInserted_WithAllFields` | AC-001: full insert roundtrip | ✅ Pass |
| `AuditLog_BeforeAfterValue_CanBeNull_ForInsertAction` | AC-001: nullable BeforeValue for INSERT | ✅ Pass |
| `AuditLog_ActorId_CanBeNull_ForSystemActions` | AC-001: nullable ActorId | ✅ Pass |
| `UserAccount_HasIsDeleted_AndRetainUntil_Properties` | AC-002: reflection contract | ✅ Pass |
| `PhiEntity_HasIsDeletedAndRetainUntil` [Theory × 4] | AC-002: all 4 PHI entities | ✅ Pass |
| `UserAccount_IsDeleted_DefaultsFalse` | AC-002: default state | ✅ Pass |
| `HardDelete_OnUserAccount_ConvertedToSoftDelete` | AC-003: soft-delete intercept | ✅ Pass |
| `HardDelete_OnUserAccount_SetsRetainUntil_SevenYears` | AC-003: 7-year retention | ✅ Pass |
| `HardDelete_OnIntakeRecord_ConvertedToSoftDelete` | AC-003: IntakeRecord | ✅ Pass |
| `HardDelete_OnWaitlistEntry_ConvertedToSoftDelete` | AC-003: WaitlistEntry | ✅ Pass |
| `HardDelete_OnClinicalDocument_ConvertedToSoftDelete` | AC-003: ClinicalDocument | ✅ Pass |
| `HardDelete_OnSlot_ProceedsNormally_NotIntercepted` | AC-003: non-PHI hard-delete | ✅ Pass |
| `CacheSettings_SessionTtlSeconds_Is900` | AC-004: 900s constant | ✅ Pass |
| `CacheSettings_SlotTtlSeconds_Is60` | AC-004: 60s constant | ✅ Pass |
| `CacheSettings_View360TtlSeconds_Is300` | AC-004: 300s constant | ✅ Pass |

### Missing Tests (observations only — no gaps block Pass)

- [ ] **Low:** `AuditLog_QueryByEntityType_ReturnsCorrectEntries` — exercises the `IX_AuditLogs_EntityType_EntityId` index path (InMemory only; DDL is confirmed)
- [ ] **Low:** `UserAccount_SoftDeleted_IsVisibleWithIgnoreQueryFilters` — anticipates future `HasQueryFilter` addition (MEDIUM gap above); document intent now
- [ ] **Low:** `IntakeRecord_SoftDelete_CompositeFilter` — once `HasQueryFilter` is updated to `r.IsLatest && !r.IsDeleted`, this test validates both conditions

---

## Validation Results

**Commands Executed:**

```powershell
dotnet build ClinicalHealthcare.slnx
dotnet ef migrations add AuditLogPhiRetention ...
dotnet ef database update ... --context ApplicationDbContext
dotnet test tests/ClinicalHealthcare.Infrastructure.Tests/...csproj
```

**Outcomes:**

| Command | Outcome |
|---|---|
| `dotnet build` | ✅ `Build succeeded. 0 Error(s)` |
| `dotnet ef migrations add AuditLogPhiRetention` | ✅ `Done. To undo this action, use 'ef migrations remove'` |
| `dotnet ef database update` | ✅ `Done.` — 14 DDL statements + REVOKE applied |
| `dotnet test` (pre-new-tests) | ✅ `Passed: 65` |
| `dotnet test` (after AuditLogPhiRetentionTests) | ✅ `Passed: 83, Failed: 0, Skipped: 0` |

**DDL confirmation from migration output:**

```sql
ALTER TABLE [WaitlistEntries] ADD [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit);
ALTER TABLE [WaitlistEntries] ADD [RetainUntil] datetimeoffset NULL;
ALTER TABLE [UserAccounts] ADD [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit);
ALTER TABLE [UserAccounts] ADD [RetainUntil] datetimeoffset NULL;
ALTER TABLE [IntakeRecords] ADD [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit);
ALTER TABLE [IntakeRecords] ADD [RetainUntil] datetimeoffset NULL;
ALTER TABLE [ClinicalDocuments] ADD [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit);
ALTER TABLE [ClinicalDocuments] ADD [RetainUntil] datetimeoffset NULL;
CREATE TABLE [AuditLogs] (...)
CREATE INDEX [IX_AuditLogs_EntityType_EntityId] ...
CREATE INDEX [IX_AuditLogs_OccurredAt] ...
REVOKE UPDATE, DELETE ON [dbo].[AuditLogs] FROM [public];
INSERT INTO [__EFMigrationsHistory] ... '20260514085336_AuditLogPhiRetention'
```

---

## Fix Plan (Prioritized)

| # | Fix | File(s) | Priority | Risk |
|---|---|---|---|---|
| F1 | Add `HasQueryFilter(x => !x.IsDeleted)` to `UserAccount`, `WaitlistEntry`, `ClinicalDocument` fluent config; update `IntakeRecord` filter to `r.IsLatest && !r.IsDeleted`; add soft-delete visibility tests for each entity | `ApplicationDbContext.cs`; `AuditLogPhiRetentionTests.cs` (or new test file) | **MEDIUM** — follow-up task | M |
| F2 | Consolidate `UserAccount` fluent config into single `modelBuilder.Entity<UserAccount>()` block | `ApplicationDbContext.cs` | **LOW** — cosmetic | L |

---

## Appendix

### Rules Applied

- `rules/ai-assistant-usage-policy.md` — directive compliance
- `rules/security-standards-owasp.md` — OWASP A04 (insecure design), A09 (security logging), A01 (access control: REVOKE)
- `rules/backend-development-standards.md` — entity + DbContext patterns
- `rules/database-standards.md` — migration standards, soft-delete, indexes
- `rules/dotnet-architecture-standards.md` — EF Core override pattern, `SaveChangesAsync`
- `rules/dry-principle-guidelines.md` — `IsPhiEntity()` single source of truth; query filter precedent from `IntakeRecord`
- `rules/language-agnostic-standards.md` — YAGNI, KISS
- `rules/code-anti-patterns.md` — no magic strings (uses `nameof`)

### Search Evidence

| Pattern | File | Purpose |
|---|---|---|
| `REVOKE UPDATE, DELETE` | Migration L99 | AC-001 DDL confirmation |
| `IsDeleted` + `AddColumn` | Migration L13–62 | AC-002 DDL confirmation for all 4 tables |
| `InterceptPhiDeletes` | `ApplicationDbContext.cs` | AC-003 implementation |
| `IsPhiEntity` | `ApplicationDbContext.cs` | PHI entity type registry |
| `SessionTtlSeconds = 900` | `CacheSettings.cs` | AC-004 constant confirmation |
| `Passed: 83, Failed: 0` | Terminal output | AC-005 + full test suite |
