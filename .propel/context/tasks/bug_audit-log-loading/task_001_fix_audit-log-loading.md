# Bug Fix Task - BUG_001

## Bug Report Reference

- **Reporter**: Developer observation — admin/audit page
- **Reported**: Manual QA — admin/audit component shows loading state; API returns HTTP 200 with data
- **Short name**: `audit-log-loading`
- **Severity**: HIGH
- **Priority**: HIGH

---

## Bug Summary

### Issue Classification

- **Category**: API contract mismatch — JSON property name discrepancy between backend response and frontend interface
- **Layer affected**: Backend (serialization) + Frontend (interface binding)
- **Feature**: Admin → Audit Log (`/admin/audit`)

### Steps to Reproduce

1. Log in as an **Admin** user.
2. Navigate to **Admin → Audit Log**.
3. Observe the component: it briefly shows a loading indicator, then transitions to either an empty state ("No audit entries found") or remains blank — even though the Network tab shows `GET /admin/audit` returning HTTP 200 with a non-empty body.

### Root Cause Analysis

**Root Cause 1 (PRIMARY) — Top-level property name mismatch:**

`GetAuditLogEndpoint.HandleGetAuditLog` (`src/ClinicalHealthcare.Api/Features/Admin/GetAuditLogEndpoint.cs`, line ~80) returns:
```json
{
  "data":       [...],
  "totalCount": 100,
  "pageCount":  5,
  "page":       1,
  "pageSize":   20
}
```

The Angular service interface `AuditLogPage` (`clinical-hub/src/app/core/services/audit.service.ts`) expects:
```typescript
{ items: AuditLogEntry[]; total: number; page: number; pageSize: number; }
```

- `result.items` → `undefined` (JSON has `data`)
- `result.total` → `undefined` (JSON has `totalCount`)
- `this.entries = undefined` (was `[]`) → template `entries.length` falsy → table hidden

**Root Cause 2 (SECONDARY) — Per-item field name mismatch:**

The inner select projection (`GetAuditLogEndpoint.cs`, line ~68) uses `OccurredAt = a.OccurredAt`. No global camelCase serialization policy is configured in `Program.cs`; `System.Text.Json` preserves property names as-is. The JSON field is therefore `"OccurredAt"` (PascalCase).

The frontend `AuditLogEntry` interface expects `timestamp: string`. Even if root cause 1 were fixed, every entry's timestamp would still be `undefined`.

**Why loading appears stuck / table never shows:**

`this.loading = false` IS called in the `next` callback. However, after `this.entries = undefined`, the template condition `*ngIf="!loading && entries.length"` evaluates to `!false && undefined` → falsy → the table is never rendered. The user sees the loading flash resolve into the empty state.

### Impact Assessment

| Dimension        | Detail |
|-----------------|--------|
| **Feature broken** | Admin Audit Log — 100% broken; no audit entries ever display |
| **Affected users** | All Admin role users |
| **Data integrity** | None — data exists in DB; display only |
| **Security** | None — 403 enforcement unchanged; no data leakage |
| **Workaround** | Direct API query (`GET /admin/audit`) is possible but not user-facing |

---

## Fix Overview

Fix the **backend** `GetAuditLogEndpoint.cs` to emit the property names that the documented frontend interface expects:

| Before (backend) | After (backend) | Frontend interface |
|---|---|---|
| `data`       | `items`     | `AuditLogPage.items` |
| `totalCount` | `total`     | `AuditLogPage.total` |
| `OccurredAt` | `timestamp` | `AuditLogEntry.timestamp` |

No frontend changes needed — `audit.service.ts` and `audit.component.ts` already use the correct property names.

**Update regression tests** in `AuditLogEndpointTests.cs` to reflect the new response shape.

---

## Fix Dependencies

- None — self-contained change within the Admin vertical slice.

---

## Impacted Components

| File | Type | Change |
|------|------|--------|
| `src/ClinicalHealthcare.Api/Features/Admin/GetAuditLogEndpoint.cs` | Backend endpoint | Rename `data`→`items`, `totalCount`→`total = totalCount`, `OccurredAt`→`timestamp` |
| `tests/ClinicalHealthcare.Infrastructure.Tests/Features/AuditLogEndpointTests.cs` | Test | Update `PaginatedResponse<T>` DTO properties + all assertions |

