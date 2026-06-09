# Task - TASK_044

## Requirement Reference

- **User Story**: US_044 — 360° patient view + Redis cache + Trust-First verify
- **Story Location**: `.propel/context/tasks/EP-007/us_044/us_044.md`
- **Parent Epic**: EP-007

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `GET /patients/{id}/360-view` returns assembled view; cache TTL=300s |
| AC-002 | Cache miss → assemble from PostgreSQL; cache hit → return cached |
| AC-003 | `PATCH /patients/{id}/verify` transitions patient to `ClinicalStatus=Verified` |
| AC-004 | Verify blocked (409) if any Unresolved `ConflictFlag` exists |
| AC-005 | New document upload (us_038) invalidates `360view:{patientId}` cache key |

### Edge Cases

- Verify on already-Verified patient → 409
- Patient has no extracted fields → empty sections in view; verify allowed if no conflicts

---

## Design References

N/A — UI Impact: No

---

## AI References

- **AI Platform**: Trust-First pattern — no AI output committed without `verified_by` staff id
- **Reference**: AIR-005 (Trust-First verify)

---

## Mobile References

N/A — Mobile Impact: No

---

## Applicable Technology Stack

| Layer | Technology | Version | Justification |
|-------|-----------|---------|---------------|
| Backend | ASP.NET Core Web API | 8 LTS | Endpoint hosting |
| Database | PostgreSQL | 16.x | Clinical data assembly |
| Infrastructure | Upstash Redis | N/A | 360° view cache TTL=300s |
| Backend | EF Core | 8.x | Npgsql provider |

---

## Task Overview

Implement `GET /patients/{id}/360-view` with Redis cache (TTL=300s). Implement `PATCH /patients/{id}/verify` with unresolved conflict guard. Invalidate cache on new document upload.

---

## Dependent Tasks

- **TASK_001 (us_041)** — `ExtractedClinicalField` rows assembled in view
- **TASK_001 (us_042)** — Redis cache invalidated after dedup
- **TASK_001 (us_043)** — `IConflictService.HasUnresolvedConflicts` for verify gate
- **TASK_001 (us_004)** — Redis `ICacheService`
- **TASK_001 (us_038)** — Document upload should invalidate cache key

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Patients/Get360ViewEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Patients/VerifyPatientEndpoint.cs`

---

## Implementation Plan

1. Implement `GET /patients/{id}/360-view` (`[Authorize(Roles="Staff,Admin")]`):
   - Check Redis key `360view:{patientId}`: cache hit → deserialize + return 200.
   - Cache miss → query `ExtractedClinicalField` by patientId from `ClinicalDbContext`; group by `FieldType`; build `PatientView360Dto` (sections per field type + unresolved conflict count); serialize to Redis `360view:{patientId}` TTL=300s; return 200.
2. Implement `PATCH /patients/{id}/verify` (`[Authorize(Roles="Staff,Admin")]`):
   - Call `IConflictService.HasUnresolvedConflicts(patientId)` → 409 `"Unresolved conflicts must be resolved before verification"`.
   - Check patient `ClinicalStatus != Verified` → 409 if already verified.
   - Set `ClinicalStatus=Verified`, `VerifiedByStaffId=JWT.sub`, `VerifiedAt=UtcNow`; save.
   - Return 200.
3. In `DocumentUploadEndpoint` (us_038): after saving document, call `ICacheService.DeleteAsync("360view:{patientId}")`.

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Patients/
src/ClinicalHealthcare.Infrastructure/Services/ConflictService.cs
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/Patients/Get360ViewEndpoint.cs` | GET 360° view with Redis cache |
| CREATE | `src/ClinicalHealthcare.Api/Features/Patients/VerifyPatientEndpoint.cs` | PATCH /patients/{id}/verify |
| MODIFY | `src/ClinicalHealthcare.Api/Features/Documents/DocumentUploadEndpoint.cs` | Invalidate 360° cache on new upload |

---

## External References

- [StackExchange.Redis](https://stackexchange.github.io/StackExchange.Redis/)

---

## Build Commands

```bash
dotnet build
```

---

## Implementation Validation Strategy

- First `GET /patients/{id}/360-view` → Redis miss → assembled from DB; subsequent → cache hit.
- After new document upload → cache key deleted → next GET reassembles.
- `PATCH /verify` with unresolved conflict → 409.
- `PATCH /verify` with no unresolved conflicts → 200; `ClinicalStatus=Verified`.
- `PATCH /verify` on already-Verified patient → 409.

---

## Implementation Checklist

- [x] **[AC-001]** `GET 360-view` returns assembled view; cached with TTL=300s
- [x] **[AC-002]** Cache miss → PostgreSQL assembly; cache hit → return Redis value
- [x] **[AC-003]** `PATCH /verify` sets `ClinicalStatus=Verified` with `VerifiedByStaffId`
- [x] **[AC-004]** Verify returns 409 if any Unresolved `ConflictFlag` exists
- [x] **[AC-005]** Document upload endpoint invalidates `360view:{patientId}` key
- [x] Already-Verified patient verify → 409
- [x] All endpoints require `[Authorize(Roles="Staff,Admin")]`
- [x] `dotnet build` passes with 0 errors
