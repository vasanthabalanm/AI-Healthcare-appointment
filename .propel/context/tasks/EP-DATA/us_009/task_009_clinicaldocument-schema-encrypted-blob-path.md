# Task - TASK_009

## Requirement Reference

- **User Story**: US_009 — ClinicalDocument schema + encrypted blob path
- **Story Location**: `.propel/context/tasks/EP-DATA/us_009/us_009.md`
- **Parent Epic**: EP-DATA

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `ClinicalDocument` entity created in SQL Server with `EncryptedBlobPath` as `nvarchar` (no binary blob in DB) |
| AC-002 | `VirusScanResult` column defaults to `Pending` |
| AC-003 | Non-clustered index on `PatientId` for efficient patient document queries |
| AC-004 | Migration applies cleanly via `dotnet ef database update` |
| AC-005 | `ClinicalDocument` is registered in `ApplicationDbContext` with correct column types |

### Edge Cases

- Binary document data is never stored in the database; only the encrypted file path on disk is stored
- `VirusScanResult` enum: Pending / Clean / Infected — default value set at the database column level

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

---

## Task Overview

Create the `ClinicalDocument` entity and EF Core migration in `ApplicationDbContext`. Store only the encrypted file path (not binary content). Configure `VirusScanResult` default to `Pending` and a non-clustered index on `PatientId`.

---

## Dependent Tasks

- **TASK_001 (us_003)** — `ApplicationDbContext` infrastructure
- **TASK_001 (us_007)** — `UserAccount` migration (PatientId FK)

---

## Impacted Components

- `src/ClinicalHealthcare.Infrastructure/Entities/ClinicalDocument.cs`
- `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs`
- `src/ClinicalHealthcare.Infrastructure.SqlMigrations/Migrations/`

---

## Implementation Plan

1. Create `ClinicalDocument` entity: `Id`, `PatientId` (FK), `OriginalFileName`, `EncryptedBlobPath` (nvarchar(500)), `VirusScanResult` (enum: Pending/Clean/Infected, default Pending), `OcrStatus`, `UploadedAt`, `UploadedByStaffId`.
2. Configure fluent API: `EncryptedBlobPath` as `nvarchar(500)`; `VirusScanResult` default value `(0)` (Pending); non-clustered index on `PatientId`.
3. Register `ClinicalDocument` in `ApplicationDbContext`.
4. Generate and apply migration `ClinicalDocumentSchema`.
5. Verify `VirusScanResult` column has default constraint in generated SQL.
6. Verify non-clustered index appears in migration.

---

## Current Project State

```
src/ClinicalHealthcare.Infrastructure/Entities/
├── UserAccount.cs
├── Slot.cs
├── Appointment.cs
├── WaitlistEntry.cs
└── IntakeRecord.cs
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Infrastructure/Entities/ClinicalDocument.cs` | ClinicalDocument entity |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs` | Register entity; configure index and default |
| CREATE | `src/ClinicalHealthcare.Infrastructure.SqlMigrations/Migrations/*_ClinicalDocumentSchema.cs` | EF Core migration |

---

## External References

- [EF Core Default Values](https://learn.microsoft.com/en-us/ef/core/modeling/generated-properties)
- [EF Core Indexes](https://learn.microsoft.com/en-us/ef/core/modeling/indexes)

---

## Build Commands

```bash
dotnet ef migrations add ClinicalDocumentSchema --project src/ClinicalHealthcare.Infrastructure.SqlMigrations --startup-project src/ClinicalHealthcare.Api --context ApplicationDbContext
dotnet ef database update --project src/ClinicalHealthcare.Infrastructure.SqlMigrations --startup-project src/ClinicalHealthcare.Api --context ApplicationDbContext
```

---

## Implementation Validation Strategy

- Migration SQL contains `DEFAULT (0)` for `VirusScanResult`.
- Migration SQL contains `CREATE NONCLUSTERED INDEX` on `PatientId`.
- Insert `ClinicalDocument` without setting `VirusScanResult` → DB value = 0 (Pending).
- `EncryptedBlobPath` column is `nvarchar(500)`, not `varbinary`.
- `dotnet build` → 0 errors.

---

## Implementation Checklist

- [x] **[AC-001]** `ClinicalDocument` entity with `EncryptedBlobPath` as `nvarchar(500)` (no binary)
- [x] **[AC-002]** `VirusScanResult` defaults to `Pending (0)` at database column level
- [x] **[AC-003]** Non-clustered index on `PatientId` configured via fluent API
- [x] **[AC-004]** Migration created and applies cleanly
- [x] **[AC-005]** Entity registered in `ApplicationDbContext` with correct column type configuration
- [x] `OcrStatus` column present (Pending/Extracted/LowConfidence/NoData)
- [x] Binary document content never stored in DB
- [x] `dotnet build` passes with 0 errors- [x] **[F1]** `EncryptedBlobPath` setter guard — throws `ArgumentException` on null/whitespace
- [x] **[F3]** `OcrStatus` transition tests (Extracted / LowConfidence / NoData)
- [x] **[F4]** FK Restrict document count test added
- [x] **[F5]** Removed dead `HasDefaultValueSql("GETUTCDATE()")` from fluent config