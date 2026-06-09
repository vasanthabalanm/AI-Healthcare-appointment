# Test Plan: AuditLog, PHI Retention & Redis TTL Configuration

## Requirement Reference

| Field | Value |
|---|---|
| Epic | EP-DATA |
| User Story | US_011 |
| Layer | BE |
| AC Coverage | AC-001, AC-002, AC-003, AC-004, AC-005 |
| AI Impact | No |

## Test Plan Overview

**Purpose:** Verify that `ApplicationDbContext.InterceptPhiDeletes()` converts EF Core hard-delete operations on PHI entities (`UserAccount`, `WaitlistEntry`, `IntakeRecord`, `ClinicalDocument`) to soft-deletes (setting `IsDeleted=true` and `RetainUntil=UtcNow+7years`), that non-PHI entities (`Slot`) proceed to hard-delete, that `AuditLog` can be inserted without triggering the soft-delete interceptor, and that `CacheSettings` exposes the correct default TTL constants.

**Scope:** `SaveChanges` override behavior tested against InMemory DbContext. SQL GRANT revoking UPDATE/DELETE on the `AuditLogs` table (AC-001) is a database-level security control — it cannot be unit-tested and requires an integration test against a real SQL Server instance.

## Dependent Tasks

| Task | Plan |
|---|---|
| TASK_011 | AuditLog, PHI retention, Redis TTL implementation |

## Components Under Test

| Component | Type | File Path | Responsibilities |
|---|---|---|---|
| `ApplicationDbContext.InterceptPhiDeletes()` | Private method (via `SaveChanges` override) | `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs` | Converts hard-delete → soft-delete for PHI entities; sets `IsDeleted=true`, `RetainUntil=UtcNow+7years` |
| `ApplicationDbContext.IsPhiEntity()` | Private static method | `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs` | Returns `true` for `UserAccount`, `WaitlistEntry`, `IntakeRecord`, `ClinicalDocument` only |
| `AuditLog` | Entity | `src/ClinicalHealthcare.Infrastructure/Entities/AuditLog.cs` | Append-only log entity; not a PHI entity; not affected by `InterceptPhiDeletes` |
| `CacheSettings` | Configuration POCO | `src/ClinicalHealthcare.Infrastructure/Cache/CacheSettings.cs` | Centralises Redis TTL constants with defaults: Session=900s, Slot=60s, View360=300s |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---|---|---|---|---|---|---|
| TC-001 | positive | Remove(UserAccount) converts to soft-delete: IsDeleted=true | InMemory DB; `UserAccount` inserted and saved | `db.UserAccounts.Remove(user)` + `SaveChanges` | Row still exists; `IsDeleted=true`; `RetainUntil` is approximately UtcNow+7years | `db.UserAccounts.IgnoreQueryFilters().Single().IsDeleted == true`; `RetainUntil > DateTimeOffset.UtcNow.AddYears(6)` [SOURCE:INPUT] |
| TC-002 | positive | Remove(WaitlistEntry) converts to soft-delete: IsDeleted=true | InMemory DB; `WaitlistEntry` inserted and saved | `db.WaitlistEntries.Remove(entry)` + `SaveChanges` | Row still exists; `IsDeleted=true` | `db.WaitlistEntries.IgnoreQueryFilters().Single().IsDeleted == true` [SOURCE:INPUT] |
| TC-003 | positive | Remove(IntakeRecord) converts to soft-delete: IsDeleted=true | InMemory DB; `IntakeRecord` inserted and saved | `db.IntakeRecords.Remove(record)` + `SaveChanges` | Row still exists; `IsDeleted=true` | `db.IntakeRecords.IgnoreQueryFilters().Single().IsDeleted == true` [SOURCE:INPUT] |
| TC-004 | positive | Remove(ClinicalDocument) converts to soft-delete: IsDeleted=true | InMemory DB; `ClinicalDocument` inserted and saved | `db.ClinicalDocuments.Remove(doc)` + `SaveChanges` | Row still exists; `IsDeleted=true` | `db.ClinicalDocuments.IgnoreQueryFilters().Single().IsDeleted == true` [SOURCE:INPUT] |
| TC-005 | positive | AuditLog INSERT succeeds; soft-delete interceptor does not apply | InMemory DB | `db.AuditLogs.Add(log)` + `SaveChanges` | AuditLog row inserted | `db.AuditLogs.Count() == 1`; no exception; `IsPhiEntity` does NOT match `AuditLog` [SOURCE:INPUT] |
| TC-006 | positive | CacheSettings default TTL values are Session=900, Slot=60, View360=300 | `new CacheSettings()` default-constructed | Properties read | All three default values correct | `settings.SessionTtlSeconds == 900`; `settings.SlotTtlSeconds == 60`; `settings.View360TtlSeconds == 300` [SOURCE:INPUT] |
| EC-001 | edge_case | RetainUntil set to approximately 7 years from UtcNow on soft-delete | InMemory DB; `UserAccount` inserted | `Remove(user)` + `SaveChanges` | `RetainUntil` is within ±5 seconds of `UtcNow.AddYears(7)` | `Math.Abs((retainUntil - DateTimeOffset.UtcNow.AddYears(7)).TotalSeconds) < 5` [SOURCE:INPUT] |
| EC-002 | edge_case | Soft-deleted UserAccount not returned by default UserAccounts query | InMemory DB; `UserAccount` inserted then soft-deleted | `db.UserAccounts.ToList()` (with query filter active) | Empty result set (soft-deleted row hidden) | `db.UserAccounts.ToList().Count == 0` [SOURCE:INPUT] |