---

## Expected Changes

### `GetAuditLogEndpoint.cs` — inner select projection
```csharp
// Before
OccurredAt = a.OccurredAt,

// After
timestamp  = a.OccurredAt,
```

### `GetAuditLogEndpoint.cs` — outer Results.Ok return
```csharp
// Before
return Results.Ok(new
{
    data       = items,
    totalCount,
    pageCount,
    page,
    pageSize
});

// After
return Results.Ok(new
{
    items,
    total      = totalCount,
    pageCount,
    page,
    pageSize
});
```

### `AuditLogEndpointTests.cs` — `PaginatedResponse<T>` helper DTO
```csharp
// Before
public int    TotalCount { get; set; }
public List<T> Data      { get; set; } = [];

// After
public int    Total       { get; set; }
public List<T> Items      { get; set; } = [];
```

### `AuditLogEndpointTests.cs` — assertions
```csharp
// Before → After
body.TotalCount              → body.Total
body.Data.Count              → body.Items.Count
body.Data                    → body.Items
body.Data[n].GetProperty("OccurredAt")  → body.Items[n].GetProperty("timestamp")
```

---

## Implementation Plan

1. Edit `GetAuditLogEndpoint.cs` — inner select: rename `OccurredAt` → `timestamp`
2. Edit `GetAuditLogEndpoint.cs` — outer return: rename `data`→`items`, `totalCount`→`total = totalCount`
3. Edit `AuditLogEndpointTests.cs` — rename `PaginatedResponse<T>` properties
4. Edit `AuditLogEndpointTests.cs` — update all affected assertions
5. Build solution; run all 720+ tests

---

## Regression Prevention Strategy

- Existing tests in `AuditLogEndpointTests.cs` (updated in step 3–4) will assert the correct response shape going forward.
- The test at `GetAuditLog_OrderedByOccurredAtDesc` now verifies the `timestamp` field exists and parses correctly as a `DateTime`, ensuring the PascalCase regression cannot reoccur silently.

---

## Rollback Procedure

Revert the two anonymous-type property name changes in `GetAuditLogEndpoint.cs`:
- `items` → `data = items`
- `total = totalCount` → `totalCount`
- `timestamp = a.OccurredAt` → `OccurredAt = a.OccurredAt`

Revert corresponding test DTO and assertion renames.

---

## External References

- `AuditLogPage` interface: `clinical-hub/src/app/core/services/audit.service.ts`
- `AuditLogEntry` interface: `clinical-hub/src/app/core/services/audit.service.ts`
- Component load logic: `clinical-hub/src/app/features/admin/audit/audit.component.ts` — `loadPage()`

---

## Build Commands

```bash
# Backend build + test
cd d:/BRD-Healthcare/Clinical-Healthcare
dotnet build src/ClinicalHealthcare.Api/ClinicalHealthcare.Api.csproj
dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --no-build --logger "console;verbosity=normal"
```

---

## Implementation Validation Strategy

| Validation | Pass Condition |
|---|---|
| All 720+ tests pass | `dotnet test` exits 0; no audit log tests fail |
| Timestamp ordering test | `GetAuditLog_OrderedByOccurredAtDesc` asserts `timestamp` field; passes |
| Pagination count tests | `body.Total == 55`, `body.Items.Count == 50` |
| Empty table test | `body.Total == 0`, `body.Items` empty |

---

## Implementation Checklist

- [x] `GetAuditLogEndpoint.cs` — inner projection: `OccurredAt` → `timestamp`
- [x] `GetAuditLogEndpoint.cs` — outer return: `data` → `items`, `totalCount` → `total`
- [x] `AuditLogEndpointTests.cs` — `PaginatedResponse<T>`: `Data` → `Items`, `TotalCount` → `Total`
- [x] `AuditLogEndpointTests.cs` — assertions updated (all `body.Data.*` → `body.Items.*`, `body.TotalCount` → `body.Total`, `GetProperty("OccurredAt")` → `GetProperty("timestamp")`)
- [x] `dotnet test` — all tests pass
