# Task - TASK_048

## Requirement Reference

- **User Story**: US_048 — AI-Human Agreement Rate metric for Admin dashboard
- **Story Location**: `.propel/context/tasks/EP-008/us_048/us_048.md`
- **Parent Epic**: EP-008

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `GET /admin/metrics/code-agreement?days=30` → `{agreementRate, totalActioned, accepted, modified, rejected, windowDays}` |
| AC-002 | `agreementRate = count(Accepted where committedCode == suggestedCode) / totalActioned` |
| AC-003 | Admin role only → 403 for Staff/Patient |
| AC-004 | Zero actioned → `{agreementRate:null, totalActioned:0, message:"No code suggestions actioned..."}` |
| AC-005 | Custom `?days=N` param; default 30; `days=0` or negative → 422; `days>365` → cap at 365 + WARNING |

### Edge Cases

- `days` not provided → default 30
- Mixed ICD-10 and CPT in window — both types counted together

---

## Design References

N/A — UI Impact: No

---

## AI References

N/A — AI Impact: No (metrics endpoint for AI-generated code agreement tracking)

---

## Mobile References

N/A — Mobile Impact: No

---

## Applicable Technology Stack

| Layer | Technology | Version | Justification |
|-------|-----------|---------|---------------|
| Backend | ASP.NET Core Web API | 8 LTS | Endpoint hosting |
| Database | SQL Server 2022 | Express | `MedicalCodeSuggestion` query in `ApplicationDbContext` |
| Backend | EF Core | 8.x | SQL Server LINQ query |

---

## Task Overview

Implement `GET /admin/metrics/code-agreement?days=N`. Validate `days` parameter (0/negative → 422; >365 → cap). Query `MedicalCodeSuggestion` in window. Calculate agreement rate. Return response DTO. Admin-only authorization.

---

## Dependent Tasks

- **TASK_001 (us_047)** — `MedicalCodeSuggestion` rows have `Status`, `committedCode`, `suggestedCode` populated by verification

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Admin/CodeAgreementMetricEndpoint.cs`

---

## Implementation Plan

1. Implement `GET /admin/metrics/code-agreement` (`[Authorize(Roles="Admin")]`):
   - Read `days` query param; default 30 if absent.
   - If `days <= 0` → 422 `{"error":"days parameter must be greater than 0"}`.
   - If `days > 365` → cap to 365; log WARNING `"days capped to 365"`.
   - Query `MedicalCodeSuggestion` where `VerifiedAt >= UtcNow.AddDays(-days)` and `Status != Pending` (i.e., actioned).
   - Compute:
     - `totalActioned = count(all in window)`
     - `accepted = count(Status=Accepted)` 
     - `modified = count(Status=Accepted where committedCode != suggestedCode)`
     - `rejected = count(Status=Rejected)`
     - `agreementRate = count(Status=Accepted AND committedCode == suggestedCode) / totalActioned`
   - If `totalActioned == 0` → return `{agreementRate:null, totalActioned:0, message:"No code suggestions actioned in the selected period"}`.
   - Return 200 with full DTO.

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Admin/
└── README.md
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/Admin/CodeAgreementMetricEndpoint.cs` | GET /admin/metrics/code-agreement |

---

## External References

- [ASP.NET Core Role-Based Authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles)

---

## Build Commands

```bash
dotnet build
```

---

## Implementation Validation Strategy

- `GET ?days=30` with actioned data → `agreementRate` computed; `totalActioned > 0`.
- `GET` with no actioned data in window → `{agreementRate:null, totalActioned:0}`.
- `?days=0` → 422.
- `?days=-1` → 422.
- `?days=400` → capped to 365; WARNING log.
- Staff JWT → 403; Admin JWT → 200.
- `dotnet build` → 0 errors.

---

## Implementation Checklist

- [x] **[AC-001]** Returns `{agreementRate, totalActioned, accepted, modified, rejected, windowDays}`
- [x] **[AC-002]** `agreementRate = accepted-unmodified / totalActioned` (correct formula)
- [x] **[AC-003]** Admin only; Staff/Patient → 403
- [x] **[AC-004]** Zero actioned → `{agreementRate:null, totalActioned:0, message:...}`
- [x] **[AC-005]** Default days=30; days=0 or negative → 422; days>365 → cap + WARNING
- [x] Query filters on `VerifiedAt` window (not `CreatedAt`)
- [x] `modified` count: Accepted rows where `committedCode != suggestedCode`
- [x] `dotnet build` passes with 0 errors