## Expected Changes

| Action | File Path | Description |
|---|---|---|
| Create | `tests/ClinicalHealthcare.Infrastructure.Tests/Migrations/AuditLogPhiRetentionTests.cs` | xUnit test class covering TC-001 through EC-002 |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|---|---|---|---|
| `ApplicationDbContext` | Real (InMemory) | `UseInMemoryDatabase(Guid.NewGuid().ToString())` per test | Real EF Core in-memory store; `SaveChanges` override executes `InterceptPhiDeletes` |
| `CacheSettings` | Real POCO | `new CacheSettings()` default-constructed | Default property values |

> No Moq mocks required. The soft-delete behavior is in the `SaveChanges` override and is fully exercised by InMemory.

## Test Data

| Scenario | Input Data | Expected Output |
|---|---|---|
| TC-001 UserAccount | `UserAccount { Email="a@b.com", PasswordHash="hash", Role="Patient", FirstName="A", LastName="B" }` | After `Remove` + `SaveChanges`: `IsDeleted=true`; `RetainUntil ≈ UtcNow+7y` |
| TC-002 WaitlistEntry | `WaitlistEntry { PatientId=1, Status=Active }` (with prerequisite UserAccount) | After `Remove` + `SaveChanges`: `IsDeleted=true` |
| TC-003 IntakeRecord | `IntakeRecord { PatientId=1, IntakeGroupId=Guid.NewGuid(), Version=1, IsLatest=true }` | After `Remove` + `SaveChanges`: `IsDeleted=true` |
| TC-004 ClinicalDocument | `ClinicalDocument { PatientId=1, OriginalFileName="x.pdf", EncryptedBlobPath="/enc/x.enc" }` | After `Remove` + `SaveChanges`: `IsDeleted=true` |
| TC-005 AuditLog | `AuditLog { EntityType="UserAccount", EntityId=1, Action="Create", OccurredAt=UtcNow }` | Inserted without exception; soft-delete not triggered |
| TC-006 CacheSettings | `new CacheSettings()` | Session=900, Slot=60, View360=300 |

## Test Commands

```bash
# Run all tests in this plan
dotnet test ClinicalHealthcare.slnx --filter "FullyQualifiedName~AuditLogPhiRetention"

# Run single test
dotnet test ClinicalHealthcare.slnx --filter "FullyQualifiedName=ClinicalHealthcare.Infrastructure.Tests.Migrations.AuditLogPhiRetentionTests.TC001_Remove_UserAccount_SoftDeletes"

# Coverage
dotnet test ClinicalHealthcare.slnx --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~AuditLogPhiRetention"
```

## Coverage Target

| Metric | Target |
|---|---|
| Line Coverage | ≥ 90% |
| Branch Coverage | ≥ 85% |
| Critical Paths | `InterceptPhiDeletes()` — all 4 PHI entity branches; `IsPhiEntity()` — true for all 4 PHI types and false for `AuditLog`/`Slot`; `CacheSettings` defaults; `RetainUntil` 7-year calculation |

## Documentation References

- [EF Core Change Tracker](https://learn.microsoft.com/en-us/ef/core/change-tracking/)
- [EF Core SaveChanges Override](https://learn.microsoft.com/en-us/ef/core/saving/basic)
- [EF Core Query Filters](https://learn.microsoft.com/en-us/ef/core/querying/filters)
- Existing pattern: `tests/ClinicalHealthcare.Infrastructure.Tests/Data/ApplicationDbContextTests.cs`

## Implementation Checklist

- [x] Use `UseInMemoryDatabase(Guid.NewGuid().ToString())` per test for isolation
- [x] Add `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` to all InMemory contexts
- [x] Insert + save each PHI entity BEFORE calling `Remove` so `OriginalValues` are populated and the change tracker detects `Deleted` state
- [x] Use `db.WaitlistEntries.IgnoreQueryFilters().Single()` (and similarly for other PHI sets) after soft-delete to bypass the `IsDeleted` query filter and verify row still exists
- [x] TC-006 is a pure unit test — `new CacheSettings()` only; no DbContext required
- [x] EC-001 — use `DateTimeOffset.UtcNow.AddYears(7)` as reference and assert `RetainUntil` within ±5 seconds
- [x] Note in test comments that `AuditLog` INSERT-only enforcement (SQL GRANT) is a DB-level security control requiring an integration test against SQL Server
- [x] Seed a `UserAccount` prerequisite before inserting `WaitlistEntry`, `IntakeRecord`, or `ClinicalDocument` to satisfy FK constraints
