# Unit Test Plan - TASK_035

## Requirement Reference
- **User Story**: us_035
- **Story Location**: `.propel/context/tasks/EP-005/us_035/us_035.md`
- **Layer**: BE
- **Related Test Plans**: `EP-005/us_033/unittest/test_plan_be_staff-walkin-registration-queue.md` (QueueEntry entity)
- **Acceptance Criteria Covered**:
  - AC-001: `PATCH /appointments/{id}/checkin` sets `status=Arrived`; removes walk-in QueueEntry
  - AC-002: AuditLog written on successful check-in
  - AC-003: Already `Arrived` (or any non-Scheduled status) → 409
  - AC-004: Appointment not found → 404

## Test Plan Overview

Tests `CheckInPatientEndpoint.HandleCheckIn`. Staff ID is sourced from JWT sub claim (OWASP A01). The endpoint guards the FSM transition with an explicit `Status != Scheduled` check before setting `Status=Arrived`, so the `AppointmentFsmInterceptor` does not need to be registered in the InMemory DbContext builder. `QueueEntry` mutation (Waiting→CheckedIn) is conditional — online-booked patients without a walk-in entry must still check in successfully (EC-001). Concurrent check-in is tested via the `ThrowingConcurrencyDbContext` pattern (same as US_034). **Gap noted:** US_035 AC-003 describes an `override:true` re-confirmation path; the current source does not implement it — any non-Scheduled status returns 409 unconditionally.

## Dependent Tasks

- TASK_001 (us_007) — `Appointment` entity + `AppointmentFsmInterceptor`
- TASK_001 (us_033) — `QueueEntry` entity

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `CheckInPatientEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Staff/CheckInPatientEndpoint.cs` | FSM-guarded Scheduled→Arrived transition; QueueEntry CheckedIn; AuditLog; concurrency catch |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | Scheduled→Arrived, walk-in QueueEntry→CheckedIn, AuditLog written `[SOURCE:INPUT]` | Seed `Appointment(PatientId=5, Status=Scheduled)`; seed `QueueEntry(PatientId=5, QueueDate=today, Status=Waiting)`; staff JWT (sub="10") | `HandleCheckIn(id:appointment.Id, httpContext, db, ct)` | HTTP 200; `appointment.Status==Arrived`; `queueEntry.Status==CheckedIn`; `AuditLog.Action=="CheckIn"`, `AuditLog.ActorId==10` | `StatusCode==200`; `db.Appointments.Single().Status==Arrived`; `db.QueueEntries.Single().Status==CheckedIn`; `db.AuditLogs.Single().Action=="CheckIn"` |
| TC-002 | positive | Online-booked patient (no QueueEntry) → check-in still succeeds `[SOURCE:INPUT]` | Seed `Appointment(PatientId=7, Status=Scheduled)`; no QueueEntry seeded | `HandleCheckIn(id:appointment.Id, httpContext, db, ct)` | HTTP 200; appointment status set to Arrived; no exception | `StatusCode==200`; `db.Appointments.Single().Status==Arrived`; `db.QueueEntries.Count()==0` |
| TC-003 | negative | Already Arrived → 409 `[SOURCE:INPUT]` | Seed `Appointment(Status=Arrived)` | `HandleCheckIn(id:appointment.Id, httpContext, db, ct)` | HTTP 409; no status change; no AuditLog | `StatusCode==409`; `db.Appointments.Single().Status==Arrived`; `db.AuditLogs.Count()==0` |
| TC-004 | negative | Appointment not found → 404 `[SOURCE:INPUT]` | Empty Appointments table | `HandleCheckIn(id:9999, httpContext, db, ct)` | HTTP 404 | `StatusCode==404` |
| TC-005 | negative | No JWT sub → 401 `[SOURCE:INFERRED]` Basis: `HandleCheckIn` requires `int.TryParse(sub)` before any DB access; standard OWASP A01 guard. | `DefaultHttpContext` with no claims | `HandleCheckIn(id:1, DefaultHttpContext, db, ct)` | HTTP 401 | `StatusCode==401`; `db.AuditLogs.Count()==0` |
| EC-001 | edge_case | Cancelled appointment → 409 `[SOURCE:INPUT]` Basis: US_035 edge case: "Patient's appointment is Cancelled — check-in returns HTTP 409". `Status!=Scheduled` guard covers all non-Scheduled states. | Seed `Appointment(Status=Cancelled)` | `HandleCheckIn(id:appointment.Id, httpContext, db, ct)` | HTTP 409 | `StatusCode==409` |
| EC-002 | edge_case | NoShow appointment → 409 `[SOURCE:INFERRED]` Basis: `Status!=Scheduled` guard applies to all non-Scheduled states including NoShow; not explicitly stated in US but required by FSM logic. | Seed `Appointment(Status=NoShow)` | `HandleCheckIn(id:appointment.Id, httpContext, db, ct)` | HTTP 409 | `StatusCode==409` |
| ES-001 | error | Concurrent check-in → 409 `[SOURCE:INPUT]` Basis: US_035 edge case: "EF Core rowversion lock prevents double status update". Simulated via `ThrowingConcurrencyDbContext`. | Seed `Appointment(Status=Scheduled)`; use `ThrowingConcurrencyDbContext` | `HandleCheckIn(id:appointment.Id, httpContext, throwingDb, ct)` | HTTP 409 | `StatusCode==409` |

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/CheckInPatientEndpointTests.cs` | TC-001 through ES-001 test methods |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ApplicationDbContext` | In-Memory EF Core | `UseInMemoryDatabase(Guid.NewGuid().ToString())`; `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` | Per-test isolated store |
| `AppointmentFsmInterceptor` | Not registered | Endpoint's own `Status != Scheduled` guard is tested directly; interceptor is not needed for unit tests | N/A |
| `ThrowingConcurrencyDbContext` | `ApplicationDbContext` subclass | Overrides `SaveChangesAsync` to throw `DbUpdateConcurrencyException` | Used only in ES-001 |
| `HttpContext` (staff) | `DefaultHttpContext` | JWT sub claim via `ClaimsPrincipal` with `JwtRegisteredClaimNames.Sub` = staffId | StaffId integer |
| `HttpContext` (no claims) | `DefaultHttpContext` | Empty `ClaimsPrincipal` | 401 path |

