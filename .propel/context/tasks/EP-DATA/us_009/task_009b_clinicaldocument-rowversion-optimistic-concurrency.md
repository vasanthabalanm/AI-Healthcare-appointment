# Task - TASK_009B

## Requirement Reference

- **User Story**: US_009 — ClinicalDocument schema + encrypted blob path (follow-up)
- **Story Location**: `.propel/context/tasks/EP-DATA/us_009/us_009.md`
- **Parent Epic**: EP-DATA
- **Origin**: Identified during `analyze-implementation` review of TASK_009 (F2 gap)

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `ClinicalDocument` entity has `RowVersion` (`byte[]`) column mapped to SQL Server `rowversion` |
| AC-002 | `RowVersion` is configured via `.IsRowVersion()` in fluent API |
| AC-003 | Migration adds `rowversion NOT NULL` column to `ClinicalDocuments` table |
| AC-004 | Entity carries `[Timestamp]` attribute for EF Core concurrency token recognition |

### Edge Cases

- EF Core InMemory provider does not enforce `rowversion` semantics — `DbUpdateConcurrencyException` is only raised on SQL Server. Unit tests verify entity contract; SQL Server integration tests are required for full behavioural coverage (deferred to background worker epic).
- `RowVersion` is DB-generated and must never be set manually by application code.

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
| Database | SQL Server | 2022 / Express | `rowversion` is a SQL Server-native auto-increment binary column |
| Backend | EF Core | 8.x | `.IsRowVersion()` fluent API + `[Timestamp]` attribute |

---

## Task Overview

Add optimistic concurrency protection to `ClinicalDocument` to prevent lost updates when background workers (virus scanner, OCR pipeline) update `VirusScanResult` and `OcrStatus` concurrently. The fix adds a `rowversion` column via a new EF Core migration and configures it as the concurrency token.

---

## Dependent Tasks

- **TASK_009** — `ClinicalDocument` entity and initial migration must already exist

---

## Impacted Components

- `src/ClinicalHealthcare.Infrastructure/Entities/ClinicalDocument.cs`
- `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs`
- `src/ClinicalHealthcare.Infrastructure.SqlMigrations/Migrations/`
- `tests/ClinicalHealthcare.Infrastructure.Tests/Entities/ClinicalDocumentTests.cs`

---

## Implementation Plan

1. Add `[Timestamp] public byte[] RowVersion { get; set; }` to `ClinicalDocument` entity.
2. Add `.IsRowVersion()` fluent config in `ApplicationDbContext.OnModelCreating`.
3. Generate migration `ClinicalDocumentRowVersion`.
4. Apply migration — verifies `ALTER TABLE [ClinicalDocuments] ADD [RowVersion] rowversion NOT NULL`.
5. Add unit test validating `[Timestamp]` attribute + `byte[]` type on `RowVersion` property.

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Entities/ClinicalDocument.cs` | Add `[Timestamp] byte[] RowVersion` property |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs` | Add `.IsRowVersion()` fluent config |
| CREATE | `src/ClinicalHealthcare.Infrastructure.SqlMigrations/Migrations/*_ClinicalDocumentRowVersion.cs` | EF Core migration |
| MODIFY | `tests/.../Entities/ClinicalDocumentTests.cs` | Add `RowVersion_IsTimestampByteArray` test |

---

## Build Commands

```bash
dotnet ef migrations add ClinicalDocumentRowVersion --project src/ClinicalHealthcare.Infrastructure.SqlMigrations --startup-project src/ClinicalHealthcare.Api --context ApplicationDbContext
dotnet ef database update --project src/ClinicalHealthcare.Infrastructure.SqlMigrations --startup-project src/ClinicalHealthcare.Api --context ApplicationDbContext
```

---

## Implementation Checklist

- [x] **[AC-001]** `RowVersion` property added to `ClinicalDocument` as `byte[]`
- [x] **[AC-002]** `.IsRowVersion()` configured in fluent API
- [x] **[AC-003]** Migration `20260514072726_ClinicalDocumentRowVersion` adds `rowversion NOT NULL`
- [x] **[AC-004]** `[Timestamp]` attribute present — verified by unit test
- [x] `dotnet build` passes with 0 errors
- [x] 42/42 tests pass
- [ ] SQL Server integration test for `DbUpdateConcurrencyException` — deferred to background worker epic
