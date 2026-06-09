# Unit Test Plan - TASK_034

## Requirement Reference
- **User Story**: us_034
- **Story Location**: `.propel/context/tasks/EP-005/us_034/us_034.md`
- **Layer**: BE
- **Related Test Plans**: `EP-005/us_033/unittest/test_plan_be_staff-walkin-registration-queue.md` (QueueEntry entity from US_033)
- **Acceptance Criteria Covered**:
  - AC-001: `GET /staff/queue` returns today's Waiting entries ordered by Position ASC
  - AC-002: `PATCH /staff/queue/reorder` with ordered IDs updates positions; returns 200
  - AC-003: Concurrent reorder → 409 (`DbUpdateConcurrencyException`)
  - AC-004: `DELETE /staff/queue/{entryId}` sets `Status=Removed`; returns 200

## Test Plan Overview

Tests `GetQueueEndpoint.HandleGetQueue`, `ReorderQueueEndpoint.HandleReorder`, and `RemoveQueueEntryEndpoint.HandleRemove`. `GetQueueEndpoint` scopes to today's `QueueDate` and `Status=Waiting` only. `ReorderQueueEndpoint` validates all current Waiting IDs are present before updating positions; applies caller-supplied `RowVersions` for optimistic concurrency; catches `DbUpdateConcurrencyException` → 409. In-Memory provider does not enforce RowVersion, so TC-004 (concurrent reorder) is tested by injecting a test-subclass that overrides `SaveChangesAsync` to throw `DbUpdateConcurrencyException`. **Gap noted:** AC-002 states an AuditLog entry with before/after positions should be written; the current `ReorderQueueEndpoint` source does not write one.

## Dependent Tasks

- TASK_001 (us_033) — `QueueEntry` entity with `RowVersion` concurrency token

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `GetQueueEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Staff/GetQueueEndpoint.cs` | Return today's Waiting queue ordered by Position |
| `ReorderQueueEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Staff/ReorderQueueEndpoint.cs` | Validate all IDs present; assign positions; handle concurrency conflict |
| `RemoveQueueEntryEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Staff/RemoveQueueEntryEndpoint.cs` | Soft-remove entry (Status=Removed) scoped to today |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | GET returns today's Waiting entries ordered by Position `[SOURCE:INPUT]` | Seed three `QueueEntry` rows for today with `Position=3,1,2` and `Status=Waiting` | `HandleGetQueue(db, ct)` | HTTP 200; entries returned in Position order 1, 2, 3 | `StatusCode==200`; `entries[0].Position==1`; `entries[1].Position==2`; `entries[2].Position==3` |
| TC-002 | positive | Reorder with all valid IDs updates positions → 200 `[SOURCE:INPUT]` | Seed two Waiting entries (id=10 pos=1, id=11 pos=2); request `OrderedIds=[11,10]` with valid RowVersions | `HandleReorder(request, db, ct)` | HTTP 200; entry 11 has Position=1; entry 10 has Position=2 | `StatusCode==200`; `db.QueueEntries.Single(e=>e.Id==11).Position==1`; `db.QueueEntries.Single(e=>e.Id==10).Position==2` |
| TC-003 | negative | Reorder with missing ID → 400 `[SOURCE:INPUT]` | Seed two entries (id=10, id=11); request `OrderedIds=[10]` (missing id=11) | `HandleReorder(request, db, ct)` | HTTP 400; positions unchanged | `StatusCode==400`; `db.QueueEntries.Single(e=>e.Id==11).Position==2` (unchanged) |
| TC-004 | negative | Concurrent reorder → 409 `[SOURCE:INPUT]` | Seed two entries; use `ThrowingConcurrencyDbContext` that throws `DbUpdateConcurrencyException` on `SaveChangesAsync` | `HandleReorder(request, throwingDb, ct)` | HTTP 409 conflict | `StatusCode==409` |
| TC-005 | positive | DELETE soft-removes entry (Status=Removed) → 200 `[SOURCE:INPUT]` | Seed `QueueEntry(Id=5, QueueDate=today, Status=Waiting)` | `HandleRemove(entryId:5, db, ct)` | HTTP 200; entry status set to Removed | `StatusCode==200`; `db.QueueEntries.Single().Status==QueueStatus.Removed` |
| TC-006 | negative | DELETE non-existent entry → 404 `[SOURCE:INPUT]` | Empty DB | `HandleRemove(entryId:99, db, ct)` | HTTP 404 | `StatusCode==404` |
| EC-001 | edge_case | GET empty queue → 200 with empty array `[SOURCE:INPUT]` Basis: US_034 edge case: "Empty queue — GET returns 200 with empty array, not 404". | No QueueEntries seeded | `HandleGetQueue(db, ct)` | HTTP 200; empty list | `StatusCode==200`; `entries.Count==0` |
| EC-002 | edge_case | Yesterday's entries not included in GET `[SOURCE:INFERRED]` Basis: `HandleGetQueue` filters by `QueueDate == today`; prior-day entries must be excluded to ensure daily scope. | Seed `QueueEntry(QueueDate=yesterday, Status=Waiting)` | `HandleGetQueue(db, ct)` | HTTP 200; empty list (prior-day entry excluded) | `entries.Count==0` |
| ES-001 | error | Reorder with extra IDs not in current queue → 400 `[SOURCE:INPUT]` Basis: US_034 edge case: "Reorder with a missing patient ID — validation rejects". `HandleReorder` uses `SetEquals` — extra IDs also break equality. | Seed one entry (id=10); request `OrderedIds=[10, 999]` (extra stale id) | `HandleReorder(request, db, ct)` | HTTP 400 | `StatusCode==400` |

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/StaffQueueEndpointTests.cs` | TC-001 through ES-001 test methods |
| CREATE | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/StaffQueueEndpointTests.cs` | `ThrowingConcurrencyDbContext` inner class for TC-004 |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ApplicationDbContext` | In-Memory EF Core | `UseInMemoryDatabase(Guid.NewGuid().ToString())`; `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` | Per-test isolated store |
| `ThrowingConcurrencyDbContext` | `ApplicationDbContext` subclass | Overrides `SaveChangesAsync` to throw `new DbUpdateConcurrencyException("concurrent edit", [])` | Used only in TC-004 |

### Concurrency Test Subclass Pattern

```csharp
// Used only in TC-004 to simulate DbUpdateConcurrencyException in InMemory context
private sealed class ThrowingConcurrencyDbContext : ApplicationDbContext
{
    public ThrowingConcurrencyDbContext(DbContextOptions<ApplicationDbContext> opts)
        : base(opts) { }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
        => throw new DbUpdateConcurrencyException("Simulated concurrent edit.", []);
}
```

### Helper Patterns

```csharp
private static ApplicationDbContext BuildDb() =>
    new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

