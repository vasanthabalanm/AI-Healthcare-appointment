# Task - TASK_034

## Requirement Reference

- **User Story**: US_034 — Staff same-day queue view + manual reorder
- **Story Location**: `.propel/context/tasks/EP-005/us_034/us_034.md`
- **Parent Epic**: EP-005

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `GET /staff/queue` returns today's queue ordered by position |
| AC-002 | `PATCH /staff/queue/reorder` accepts ordered list of entry IDs and updates positions |
| AC-003 | Concurrent reorder edit → 409 (optimistic concurrency) |
| AC-004 | `DELETE /staff/queue/{entryId}` removes patient from queue |

### Edge Cases

- Reorder with missing entry IDs (fewer IDs than queue entries) → 400 `{"error":"All queue entry IDs must be included in reorder"}`
- Concurrent `PATCH /staff/queue/reorder` from two sessions → second returns 409

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
| Backend | EF Core | 8.x | Queue management |
| Database | SQL Server | 2022 / Express | QueueEntry storage |

---

## Task Overview

Implement `GET /staff/queue`, `PATCH /staff/queue/reorder`, and `DELETE /staff/queue/{entryId}`. Add optimistic concurrency to detect concurrent reorder conflicts.

---

## Dependent Tasks

- **TASK_001 (us_033)** — `QueueEntry` entity

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Staff/GetQueueEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Staff/ReorderQueueEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Staff/RemoveQueueEntryEndpoint.cs`

---

## Implementation Plan

1. Implement `GET /staff/queue` (`[Authorize(Roles="Staff,Admin")]`): query `QueueEntry` where `QueueDate=today` and `Status=Waiting`; order by `Position ASC`; return `[{entryId, patientId, patientName, position, isWalkIn}]`.
2. Add `RowVersion` (`[Timestamp]`) to `QueueEntry` for optimistic concurrency on reorder; create migration.
3. Implement `PATCH /staff/queue/reorder` (`[Authorize(Roles="Staff,Admin")]`): accept `{orderedIds: [id1, id2, ...], rowVersions: {id: version}}`; validate all current queue entry IDs present in request → 400 if not; loop: set `Position = index`; catch `DbUpdateConcurrencyException` → 409; return 200.
4. Implement `DELETE /staff/queue/{entryId}` (`[Authorize(Roles="Staff,Admin")]`): set `Status=Removed`; return 200.

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Staff/
├── SearchPatientsEndpoint.cs
└── RegisterWalkInEndpoint.cs
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/Staff/GetQueueEndpoint.cs` | GET /staff/queue |
| CREATE | `src/ClinicalHealthcare.Api/Features/Staff/ReorderQueueEndpoint.cs` | PATCH /staff/queue/reorder |
| CREATE | `src/ClinicalHealthcare.Api/Features/Staff/RemoveQueueEntryEndpoint.cs` | DELETE /staff/queue/{entryId} |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Entities/QueueEntry.cs` | Add RowVersion concurrency token |
| CREATE | `src/ClinicalHealthcare.Infrastructure.SqlMigrations/Migrations/*_QueueEntryRowVersion.cs` | Migration |

---

## External References

- [EF Core Concurrency Tokens](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)

---

## Build Commands

```bash
dotnet ef migrations add QueueEntryRowVersion --project src/ClinicalHealthcare.Infrastructure.SqlMigrations --startup-project src/ClinicalHealthcare.Api --context ApplicationDbContext
dotnet build
```

---

## Implementation Validation Strategy

- `GET /staff/queue` → entries ordered by position.
- `PATCH /staff/queue/reorder` → positions updated in DB.
- Concurrent `PATCH` → second returns 409.
- `PATCH` with incomplete IDs → 400.
- `DELETE /staff/queue/{id}` → `Status=Removed`; entry absent from next `GET /staff/queue`.

---

## Implementation Checklist

- [x] **[AC-001]** `GET /staff/queue` returns today's Waiting entries ordered by position
- [x] **[AC-002]** `PATCH /staff/queue/reorder` updates positions from ordered ID list
- [x] **[AC-003]** Concurrent reorder → `DbUpdateConcurrencyException` → 409
- [x] **[AC-004]** `DELETE /staff/queue/{entryId}` sets `Status=Removed`
- [x] Incomplete ID list in reorder → 400
- [x] `QueueEntry.RowVersion` concurrency token added + migration
- [x] All endpoints require `[Authorize(Roles="Staff,Admin")]`
- [x] `dotnet build` passes with 0 errors
