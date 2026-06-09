# Test Plan: WaitlistEntry & IntakeRecord EF Core Migrations

## Requirement Reference

| Field | Value |
|---|---|
| Epic | EP-DATA |
| User Story | US_008 |
| Layer | BE |
| AC Coverage | AC-001, AC-002, AC-003, AC-004, AC-005 |
| AI Impact | No |

## Test Plan Overview

**Purpose:** Verify that `ApplicationDbContext` correctly configures the `WaitlistEntry` filtered partial unique index, `WaitlistStatus` enum integer values, and `IntakeRecord` versioning logic with its `IsLatest && !IsDeleted` composite query filter. Tests validate EF Core model metadata and in-memory query filter behavior.

**Scope:** Model metadata assertions and InMemory DbContext query filter behavior. SQL Server partial-index enforcement and FK RESTRICT behavior are integration-test concerns and are out of scope here.

## Dependent Tasks

| Task | Plan |
|---|---|
| TASK_008 | WaitlistEntry and IntakeRecord schema implementation |

## Components Under Test

| Component | Type | File Path | Responsibilities |
|---|---|---|---|
| `ApplicationDbContext` | EF Core DbContext | `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs` | Configures WaitlistEntry filtered index; IntakeRecord composite query filter `IsLatest && !IsDeleted` |
| `WaitlistEntry` | Entity | `src/ClinicalHealthcare.Infrastructure/Entities/WaitlistEntry.cs` | Waitlist position with `WaitlistStatus` enum defaulting to Active=0; PHI soft-delete fields |
| `IntakeRecord` | Entity | `src/ClinicalHealthcare.Infrastructure/Entities/IntakeRecord.cs` | Patient intake with versioning via `IntakeGroupId`/`Version`/`IsLatest`; composite query filter |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---|---|---|---|---|---|---|
| TC-001 | positive | WaitlistEntry has filtered partial index on PatientId in EF Core model | InMemory ApplicationDbContext created | `WaitlistEntry` model metadata inspected via `db.Model.FindEntityType` | A filtered index with filter expression containing `Status` and `0` exists on `PatientId` | `entityType.GetIndexes().Any(i => i.GetFilter() != null && i.Properties.Any(p => p.Name == "PatientId"))` is `true` [SOURCE:INPUT] |
| TC-002 | positive | WaitlistStatus enum values Active=0, Fulfilled=1, Expired=2 stored as int | No DbContext required | `WaitlistStatus` enum cast to int | Active casts to 0; Fulfilled to 1; Expired to 2 | `(int)WaitlistStatus.Active == 0`, `(int)WaitlistStatus.Fulfilled == 1`, `(int)WaitlistStatus.Expired == 2` [SOURCE:INPUT] |
| TC-003 | positive | Default query filter returns only IsLatest=true rows | InMemory DB; two IntakeRecord rows — v1 IsLatest=false, v2 IsLatest=true, both IsDeleted=false | `db.IntakeRecords.ToList()` called without IgnoreQueryFilters | Only v2 (IsLatest=true) returned | `records.Count == 1 && records[0].Version == 2` [SOURCE:INPUT] |
| TC-004 | positive | IgnoreQueryFilters() returns all versions | InMemory DB; v1 IsLatest=false + v2 IsLatest=true | `db.IntakeRecords.IgnoreQueryFilters().ToList()` | Both rows returned | `records.Count == 2` [SOURCE:INPUT] |
| TC-005 | positive | IntakeRecord versioning pattern: insert v1 then v2, only v2 visible by default | InMemory DB; v1 inserted with IsLatest=true; then v1 updated to IsLatest=false and v2 inserted | Default query | Version 2 row visible; Version 1 filtered out | `db.IntakeRecords.Single().Version == 2` [SOURCE:INPUT] |
| EC-001 | edge_case | WaitlistEntry Status=Fulfilled not covered by partial index — two Fulfilled entries for same PatientId allowed | InMemory DB; patient has existing Fulfilled entry | Second Fulfilled entry for same patient inserted and SaveChanges called | No exception (partial index only covers Active=0) | No exception; `db.WaitlistEntries.IgnoreQueryFilters().Count() == 2` [SOURCE:INFERRED] |
| EC-002 | edge_case | IntakeRecord IsLatest=true but IsDeleted=true excluded by composite filter | InMemory DB; row with IsLatest=true, IsDeleted=true | Default query | Row excluded | `db.IntakeRecords.ToList().Count == 0` [SOURCE:INPUT] |
| ES-001 | error | IntakeRecord with IsLatest=false and IsDeleted=true also excluded | InMemory DB; row with IsLatest=false, IsDeleted=true | Default query | Row excluded | `db.IntakeRecords.ToList().Count == 0` [SOURCE:INFERRED] |

