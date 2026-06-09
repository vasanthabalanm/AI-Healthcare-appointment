# Task - TASK_047

## Requirement Reference

- **User Story**: US_047 — Trust-First Staff code verification interface
- **Story Location**: `.propel/context/tasks/EP-008/us_047/us_047.md`
- **Parent Epic**: EP-008

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `GET /patients/{id}/code-suggestions` returns grouped Pending suggestions |
| AC-002 | `PATCH /code-suggestions/{id} {status:"Accepted", verifiedById}` → 200; stores verifiedById + verifiedAt |
| AC-003 | `PATCH` without `verifiedById` → 422 `{"error":"verifiedById is required for code verification"}` |
| AC-004 | `POST /patients/{id}/code-suggestions/accept-all {verifiedById}` → all Pending → Accepted |
| AC-005 | `POST /patients/{id}/coding-complete` → 409 if any Pending remain |
| AC-006 | `AuditLog` entry for every verification action (accept/reject/accept-all/coding-complete) |

### Edge Cases

- Staff modifies `committedCode` (differs from `suggestedCode`) → counts as "Modified" in metrics (us_048)
- Already-Accepted PATCH → 409

---

## Design References

N/A — UI Impact: No

---

## AI References

N/A — AI Impact: No (Trust-First enforcement on AI outputs; no AI invoked here)

---

## Mobile References

N/A — Mobile Impact: No

---

## Applicable Technology Stack

| Layer | Technology | Version | Justification |
|-------|-----------|---------|---------------|
| Backend | ASP.NET Core Web API | 8 LTS | Endpoint hosting |
| Database | SQL Server 2022 | Express | `MedicalCodeSuggestion` + `AuditLog` in `ApplicationDbContext` |
| Backend | EF Core | 8.x | SQL Server provider |

---

## Task Overview

Implement the 4 Trust-First verification endpoints: GET suggestions, PATCH single accept/reject, POST accept-all, POST coding-complete. Require `verifiedById` for acceptance. Write `AuditLog` entries for all actions.

---

## Dependent Tasks

- **TASK_001 (us_045)** — `MedicalCodeSuggestion` rows (ICD-10) inserted by generation job
- **TASK_001 (us_046)** — `MedicalCodeSuggestion` rows (CPT) inserted by generation job

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Coding/GetCodeSuggestionsEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Coding/PatchCodeSuggestionEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Coding/AcceptAllCodeSuggestionsEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Coding/CodingCompleteEndpoint.cs`

---

## Implementation Plan

1. `GET /patients/{id}/code-suggestions` (`[Authorize(Roles="Staff,Admin")]`): query `MedicalCodeSuggestion` for patient grouped by `codeType` (ICD10/CPT); return Pending suggestions list.
2. `PATCH /code-suggestions/{id}` (`[Authorize(Roles="Staff,Admin")]`):
   - Validate body: `status` is `Accepted` or `Rejected`; if `status=Accepted` and `verifiedById` missing → 422 `{"error":"verifiedById is required for code verification"}`.
   - Load suggestion → 404 if not found; if `Status=Accepted` already → 409.
   - If `committedCode` provided and differs from `suggestedCode` → mark as Modified (store `committedCode`).
   - Set `Status`, `VerifiedById`, `VerifiedAt=UtcNow`; save.
   - Write `AuditLog`.
3. `POST /patients/{id}/code-suggestions/accept-all` (`[Authorize(Roles="Staff,Admin")]`):
   - Validate `verifiedById` in body → 422 if missing.
   - Set all Pending suggestions for patient to `Accepted`, `VerifiedById`, `VerifiedAt=UtcNow`; save in bulk.
   - Write single `AuditLog` entry.
4. `POST /patients/{id}/coding-complete` (`[Authorize(Roles="Staff,Admin")]`):
   - Count Pending suggestions for patient → 409 `"All code suggestions must be reviewed before coding-complete"` if any remain.
   - Set patient `CodingStatus=Complete`; save.
   - Write `AuditLog`.

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Coding/
└── GenerateCodesEndpoint.cs
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/Coding/GetCodeSuggestionsEndpoint.cs` | GET grouped suggestions |
| CREATE | `src/ClinicalHealthcare.Api/Features/Coding/PatchCodeSuggestionEndpoint.cs` | PATCH single accept/reject |
| CREATE | `src/ClinicalHealthcare.Api/Features/Coding/AcceptAllCodeSuggestionsEndpoint.cs` | POST accept-all |
| CREATE | `src/ClinicalHealthcare.Api/Features/Coding/CodingCompleteEndpoint.cs` | POST coding-complete gate |

---

## External References

- [ASP.NET Core Authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/)

---

## Build Commands

```bash
dotnet build
```

---

## Implementation Validation Strategy

- `GET /code-suggestions` → returns grouped Pending rows.
- `PATCH` with `verifiedById` → 200; `VerifiedById` + `VerifiedAt` stored.
- `PATCH` without `verifiedById` → 422 with exact error message.
- `PATCH` already-Accepted → 409.
- `POST accept-all` → all Pending → Accepted.
- `POST coding-complete` with Pending remaining → 409.
- `POST coding-complete` with no Pending → 200.
- Every action → `AuditLog` entry present.

---

## Implementation Checklist

- [x] **[AC-001]** `GET /code-suggestions` returns Pending suggestions grouped by codeType
- [x] **[AC-002]** `PATCH` accept/reject stores `verifiedById` + `verifiedAt`
- [x] **[AC-003]** `PATCH` without `verifiedById` → 422 with required error message
- [x] **[AC-004]** `POST accept-all` transitions all Pending to Accepted
- [x] **[AC-005]** `POST coding-complete` → 409 if any Pending remain
- [x] **[AC-006]** `AuditLog` entry written for every verification action
- [x] Already-Accepted suggestion PATCH → 409
- [x] `dotnet build` passes with 0 errors
