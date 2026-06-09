# Task - TASK_042

## Requirement Reference

- **User Story**: US_042 — De-duplication via Hangfire aggregation job
- **Story Location**: `.propel/context/tasks/EP-007/us_042/us_042.md`
- **Parent Epic**: EP-007

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `DeduplicateClinicalFieldsJob` runs after extraction; same value → soft-delete older |
| AC-002 | Conflicting values → insert `ConflictFlag` in PostgreSQL |
| AC-003 | Redis 360° view cache invalidated for the patient after deduplication |
| AC-004 | Patient-scoped distributed lock prevents concurrent deduplication for same patient |

### Edge Cases

- Two identical field values from different documents → soft-delete the older; keep newer
- Two conflicting values (different non-null values for same field) → create `ConflictFlag`; both rows remain until Staff resolution

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
| Backend | Hangfire | 1.8.x | Aggregation job per design.md |
| Database | PostgreSQL | 16.x | `ExtractedClinicalField` + `ConflictFlag` |
| Infrastructure | Upstash Redis | N/A | 360° cache invalidation + distributed lock |

---

## Task Overview

Implement `DeduplicateClinicalFieldsJob`. Acquire patient-scoped Redis distributed lock. Group `ExtractedClinicalField` by `FieldName`. Soft-delete duplicate same-values. Create `ConflictFlag` for conflicting values. Invalidate 360° Redis cache.

---

## Dependent Tasks

- **TASK_001 (us_041)** — `ExtractClinicalFieldsJob` enqueues this job
- **TASK_001 (us_010)** — `ExtractedClinicalField` + `ConflictFlag` entities
- **TASK_001 (us_004)** — Redis `ICacheService`

---

## Impacted Components

- `src/ClinicalHealthcare.Infrastructure/Jobs/DeduplicateClinicalFieldsJob.cs`

---

## Implementation Plan

1. Implement `DeduplicateClinicalFieldsJob.Execute(patientId)`:
   - Acquire Redis distributed lock `dedup-lock:{patientId}` with 30s TTL (StackExchange.Redis `LockTake`); if lock not acquired → reschedule.
   - Load all non-deleted `ExtractedClinicalField` for patient from `ClinicalDbContext`.
   - Group by `FieldName`: within each group, if all values equal → keep newest; soft-delete older ones (`IsDeleted=true`).
   - If conflicting values (different non-null values in same field group) → check if `ConflictFlag` already exists for this `FieldName`; if not → insert new `ConflictFlag` with `Status=Unresolved`.
   - Save via `ClinicalDbContext.SaveChangesAsync()`.
   - Release lock.
   - Invalidate Redis `360view:{patientId}` cache key via `ICacheService.DeleteAsync(...)`.
2. Enqueue `DeduplicateClinicalFieldsJob` from `ExtractClinicalFieldsJob` (us_041).

---

## Current Project State

```
src/ClinicalHealthcare.Infrastructure/Jobs/
├── OcrDocumentJob.cs
└── ExtractClinicalFieldsJob.cs
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Infrastructure/Jobs/DeduplicateClinicalFieldsJob.cs` | Hangfire de-duplication job |

---

## External References

- [StackExchange.Redis Distributed Lock](https://stackexchange.github.io/StackExchange.Redis/Locks)
- [EF Core Soft Delete](https://learn.microsoft.com/en-us/ef/core/querying/filters)

---

## Build Commands

```bash
dotnet build
```

---

## Implementation Validation Strategy

- Two identical `FieldValue` for same `FieldName` and patient → older soft-deleted; newer retained.
- Two conflicting `FieldValue` for same `FieldName` → `ConflictFlag` inserted with `Status=Unresolved`.
- `360view:{patientId}` Redis key deleted after job.
- Concurrent job for same patient → second waits for lock or reschedules.

---

## Implementation Checklist

- [x] **[AC-001]** Same value → soft-delete older `ExtractedClinicalField`
- [x] **[AC-002]** Conflicting values → `ConflictFlag` inserted with `Status=Unresolved`
- [x] **[AC-003]** Redis `360view:{patientId}` invalidated after deduplication
- [x] **[AC-004]** Patient-scoped Redis distributed lock (`dedup-lock:{patientId}`)
- [x] Grouping by `FieldName` across all documents for the patient
- [x] Duplicate `ConflictFlag` for same `FieldName` not created if one already exists (Unresolved)
- [x] Lock released after job completion (including on error)
- [x] `dotnet build` passes with 0 errors