## Expected Changes

| Action | File Path | Description |
|---|---|---|
| Create | `tests/ClinicalHealthcare.Infrastructure.Tests/Migrations/WaitlistEntryIntakeRecordMigrationTests.cs` | xUnit test class covering TC-001 through ES-001 |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|---|---|---|---|
| `ApplicationDbContext` | Real (InMemory) | `UseInMemoryDatabase(Guid.NewGuid().ToString())` per test | Real EF Core in-memory store |
| `UserAccount` (FK) | Real entity | Inserted as prerequisite where PatientId FK is required | Seeded UserAccount with Id=1 |

> No Moq mocks required — all tests use the real EF Core InMemory provider.

## Test Data

| Scenario | Input Data | Expected Output |
|---|---|---|
| TC-003 query filter | `IntakeRecord { IntakeGroupId=G, PatientId=1, Version=1, IsLatest=false, IsDeleted=false }`, `IntakeRecord { IntakeGroupId=G, PatientId=1, Version=2, IsLatest=true, IsDeleted=false }` | Only Version=2 row returned by `db.IntakeRecords.ToList()` |
| TC-005 versioning | v1 saved; then v1.IsLatest set false + v2 inserted | `db.IntakeRecords.Single()` returns v2 |
| EC-001 partial index | PatientId=1, Status=Fulfilled (×2) | Both rows saved; no exception from InMemory (partial index is DDL-only) |
| EC-002 composite filter | `IntakeRecord { IsLatest=true, IsDeleted=true }` | `db.IntakeRecords.ToList()` returns empty list |

## Test Commands

```bash
# Run all tests in this plan
dotnet test ClinicalHealthcare.slnx --filter "FullyQualifiedName~WaitlistEntryIntakeRecordMigration"

# Run single test
dotnet test ClinicalHealthcare.slnx --filter "FullyQualifiedName=ClinicalHealthcare.Infrastructure.Tests.Migrations.WaitlistEntryIntakeRecordMigrationTests.TC003_DefaultQueryFilter_ReturnsOnlyLatest"

# Coverage
dotnet test ClinicalHealthcare.slnx --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~WaitlistEntryIntakeRecordMigration"
```

## Coverage Target

| Metric | Target |
|---|---|
| Line Coverage | ≥ 85% |
| Branch Coverage | ≥ 80% |
| Critical Paths | `IntakeRecord.HasQueryFilter` composite filter (`IsLatest && !IsDeleted`); `WaitlistStatus` integer enum mapping; `WaitlistEntry` filtered index registration |

## Documentation References

- [EF Core Query Filters](https://learn.microsoft.com/en-us/ef/core/querying/filters)
- [EF Core InMemory Testing](https://learn.microsoft.com/en-us/ef/core/testing/testing-without-the-database)
- [HasIndex with filter](https://learn.microsoft.com/en-us/ef/core/modeling/indexes#index-filter)
- Existing pattern: `tests/ClinicalHealthcare.Infrastructure.Tests/Data/ApplicationDbContextTests.cs`

## Implementation Checklist

- [x] Use `UseInMemoryDatabase(Guid.NewGuid().ToString())` per test for isolation
- [x] Add `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` to all InMemory contexts
- [x] Seed a prerequisite `UserAccount` before inserting `WaitlistEntry` or `IntakeRecord` rows (FK requirement)
- [x] Use `db.WaitlistEntries.IgnoreQueryFilters()` for EC-001 to confirm InMemory count without the soft-delete filter
- [x] Assert `IntakeRecord` composite filter behavior via `db.IntakeRecords.ToList()` (with filter) vs `IgnoreQueryFilters()` (without)
- [x] Verify `WaitlistEntry` filtered index via `db.Model.FindEntityType(typeof(WaitlistEntry))!.GetIndexes()`
- [x] Save changes with `await db.SaveChangesAsync()` and use `await using var db = ...` pattern
- [x] EC-001: note in test comment that InMemory does NOT enforce SQL partial-index uniqueness — test verifies model metadata only
