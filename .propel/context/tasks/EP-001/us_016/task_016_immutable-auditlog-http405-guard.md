# Task - TASK_016

## Requirement Reference

- **User Story**: US_016 — Immutable audit log + HTTP 405 guard
- **Story Location**: `.propel/context/tasks/EP-001/us_016/us_016.md`
- **Parent Epic**: EP-001

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `GET /audit` returns paginated AuditLog (50 per page) |
| AC-002 | CSV export: ≤10k rows synchronously; >10k rows via Hangfire async job |
| AC-003 | `DELETE /audit` and `PATCH /audit` return HTTP 405 Method Not Allowed and are logged |
| AC-004 | AuditLog rows are INSERT-only; no UPDATE or DELETE via application layer |
| AC-005 | Admin role required for all `/audit` endpoints |

### Edge Cases

- CSV export job completes → notify via email or polling endpoint (future); for now, job enqueued and 202 returned
- Attempt to call `context.AuditLogs.Remove(...)` in code → blocked by DB-level REVOKE (from us_011)

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
| Backend | ASP.NET Core Web API | 8 LTS | Endpoint hosting |
| Backend | Hangfire | 1.8.x | Async CSV export per design.md |
| Database | SQL Server | 2022 / Express | `AuditLog` storage |

---

## Task Overview

Implement `GET /audit` with pagination (50/page) and CSV export (sync ≤10k, async >10k via Hangfire). Add explicit route handlers for `DELETE /audit` and `PATCH /audit` that return 405 and log the attempt. All endpoints require Admin role.

---

## Dependent Tasks

- **TASK_001 (us_011)** — `AuditLog` entity + INSERT-only DB grant
- **TASK_001 (us_004)** — Hangfire infrastructure

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Admin/GetAuditLogEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Admin/ExportAuditLogEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Admin/AuditLogGuardEndpoints.cs`

---

## Implementation Plan

1. Implement `GET /audit?page=1&pageSize=50` (`[Authorize(Roles="Admin")]`): query `AuditLog` ordered by `OccurredAt DESC`; return paginated response with `totalCount`, `pageCount`, `data[]`.
2. Implement `GET /audit/export?format=csv`: count rows; if ≤10k → generate CSV synchronously via `StringBuilder`; return as `text/csv` attachment; if >10k → enqueue Hangfire `ExportAuditLogJob`; return 202 `{"message":"Export queued","jobId":"..."}`.
3. Register explicit route handlers for `DELETE /audit` and `PATCH /audit`: return `405 Method Not Allowed`; write AuditLog entry with `Action=HTTP-405-Attempt`, `ActorId` from JWT (or anonymous), `AfterValue="{method} /audit"`.
4. Ensure `AuditLog` is only ever inserted (no Update/Remove calls in application code).
5. Create `ExportAuditLogJob` Hangfire background job class (stub for now; full CSV generation in job body).

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Admin/
├── CreateUserEndpoint.cs
└── UpdateUserEndpoint.cs
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/Admin/GetAuditLogEndpoint.cs` | GET /audit paginated |
| CREATE | `src/ClinicalHealthcare.Api/Features/Admin/ExportAuditLogEndpoint.cs` | GET /audit/export sync/async |
| CREATE | `src/ClinicalHealthcare.Api/Features/Admin/AuditLogGuardEndpoints.cs` | DELETE+PATCH /audit → 405 |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Jobs/ExportAuditLogJob.cs` | Hangfire async CSV export job |

---

## External References

- [Hangfire Background Jobs](https://docs.hangfire.io/en/latest/background-jobs/enqueueing-jobs.html)
- [ASP.NET Core Minimal API Route Handlers](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis)

---

## Build Commands

```bash
dotnet build
```

---

## Implementation Validation Strategy

- `GET /audit?page=1` → 200 with 50-row page.
- `GET /audit/export` with 5k rows → CSV attachment; 200.
- `GET /audit/export` with >10k rows → 202 + Hangfire job enqueued.
- `DELETE /audit` → 405; AuditLog entry written.
- `PATCH /audit` → 405; AuditLog entry written.
- Staff JWT → 403 on all `/audit` endpoints.

---

## Implementation Checklist

- [x] **[AC-001]** `GET /audit` returns paginated results (50/page) with total count
- [x] **[AC-002]** CSV export: sync ≤10k; Hangfire async >10k with 202 response
- [x] **[AC-003]** `DELETE /audit` and `PATCH /audit` return 405 and write AuditLog entry
- [x] **[AC-004]** No `Remove()` or `Update()` calls on `AuditLog` in application code
- [x] **[AC-005]** All `/audit` endpoints require `[Authorize(Roles="Admin")]`
- [x] Pagination includes `totalCount` and `pageCount` in response
- [x] `ExportAuditLogJob` Hangfire job class created
- [x] `dotnet build` passes with 0 errors