### Helper Patterns

```csharp
private static ApplicationDbContext BuildDb() =>
    new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

private static HttpContext BuildStaffContext(int staffId) {
    var ctx = new DefaultHttpContext();
    ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, staffId.ToString()),
    }));
    return ctx;
}

private static async Task<Appointment> SeedAppointment(
    ApplicationDbContext db,
    int patientId,
    AppointmentStatus status)
{
    var appt = new Appointment { PatientId = patientId, SlotId = 1, Status = status };
    db.Appointments.Add(appt);
    await db.SaveChangesAsync();
    return appt;
}
```

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Successful check-in | `Appointment(Status=Scheduled)` + `QueueEntry(Waiting)` | Status=Arrived; QueueEntry=CheckedIn; AuditLog present |
| No queue entry | `Appointment(Status=Scheduled)` only | Status=Arrived; success |
| Already arrived | `Appointment(Status=Arrived)` | 409; status unchanged |
| Cancelled | `Appointment(Status=Cancelled)` | 409 |
| NoShow | `Appointment(Status=NoShow)` | 409 |
| Not found | No appointment seeded | 404 |

## Test Commands

- **Run Tests**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~CheckInPatientEndpointTests"`
- **Run with Coverage**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~CheckInPatientEndpointTests"`
- **Run Single Test**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~CheckInPatientEndpointTests.TC_001"`

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**: `Status != Scheduled` guard (all non-Scheduled states); QueueEntry null-check (with/without walk-in entry); `DbUpdateConcurrencyException` catch; JWT sub guard

## Documentation References

- **Framework Docs**: [xUnit.net](https://xunit.net/docs/getting-started/netcore/cmdline)
- **Mocking Guide**: [Moq Quickstart](https://github.com/moq/moq4/wiki/Quickstart)
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/IntakeVersioningEndpointTests.cs`
- **EF Core Concurrency**: [Optimistic Concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency)

## Implementation Checklist

- [x] Create test file `tests/.../Features/CheckInPatientEndpointTests.cs`
- [x] Set up `BuildDb()`, `BuildStaffContext()`, `SeedAppointment()` helpers and `ThrowingConcurrencyDbContext`
- [x] Implement positive test cases (TC-001, TC-002)
- [x] Implement negative test cases (TC-003, TC-004, TC-005)
- [x] Implement edge case tests (EC-001, EC-002)
- [x] Implement error scenario tests (ES-001)
- [x] Run test suite and validate coverage meets target
