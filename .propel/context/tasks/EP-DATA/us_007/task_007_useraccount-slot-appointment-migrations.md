# Task - TASK_007

## Requirement Reference

- **User Story**: US_007 — UserAccount, Slot, Appointment migrations
- **Story Location**: `.propel/context/tasks/EP-DATA/us_007/us_007.md`
- **Parent Epic**: EP-DATA

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `UserAccount` entity and migration created in SQL Server (`ApplicationDbContext`) |
| AC-002 | `Slot` entity with `rowversion` concurrency token created in SQL Server |
| AC-003 | `Appointment` entity with FSM state machine enforced in `SaveChanges` interceptor |
| AC-004 | `UserAccount.Email` has a UNIQUE index enforced at the database level |
| AC-005 | EF Core migrations apply cleanly via `dotnet ef database update` |

### Edge Cases

- Duplicate `Email` on insert → DB raises unique constraint violation; API must return 409 (handled at feature layer)
- Appointment FSM: only valid transitions are `Scheduled→Arrived`, `Scheduled→Cancelled`, `Arrived→Completed`, `Scheduled→NoShow`; invalid transition throws `InvalidOperationException` in interceptor

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

Create EF Core entity classes and migrations for `UserAccount`, `Slot`, and `Appointment` in `ApplicationDbContext` (SQL Server). Implement a `SaveChangesInterceptor` that enforces the Appointment FSM. Add a unique index on `UserAccount.Email` and a `rowversion` concurrency token on `Slot`.

---

## Dependent Tasks

- **TASK_001 (us_003)** — `ApplicationDbContext` and migration infrastructure must exist

---

## Impacted Components

- `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs`
- `src/ClinicalHealthcare.Infrastructure/Entities/UserAccount.cs`
- `src/ClinicalHealthcare.Infrastructure/Entities/Slot.cs`
- `src/ClinicalHealthcare.Infrastructure/Entities/Appointment.cs`
- `src/ClinicalHealthcare.Infrastructure/Interceptors/AppointmentFsmInterceptor.cs`
- `src/ClinicalHealthcare.Infrastructure.SqlMigrations/Migrations/`

---

## Implementation Plan

1. Create `UserAccount` entity: `Id`, `Email` (unique index), `PasswordHash`, `Role`, `IsActive`, `CreatedAt`.
2. Create `Slot` entity: `Id`, `SlotTime`, `DurationMinutes`, `IsAvailable`, `RowVersion` (`[Timestamp]` attribute → `rowversion`).
3. Create `Appointment` entity: `Id`, `PatientId`, `SlotId`, `Status` (enum: Scheduled/Arrived/Cancelled/Completed/NoShow), `BookedAt`.
4. Register all three entities in `ApplicationDbContext`; configure unique index on `UserAccount.Email` via fluent API.
5. Implement `AppointmentFsmInterceptor : SaveChangesInterceptor`; on `SavingChanges`, find modified `Appointment` entries and validate state transitions; throw `InvalidOperationException` on invalid transition.
6. Register `AppointmentFsmInterceptor` in `ApplicationDbContext` via `optionsBuilder.AddInterceptors(...)`.
7. Run `dotnet ef migrations add UserAccountSlotAppointment --project SqlMigrations --context ApplicationDbContext`.
8. Apply migration and verify schema with `dotnet ef database update`.

---

## Current Project State

```
src/ClinicalHealthcare.Infrastructure/
├── Data/
│   ├── ApplicationDbContext.cs
│   └── ClinicalDbContext.cs
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Infrastructure/Entities/UserAccount.cs` | UserAccount entity with Email unique index |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Entities/Slot.cs` | Slot entity with rowversion concurrency token |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Entities/Appointment.cs` | Appointment entity with FSM status enum |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Interceptors/AppointmentFsmInterceptor.cs` | SaveChanges FSM validation |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs` | Register entities; configure unique index; add interceptor |
| CREATE | `src/ClinicalHealthcare.Infrastructure.SqlMigrations/Migrations/*_UserAccountSlotAppointment.cs` | EF Core migration |

---

## External References

- [EF Core Concurrency Tokens](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)
- [EF Core SaveChanges Interceptors](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors)
- [EF Core Indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes)

---

## Build Commands

```bash
dotnet ef migrations add UserAccountSlotAppointment --project src/ClinicalHealthcare.Infrastructure.SqlMigrations --startup-project src/ClinicalHealthcare.Api --context ApplicationDbContext
dotnet ef database update --project src/ClinicalHealthcare.Infrastructure.SqlMigrations --startup-project src/ClinicalHealthcare.Api --context ApplicationDbContext
```

---

## Implementation Validation Strategy

- Migration applies cleanly with `dotnet ef database update`.
- Insert duplicate `Email` → SQL unique constraint violation raised.
- `Appointment` FSM interceptor: attempt `Completed→Scheduled` transition in unit test → `InvalidOperationException` thrown.
- `Slot.RowVersion` changes on every UPDATE (concurrency token test).
- `dotnet build` → 0 errors.

---

## Implementation Checklist

- [ ] **[AC-001]** `UserAccount` entity created and registered in `ApplicationDbContext`
- [ ] **[AC-002]** `Slot` entity with `[Timestamp]` rowversion concurrency token
- [ ] **[AC-003]** `AppointmentFsmInterceptor` enforces valid FSM transitions in `SavingChanges`
- [ ] **[AC-004]** Unique index on `UserAccount.Email` configured via fluent API
- [ ] **[AC-005]** Migration created and applies cleanly with `dotnet ef database update`
- [ ] All three entities registered in `ApplicationDbContext.OnModelCreating`
- [ ] `AppointmentFsmInterceptor` registered via `AddInterceptors`
- [ ] `dotnet build` passes with 0 errors
