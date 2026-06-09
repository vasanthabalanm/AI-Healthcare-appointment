# Task - TASK_010

## Requirement Reference

- **User Story**: US_010 — ExtractedClinicalField, ConflictFlag, MedicalCodeSuggestion migrations
- **Story Location**: `.propel/context/tasks/EP-DATA/us_010/us_010.md`
- **Parent Epic**: EP-DATA

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `ExtractedClinicalField` entity created in PostgreSQL via `ClinicalDbContext` |
| AC-002 | `ConflictFlag` entity created in PostgreSQL with `Unresolved/Resolved/Dismissed` status |
| AC-003 | `MedicalCodeSuggestion` entity created in PostgreSQL |
| AC-004 | Trust-First CHECK constraint: `verified_by IS NOT NULL` when `status = Accepted` |
| AC-005 | `confidence_score` CHECK constraint: `0.0 ≤ confidence_score ≤ 1.0` |

### Edge Cases

- `verified_by` FK to `UserAccount` must be nullable (null when status = Pending or Rejected)
- CHECK constraint on `verified_by` is enforced at the PostgreSQL level using a partial CHECK: `CHECK (status != 'Accepted' OR verified_by IS NOT NULL)`

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
| Database | PostgreSQL | 16.x | Clinical data DB per design.md |
| Backend | EF Core | 8.x | ORM + migrations per design.md |
| Backend | Npgsql.EntityFrameworkCore.PostgreSQL | 8.x | PostgreSQL provider per design.md |

---

## Task Overview

Create EF Core entity classes and PostgreSQL migrations for `ExtractedClinicalField`, `ConflictFlag`, and `MedicalCodeSuggestion` in `ClinicalDbContext`. Enforce Trust-First CHECK constraint and confidence_score bounds at the PostgreSQL level.

---

## Dependent Tasks

- **TASK_001 (us_003)** — `ClinicalDbContext` and PostgreSQL migration infrastructure

---

## Impacted Components

- `src/ClinicalHealthcare.Infrastructure/Entities/ExtractedClinicalField.cs`
- `src/ClinicalHealthcare.Infrastructure/Entities/ConflictFlag.cs`
- `src/ClinicalHealthcare.Infrastructure/Entities/MedicalCodeSuggestion.cs`
- `src/ClinicalHealthcare.Infrastructure/Data/ClinicalDbContext.cs`
- `src/ClinicalHealthcare.Infrastructure.PgMigrations/Migrations/`

---

## Implementation Plan

1. Create `ExtractedClinicalField`: `Id`, `PatientId`, `DocumentId`, `FieldType` (enum: VitalSign/MedicalHistory/Medication/Allergy/Diagnosis), `FieldName`, `FieldValue`, `ConfidenceScore` (double), `ExtractionJobId`, `ExtractedAt`, `IsDeleted` (soft-delete).
2. Create `ConflictFlag`: `Id`, `PatientId`, `FieldName`, `Value1`, `Value2`, `Status` (enum: Unresolved/Resolved/Dismissed), `ResolvedByStaffId` (nullable), `CreatedAt`.
3. Create `MedicalCodeSuggestion`: `Id`, `PatientId`, `CodeType` (ICD10/CPT), `SuggestedCode`, `CommittedCode` (nullable), `CodeDescription`, `ConfidenceScore` (double), `LowConfidenceFlag`, `Status` (Pending/Accepted/Modified/Rejected), `VerifiedById` (nullable), `VerifiedAt` (nullable).
4. Configure `MedicalCodeSuggestion` CHECK constraints via `HasCheckConstraint`: `confidence_score BETWEEN 0.0 AND 1.0`; `status != 'Accepted' OR verified_by IS NOT NULL`.
5. Register all three entities in `ClinicalDbContext`.
6. Generate and apply migration `ClinicalEntities` on PostgreSQL.
7. Verify CHECK constraints are in generated SQL.

---

## Current Project State

```
src/ClinicalHealthcare.Infrastructure/Data/
└── ClinicalDbContext.cs
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Infrastructure/Entities/ExtractedClinicalField.cs` | Extracted field entity |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Entities/ConflictFlag.cs` | Conflict flag entity |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Entities/MedicalCodeSuggestion.cs` | AI code suggestion entity |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Data/ClinicalDbContext.cs` | Register entities; configure CHECK constraints |
| CREATE | `src/ClinicalHealthcare.Infrastructure.PgMigrations/Migrations/*_ClinicalEntities.cs` | PostgreSQL EF Core migration |

---

## External References

- [EF Core Check Constraints](https://learn.microsoft.com/en-us/ef/core/modeling/indexes#check-constraints)
- [Npgsql EF Core](https://www.npgsql.org/efcore/)

---

## Build Commands

```bash
dotnet ef migrations add ClinicalEntities --project src/ClinicalHealthcare.Infrastructure.PgMigrations --startup-project src/ClinicalHealthcare.Api --context ClinicalDbContext
dotnet ef database update --project src/ClinicalHealthcare.Infrastructure.PgMigrations --startup-project src/ClinicalHealthcare.Api --context ClinicalDbContext
```

---

## Implementation Validation Strategy

- Migration SQL contains `CHECK (confidence_score >= 0.0 AND confidence_score <= 1.0)`.
- Migration SQL contains `CHECK (status != 'Accepted' OR verified_by IS NOT NULL)`.
- Insert `MedicalCodeSuggestion` with `status=Accepted` and `verified_by=null` → PostgreSQL CHECK violation.
- Insert with `confidence_score = 1.5` → CHECK violation.
- `dotnet build` → 0 errors.

---

## Implementation Checklist

- [x] **[AC-001]** `ExtractedClinicalField` entity created and registered in `ClinicalDbContext`
- [x] **[AC-002]** `ConflictFlag` entity with `Unresolved/Resolved/Dismissed` status enum
- [x] **[AC-003]** `MedicalCodeSuggestion` entity with all required fields
- [x] **[AC-004]** Trust-First CHECK: `"Status" != 1 OR "VerifiedById" IS NOT NULL`
- [x] **[AC-005]** `confidence_score CHECK ("ConfidenceScore" >= 0.0 AND "ConfidenceScore" <= 1.0)` constraint configured
- [x] All three entities target PostgreSQL via `ClinicalDbContext`
- [x] Migration created and applies cleanly on PostgreSQL
- [x] `dotnet build` passes with 0 errors
