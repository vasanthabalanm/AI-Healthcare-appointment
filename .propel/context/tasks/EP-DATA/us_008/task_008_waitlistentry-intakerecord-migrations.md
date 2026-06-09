# Task - TASK_008

## Requirement Reference

- **User Story**: US_008 — WaitlistEntry + IntakeRecord migrations
- **Story Location**: `.propel/context/tasks/EP-DATA/us_008/us_008.md`
- **Parent Epic**: EP-DATA

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `WaitlistEntry` entity and migration created in SQL Server |
| AC-002 | `WaitlistEntry` has a filtered partial unique index on `(PatientId)` WHERE `Status = Active (0)` |
| AC-003 | `IntakeRecord` entity supports versioning (each PATCH creates a new row with incremented version) |
| AC-004 | `IntakeRecord` has a default query filter returning only the latest version per intake ID |
| AC-005 | Migrations apply cleanly via `dotnet ef database update` |

### Edge Cases

- Partial unique index prevents a patient from having two `Active` waitlist entries; `Expired` or `Fulfilled` entries are not constrained
- `IntakeRecord` default query filter uses a subquery or a `LatestVersion` flag; disabling the filter (`.IgnoreQueryFilters()`) is available for admin/history queries

---

## Design References

N/A — UI Impact: No

---

## AI References

N/A — AI Impact: No

---

## Mobile References

N/A — Mobile Impact: No

---

## Applicable Technology Stack

| Layer | Technology | Version | Justification |
|-------|-----------|---------|---------------|
| Database | SQL Server | 2022 / Express | Operational DB per design.md |
| Backend | EF Core | 8.x | ORM + migrations per design.md |
| Backend | EF Core SqlServer | 8.x | SQL Server provider |

---

## Task Overview

Create EF Core entity classes and migrations for `WaitlistEntry` and `IntakeRecord` in `ApplicationDbContext`. Implement the filtered partial unique index on `WaitlistEntry` and the versioning + default query filter on `IntakeRecord`.

---

## Dependent Tasks

- **TASK_001 (us_003)** — `ApplicationDbContext` infrastructure
- **TASK_001 (us_007)** — `UserAccount` migration (PatientId FK)

---

## Impacted Components

- `src/ClinicalHealthcare.Infrastructure/Entities/WaitlistEntry.cs`
- `src/ClinicalHealthcare.Infrastructure/Entities/IntakeRecord.cs`
- `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs`
- `src/ClinicalHealthcare.Infrastructure.SqlMigrations/Migrations/`

---

## Implementation Plan

1. Create `WaitlistEntry` entity: `Id`, `PatientId` (FK→UserAccount), `SlotId`, `Status` (enum: Active=0/Fulfilled/Expired), `QueuedAt`.
2. Configure filtered unique index on `WaitlistEntry` in fluent API: `.HasIndex(e => e.PatientId).IsUnique().HasFilter("[Status] = 0")`.
3. Create `IntakeRecord` entity: `Id`, `IntakeGroupId` (groups versions), `PatientId`, `Version` (int), `IsLatest` (bool), `Source` (Manual/AI), `SubmittedAt`, and clinical field columns.
4. Configure default query filter on `IntakeRecord`: `.HasQueryFilter(r => r.IsLatest)`.
5. Register both entities in `ApplicationDbContext`.
6. Run and apply migration `WaitlistEntryIntakeRecord`.
7. Write unit test: add two `Active` WaitlistEntry rows for same patient → FK/unique constraint violation on second insert.
8. Verify `IntakeRecord` query (without `IgnoreQueryFilters`) returns only `IsLatest=true` rows.

---

## Current Project State

```
src/ClinicalHealthcare.Infrastructure/
├── Data/ApplicationDbContext.cs
├── Entities/
│   ├── UserAccount.cs
│   ├── Slot.cs
│   └── Appointment.cs
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Infrastructure/Entities/WaitlistEntry.cs` | WaitlistEntry entity |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Entities/IntakeRecord.cs` | IntakeRecord with versioning |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs` | Register entities; configure filtered index and query filter |
| CREATE | `src/ClinicalHealthcare.Infrastructure.SqlMigrations/Migrations/*_WaitlistEntryIntakeRecord.cs` | EF Core migration |

---

## External References

- [EF Core Filtered Indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes#index-filter)
- [EF Core Query Filters](https://learn.microsoft.com/en-us/ef/core/querying/filters)

---

## Build Commands

```bash
dotnet ef migrations add WaitlistEntryIntakeRecord --project src/ClinicalHealthcare.Infrastructure.SqlMigrations --startup-project src/ClinicalHealthcare.Api --context ApplicationDbContext
dotnet ef database update --project src/ClinicalHealthcare.Infrastructure.SqlMigrations --startup-project src/ClinicalHealthcare.Api --context ApplicationDbContext
```

---

## Implementation Validation Strategy

- Migration applies cleanly.
- Two `Active` WaitlistEntry rows for same patient → unique constraint violation.
- `Expired` WaitlistEntry for same patient with existing `Active` → allowed.
- Default query filter: `context.IntakeRecords.ToList()` returns only `IsLatest=true` rows.
- `context.IntakeRecords.IgnoreQueryFilters().ToList()` returns all versions.
- `dotnet build` → 0 errors.

---

## Implementation Checklist

- [x] **[AC-001]** `WaitlistEntry` entity created and registered in `ApplicationDbContext`
- [x] **[AC-002]** Filtered partial unique index on `(PatientId) WHERE Status = 0` configured
- [x] **[AC-003]** `IntakeRecord` versioning: each PATCH creates new row; `IntakeGroupId` groups versions
- [x] **[AC-004]** Default query filter `r.IsLatest` on `IntakeRecord`; `IgnoreQueryFilters()` available
- [x] **[AC-005]** Migration created and applies cleanly
- [x] Two Active WaitlistEntry rows for same patient rejected at DB level
- [x] `IsLatest` flag managed by application before `SaveChanges`
- [x] `dotnet build` passes with 0 errors