private static QueueEntry SeedEntry(ApplicationDbContext db, int id, int position,
    DateOnly? date = null, QueueStatus status = QueueStatus.Waiting)
{
    var entry = new QueueEntry
    {
        PatientId      = id * 10,
        QueueDate      = date ?? DateOnly.FromDateTime(DateTime.UtcNow),
        Position       = position,
        Status         = status,
        AddedByStaffId = 1,
    };
    db.QueueEntries.Add(entry);
    db.SaveChanges();
    return entry;
}
```

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Ordered GET | Three entries (pos=3,1,2) | Returned order: pos 1, 2, 3 |
| Valid reorder | `OrderedIds=[11,10]`; entries pos=1,2 | Entry 11→pos 1; entry 10→pos 2 |
| Missing ID | `OrderedIds=[10]`; two entries exist | 400; positions unchanged |
| Concurrent reorder | `ThrowingConcurrencyDbContext` | 409 |
| Soft-delete | Entry with Status=Waiting | Status=Removed after DELETE |
| Yesterday's entry | `QueueDate=yesterday` | Excluded from GET |

## Test Commands

- **Run Tests**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~StaffQueueEndpointTests"`
- **Run with Coverage**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~StaffQueueEndpointTests"`
- **Run Single Test**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~StaffQueueEndpointTests.TC_001"`

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**: `SetEquals` validation (400 path); `DbUpdateConcurrencyException` catch (409 path); `QueueDate == today` filter; `Status=Waiting` filter

## Documentation References

- **Framework Docs**: [xUnit.net](https://xunit.net/docs/getting-started/netcore/cmdline)
- **Mocking Guide**: [Moq Quickstart](https://github.com/moq/moq4/wiki/Quickstart)
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/IntakeVersioningEndpointTests.cs`
- **EF Core Concurrency**: [Optimistic Concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)

## Implementation Checklist

- [x] Create test file `tests/.../Features/StaffQueueEndpointTests.cs`
- [x] Set up `BuildDb()`, `SeedEntry()` helpers and `ThrowingConcurrencyDbContext` inner class
- [x] Implement positive test cases (TC-001, TC-002, TC-005)
- [x] Implement negative test cases (TC-003, TC-004, TC-006)
- [x] Implement edge case tests (EC-001, EC-002)
- [x] Implement error scenario tests (ES-001)
- [x] Run test suite and validate coverage meets target
