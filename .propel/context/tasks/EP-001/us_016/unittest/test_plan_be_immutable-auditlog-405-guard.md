# Unit Test Plan - TASK_016

## Requirement Reference
- **User Story**: us_016
- **Story Location**: `.propel/context/tasks/EP-001/us_016/us_016.md`
- **Layer**: BE
- **Related Test Plans**: None (single-layer feature)
- **Acceptance Criteria Covered**:
  - AC-001: Every CRUD action generates an AuditLog entry via `AuditLogHelper.Stage`
  - AC-002: Admin `GET /admin/audit` returns paginated entries ordered by `OccurredAt DESC`
  - AC-003: ≤10,000 rows → CSV returned synchronously (HTTP 200)
  - AC-004: >10,000 rows → Hangfire job enqueued (HTTP 202)
  - AC-005: `DELETE /audit` or `PATCH /audit` → HTTP 405 + AuditLog entry for attempt

## Test Plan Overview
Covers the immutable audit log vertical slice: `GetAuditLogEndpoint` (pagination), `ExportAuditLogEndpoint` (sync/async CSV), `AuditLogGuardEndpoints` (405 guard + tamper audit), and `AuditLogHelper.Stage` (append-only staging). Hangfire `IBackgroundJobClient` is mocked with Moq. Authorization (AdminOnly policy) is declared on all endpoints and verified by `EndpointAuthorizationConventionTests`. Tests call handler methods directly with InMemory EF Core.

## Dependent Tasks
- TASK_007 — `AuditLog` entity must exist in `ApplicationDbContext`
- TASK_011 — `AuditLog` INSERT-only DB permission (integration test concern; unit tests use InMemory)

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `GetAuditLogEndpoint.HandleGetAuditLog` | static method | `src/ClinicalHealthcare.Api/Features/Admin/GetAuditLogEndpoint.cs` | Paginated query ordered by OccurredAt DESC; clamped page size |
| `ExportAuditLogEndpoint.HandleExportAuditLog` | static method | `src/ClinicalHealthcare.Api/Features/Admin/ExportAuditLogEndpoint.cs` | Sync CSV (≤10k) or async Hangfire job (>10k) |
| `AuditLogGuardEndpoints.HandleDeleteAudit` | static method | `src/ClinicalHealthcare.Api/Features/Admin/AuditLogGuardEndpoints.cs` | Returns 405; logs deletion attempt |
| `AuditLogGuardEndpoints.HandlePatchAudit` | static method | `src/ClinicalHealthcare.Api/Features/Admin/AuditLogGuardEndpoints.cs` | Returns 405; logs patch attempt |
| `AuditLogHelper.Stage` | static method | `src/ClinicalHealthcare.Infrastructure/Helpers/AuditLogHelper.cs` | Appends AuditLog to DbContext change tracker; no SaveChanges |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 [SOURCE:INPUT] | positive | Paginated audit returns 200 with metadata | 55 AuditLog rows seeded; `page=1`, `pageSize=50` | `HandleGetAuditLog` called | HTTP 200; 50 entries; totalCount=55; pageCount=2 | Status 200; `body.Data.Count == 50`; `body.TotalCount == 55` — Basis: AC-002 |
| TC-002 [SOURCE:INPUT] | positive | Page 2 returns remaining 5 rows | 55 rows seeded | `HandleGetAuditLog` with `page=2`, `pageSize=50` | HTTP 200; 5 entries | `body.Data.Count == 5` — Basis: AC-002 |
| TC-003 [SOURCE:INPUT] | positive | Entries ordered by OccurredAt descending | 3 rows seeded with deterministic timestamps | `HandleGetAuditLog` | First entry has latest timestamp | `body.Data[0].OccurredAt >= body.Data[1].OccurredAt` — Basis: AC-002 |
| TC-004 [SOURCE:INPUT] | positive | ≤10,000 rows returns 200 with CSV content | 5 AuditLog rows seeded | `HandleExportAuditLog` called with `format=csv` | HTTP 200; CSV headers present | Status 200; response body contains `"Id,EntityType"` header line — Basis: AC-003 |
| TC-005 [SOURCE:INPUT] | positive | >10,000 rows returns 202 with jobId | `db.AuditLogs.Count()` returns 10001 via seeding | `HandleExportAuditLog` called | HTTP 202; `message` and `jobId` in body | Status 202; `body.Message == "Export queued"` — Basis: AC-004 |
| TC-006 [SOURCE:INPUT] | positive | DELETE /audit returns 405 and writes AuditLog entry | Admin `HttpContext` with actorId | `HandleDeleteAudit` called | HTTP 405; AuditLog row inserted recording attempt | Status 405; `db.AuditLogs.Count() == 1` — Basis: AC-005 |
| TC-007 [SOURCE:INPUT] | positive | PATCH /audit returns 405 and writes AuditLog entry | Admin `HttpContext` with actorId | `HandlePatchAudit` called | HTTP 405; AuditLog row inserted | Status 405; `db.AuditLogs.Count() == 1` — Basis: AC-005 |
| TC-008 [SOURCE:INPUT] | positive | AuditLogHelper.Stage adds entry to change tracker correctly | Fresh InMemory DbContext | `AuditLogHelper.Stage(db, "UserAccount", 1, 99, "INSERT", null, afterObj)` called | AuditLog entity staged; not yet saved | `db.AuditLogs.Local.Count == 1`; `Action == "INSERT"`; `AfterValue != null` — Basis: AC-001 |
| EC-001 [SOURCE:INFERRED] | edge_case | pageSize > 50 clamped to DefaultPageSize | 10 rows seeded | `HandleGetAuditLog` with `pageSize=200` | HTTP 200; all 10 rows returned (within clamped 50) | `body.Data.Count == 10`; no exception — Basis: clamp prevents over-fetching |
| EC-002 [SOURCE:INFERRED] | edge_case | Unsupported export format returns 400 | Fresh DB | `HandleExportAuditLog` with `format=xml"` | HTTP 400 | `IStatusCodeHttpResult.StatusCode == 400` — Basis: handler validates `format == "csv"` only |
| ES-001 [SOURCE:INFERRED] | error | Empty AuditLog table returns 200 with empty CSV | Empty DB | `HandleExportAuditLog` with `format=csv` | HTTP 200; CSV contains only header row | Status 200; CSV header present; no data rows — Basis: empty dataset is valid; no error expected |

