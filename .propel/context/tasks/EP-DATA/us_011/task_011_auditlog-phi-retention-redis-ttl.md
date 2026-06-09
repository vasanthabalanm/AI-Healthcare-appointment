# Task - TASK_011

## Requirement Reference

- **User Story**: US_011 — AuditLog append-only + PHI retention + Redis TTL
- **Story Location**: `.propel/context/tasks/EP-DATA/us_011/us_011.md`
- **Parent Epic**: EP-DATA

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `AuditLog` entity is INSERT-only; `UPDATE` and `DELETE` are revoked at the SQL Server GRANT level |
| AC-002 | All PHI entities have `IsDeleted` (soft-delete) and `RetainUntil` columns |
| AC-003 | `SaveChanges` override in `ApplicationDbContext` converts hard `DELETE` on PHI entities to soft-delete |
| AC-004 | `CacheSettings` class defines `SessionTtlSeconds=900`, `SlotTtlSeconds=60`, `View360TtlSeconds=300` |
| AC-005 | `AuditLog` migration applies cleanly; migration SQL includes `REVOKE UPDATE, DELETE ON AuditLogs` |

### Edge Cases

- Hard-delete attempt on PHI entity (UserAccount, IntakeRecord, ClinicalDocument, WaitlistEntry) → intercepted in `SaveChanges`; converted to `IsDeleted=true` + `RetainUntil=UtcNow+7years`
- `AuditLog` must never be modified after insert; no EF Core tracking on update is permitted

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
| Infrastructure | Upstash Redis | N/A | Cache TTL constants consumed by CacheService |

---

## Task Overview

Create the `AuditLog` entity and migration. Add soft-delete + `RetainUntil` columns to all PHI entities. Override `SaveChanges` to intercept hard deletes and convert to soft-deletes. Add `REVOKE UPDATE, DELETE` SQL to the `AuditLog` migration. Define `CacheSettings` TTL constants.

---

## Dependent Tasks

- **TASK_001 (us_003)** — `ApplicationDbContext` infrastructure
- **TASK_001 (us_004)** — `CacheSettings` lives alongside `CacheService`
- **TASK_001 (us_007–us_009)** — PHI entity classes must exist before adding soft-delete columns

---

## Impacted Components

- `src/ClinicalHealthcare.Infrastructure/Entities/AuditLog.cs`
- `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs`
- `src/ClinicalHealthcare.Infrastructure/Cache/CacheSettings.cs`
- PHI entity classes: `UserAccount`, `IntakeRecord`, `ClinicalDocument`, `WaitlistEntry`
- `src/ClinicalHealthcare.Infrastructure.SqlMigrations/Migrations/`

---

## Implementation Plan

1. Create `AuditLog` entity: `Id`, `EntityType`, `EntityId`, `ActorId`, `Action`, `BeforeValue` (nvarchar(max)), `AfterValue` (nvarchar(max)), `OccurredAt`, `CorrelationId`.
2. Add `IsDeleted` (bool, default false) and `RetainUntil` (DateTimeOffset?, nullable) to `UserAccount`, `IntakeRecord`, `ClinicalDocument`, `WaitlistEntry` entities.
3. Override `SaveChangesAsync` in `ApplicationDbContext`: find `EntityState.Deleted` entries for PHI entity types; set `IsDeleted=true`, `RetainUntil=UtcNow.AddYears(7)`; change state to `Modified`.
4. Create `CacheSettings` class (or confirm it exists from us_004): `SessionTtlSeconds=900`, `SlotTtlSeconds=60`, `View360TtlSeconds=300`.
5. Generate migration `AuditLogPhiRetention`.
6. Add raw SQL to migration `Up()` method: `migrationBuilder.Sql("REVOKE UPDATE, DELETE ON [AuditLogs] FROM PUBLIC;")` (adjust principal as needed for the app DB user).
7. Apply migration; verify `AuditLog` table structure.

---

## Current Project State

```
src/ClinicalHealthcare.Infrastructure/Entities/
├── UserAccount.cs
├── Slot.cs
├── Appointment.cs
├── WaitlistEntry.cs
├── IntakeRecord.cs
└── ClinicalDocument.cs
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Infrastructure/Entities/AuditLog.cs` | Append-only audit log entity |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Entities/UserAccount.cs` | Add IsDeleted + RetainUntil |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Entities/IntakeRecord.cs` | Add IsDeleted + RetainUntil |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Entities/ClinicalDocument.cs` | Add IsDeleted + RetainUntil |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Entities/WaitlistEntry.cs` | Add IsDeleted + RetainUntil |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs` | SaveChanges PHI soft-delete override; register AuditLog |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Cache/CacheSettings.cs` | Confirm TTL constants (or create) |
| CREATE | `src/ClinicalHealthcare.Infrastructure.SqlMigrations/Migrations/*_AuditLogPhiRetention.cs` | Migration with REVOKE SQL |

---

## External References

- [EF Core Soft Delete](https://learn.microsoft.com/en-us/ef/core/querying/filters)
- [EF Core SaveChanges Override](https://learn.microsoft.com/en-us/ef/core/saving/hooks)

---

## Build Commands

```bash
dotnet ef migrations add AuditLogPhiRetention --project src/ClinicalHealthcare.Infrastructure.SqlMigrations --startup-project src/ClinicalHealthcare.Api --context ApplicationDbContext
dotnet ef database update --project src/ClinicalHealthcare.Infrastructure.SqlMigrations --startup-project src/ClinicalHealthcare.Api --context ApplicationDbContext
```

---

## Implementation Validation Strategy

- `context.UserAccounts.Remove(entity)` in unit test → `IsDeleted=true`, state = `Modified` (not `Deleted`).
- `AuditLog` insert succeeds; attempted `UPDATE` against `AuditLogs` table with app DB user → permission denied.
- Migration SQL includes `REVOKE UPDATE, DELETE ON [AuditLogs]`.
- `CacheSettings.SessionTtlSeconds == 900`, `SlotTtlSeconds == 60`, `View360TtlSeconds == 300`.
- `dotnet build` → 0 errors.

---

## Implementation Checklist

- [x] **[AC-001]** `AuditLog` entity created; migration SQL revokes UPDATE/DELETE permissions
- [x] **[AC-002]** `IsDeleted` + `RetainUntil` added to all four PHI entities
- [x] **[AC-003]** `SaveChanges` override converts `EntityState.Deleted` on PHI entities to soft-delete
- [x] **[AC-004]** `CacheSettings` constants: 900/60/300 seconds
- [x] **[AC-005]** Migration `AuditLogPhiRetention` created and applies cleanly
- [x] `RetainUntil` set to `UtcNow.AddYears(7)` on soft-delete
- [x] Hard-delete on non-PHI entities (Slot, Appointment) proceeds normally
- [x] `dotnet build` passes with 0 errors