## AI Component Test Cases
> Skipped — AI Impact = No (no AIR-XXX requirements in scope for this story).

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE (exists) | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/AuditLogEndpointTests.cs` | Audit log endpoint tests — already implemented |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `IBackgroundJobClient` | `Moq<IBackgroundJobClient>` | `Enqueue<ExportAuditLogJob>(...)` captured | Returns fake job ID `"job-001"` |
| `ApplicationDbContext` | EF Core InMemory | Isolated per-test; `TransactionIgnoredWarning` suppressed | In-memory store |
| `ILoggerFactory` | `LoggerFactory.Create(_ => {})` | No-op logger | ILoggerFactory |
| `HttpContext` | `new DefaultHttpContext { User = adminClaimsPrincipal }` | Actor ID = 1; role = "admin" | DefaultHttpContext |

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Paginated query | 55 AuditLog rows, `page=1, pageSize=50` | 50 rows, totalCount=55, pageCount=2 |
| Page 2 | Same 55 rows, `page=2` | 5 rows |
| CSV sync | ≤10,000 rows, `format=csv` | HTTP 200 CSV response |
| Async queue | >10,000 rows, `format=csv` | HTTP 202 with jobId |
| 405 guard | DELETE or PATCH on `/audit` | HTTP 405 + AuditLog entry |
| Unsupported format | `format=xml` | HTTP 400 |
| Empty DB export | 0 rows, `format=csv` | HTTP 200 CSV header only |

## Test Commands
- **Run Tests**: `dotnet test ClinicalHealthcare.slnx --filter "FullyQualifiedName~AuditLogEndpointTests" -v q`
- **Run with Coverage**: `dotnet test ClinicalHealthcare.slnx --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~AuditLogEndpointTests"`
- **Run Single Test**: `dotnet test ClinicalHealthcare.slnx --filter "FullyQualifiedName~AuditLogEndpointTests.GetAuditLog_Returns200_WithPaginatedData"`

## Coverage Target
- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**: `ExportAuditLogEndpoint` sync/async threshold branch; `AuditLogGuardEndpoints` 405 response + audit write; `GetAuditLogEndpoint` page-size clamp must have 100% coverage

## Documentation References
- **Framework Docs**: xUnit 2.x — https://xunit.net/docs
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/AuditLogEndpointTests.cs`
- **Mocking Guide**: `Moq<IBackgroundJobClient>` for Hangfire background job queueing

## Implementation Checklist
- [x] Create test file structure per Expected Changes (`AuditLogEndpointTests.cs` exists)
- [x] Set up test data fixtures per Test Data section (`SeedAuditLogs` helper)
- [x] Configure mocking dependencies per Mocking Strategy (Moq IBackgroundJobClient)
- [x] Implement positive test cases (TC-001 through TC-008)
- [x] Implement negative test cases (covered by EC-002 and 405 guard tests)
- [x] Implement edge case tests (EC-001, EC-002)
- [x] Implement error scenario tests (ES-001)
- [x] Run test suite and validate coverage meets target (101/101 tests green — 2026-05-24)
