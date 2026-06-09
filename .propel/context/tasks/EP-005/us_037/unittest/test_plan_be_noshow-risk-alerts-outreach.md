# Unit Test Plan - TASK_037

## Requirement Reference
- **User Story**: us_037
- **Story Location**: `.propel/context/tasks/EP-005/us_037/us_037.md`
- **Layer**: BE
- **Related Test Plans**: `EP-005/us_036/unittest/test_plan_be_staff-daily-schedule-view.md` (Slot + Appointment seed helpers)
- **Acceptance Criteria Covered**:
  - AC-001: `GET /schedule/high-risk` returns appointments with `IsHighRisk=true`; excludes cancelled
  - AC-002: `POST /appointments/{id}/outreach` creates `OutreachRecord` → 201
  - AC-003: `PATCH /appointments/{id}/status` with `NoShow` → status updated, slot released, AuditLog written, `SwapMonitorJob` enqueued, slot cache invalidated
  - AC-004: Empty high-risk list → 200 with empty array

## Test Plan Overview

Tests `GetHighRiskAppointmentsEndpoint.HandleGetHighRisk`, `RecordOutreachEndpoint.HandleRecordOutreach`, and `UpdateAppointmentStatusEndpoint.HandleUpdateStatus`. `UpdateAppointmentStatusEndpoint` depends on `ICacheService` (slot cache key invalidation) and `IBackgroundJobClient` (Hangfire job enqueue); both are Moq mocks. `RecordOutreachEndpoint` and `UpdateAppointmentStatusEndpoint` read staff ID from JWT sub claim (OWASP A01). **Gap noted:** `GetHighRiskAppointmentsEndpoint` orders results by `Slot.SlotTime ASC`; US_037 AC-001 states results should be ordered by `noShowRiskScore DESC`. This ordering mismatch is documented as a `[SOURCE:INPUT]` gap — test TC-001 verifies source behaviour, noting the divergence.

## Dependent Tasks

- TASK_001 (us_007) — `Appointment` + `Slot` entities
- TASK_001 (us_022) — `Appointment.IsHighRisk`, `Appointment.NoShowRiskScore`
- TASK_001 (us_037) — `OutreachRecord` entity, `SwapMonitorJob`, `ICacheService`, `AppSettings.NoShowRiskThreshold`

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `GetHighRiskAppointmentsEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Staff/GetHighRiskAppointmentsEndpoint.cs` | Filter high-risk, exclude cancelled, order by SlotTime |
| `RecordOutreachEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Staff/RecordOutreachEndpoint.cs` | Create OutreachRecord with staff context |
| `UpdateAppointmentStatusEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Staff/UpdateAppointmentStatusEndpoint.cs` | NoShow FSM transition; slot release; cache invalidation; Hangfire job enqueue |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | GET returns only IsHighRisk=true; low-risk excluded; ordered by SlotTime `[SOURCE:INPUT]` **Note:** Source orders by `SlotTime ASC`; US AC-001 specifies `noShowRiskScore DESC`. Test verifies source behaviour. Gap documented. | Seed `Slot(10:00)` + `Appointment(IsHighRisk=true, NoShowRiskScore=85)`; seed `Slot(09:00)` + `Appointment(IsHighRisk=false)` | `HandleGetHighRisk(db, date:null, ct)` | HTTP 200; only high-risk appointment returned; SlotTime present | `StatusCode==200`; `result.Count==1`; `result[0].RiskScore==85`; low-risk appointment absent |
| TC-002 | positive | Empty high-risk list → 200 empty array `[SOURCE:INPUT]` | No appointments seeded | `HandleGetHighRisk(db, date:null, ct)` | HTTP 200; empty array | `StatusCode==200`; `result.Count==0` |
| TC-003 | positive | Creates OutreachRecord with staffId, appointmentId, notes → 201 `[SOURCE:INPUT]` | Seed appointment (Id=8); staff JWT (sub="12"); `OutreachRequest{Notes="Left voicemail"}` | `HandleRecordOutreach(id:8, request, httpContext, db, ct)` | HTTP 201; `OutreachRecord(AppointmentId=8, StaffId=12, Notes="Left voicemail")` in DB | `StatusCode==201`; `db.OutreachRecords.Single().AppointmentId==8`; `StaffId==12`; `Notes=="Left voicemail"` |
| TC-004 | positive | Outreach on non-high-risk appointment → 201 (endpoint allows regardless of risk flag) `[SOURCE:INPUT]` Basis: US_037 edge case: "Outreach recorded on non-high-risk — HTTP 201". | Seed appointment (Id=9, IsHighRisk=false) | `HandleRecordOutreach(id:9, request, httpContext, db, ct)` | HTTP 201; OutreachRecord created | `StatusCode==201`; `db.OutreachRecords.Count()==1` |
| TC-005 | positive | NoShow → Status=NoShow, Slot.IsAvailable=true, AuditLog(NoShow), SwapMonitorJob enqueued, cache invalidated → 200 `[SOURCE:INPUT]` | Seed `Slot(id=3, IsAvailable=false)`; `Appointment(Id=7, SlotId=3, Status=Scheduled)`; staff JWT (sub="15"); mocked `ICacheService` + `IBackgroundJobClient` | `HandleUpdateStatus(id:7, {Status:"NoShow"}, httpContext, db, cache, jobs, ct)` | HTTP 200; appointment Status=NoShow; slot IsAvailable=true; AuditLog written; `cache.DeleteAsync` called once; `jobs.Enqueue<SwapMonitorJob>` called once | `StatusCode==200`; `db.Appointments.Single().Status==NoShow`; `db.Slots.Single().IsAvailable==true`; `db.AuditLogs.Single().Action=="NoShow"`; `mockCache.Verify(c=>c.DeleteAsync(It.Is<string>(k=>k.Contains(...))), Times.Once)`; `mockJobs.Verify(j=>j.Enqueue(...), Times.Once)` |
| TC-006 | negative | NoShow on non-Scheduled appointment → 409 `[SOURCE:INPUT]` | Seed `Appointment(Status=Arrived)` | `HandleUpdateStatus(id:appointment.Id, {Status:"NoShow"}, httpContext, db, cache, jobs, ct)` | HTTP 409; no status change; no job enqueued | `StatusCode==409`; `db.Appointments.Single().Status==Arrived`; `mockJobs.Verify(j=>j.Enqueue(...), Times.Never)` |
| EC-001 | edge_case | Cancelled high-risk appointments excluded from GET `[SOURCE:INFERRED]` Basis: `HandleGetHighRisk` filters `!Cancelled`; high-risk cancelled appointments must not appear. | Seed `Appointment(IsHighRisk=true, Status=Cancelled)` | `HandleGetHighRisk(db, date:null, ct)` | HTTP 200; empty result | `result.Count==0` |
| EC-002 | edge_case | Non-"NoShow" status value → 400 `[SOURCE:INFERRED]` Basis: `HandleUpdateStatus` only accepts `"NoShow"` — any other value returns 400 before DB access. | Seed `Appointment(Status=Scheduled)` | `HandleUpdateStatus(id:appointment.Id, {Status:"Arrived"}, httpContext, db, cache, jobs, ct)` | HTTP 400; no changes | `StatusCode==400`; `db.Appointments.Single().Status==Scheduled`; `mockJobs.Verify(j=>j.Enqueue(...), Times.Never)` |
| ES-001 | error | Outreach on non-existent appointment → 404 `[SOURCE:INFERRED]` Basis: `HandleRecordOutreach` loads appointment by ID → returns 404 if null. | No appointment with target id | `HandleRecordOutreach(id:99, request, httpContext, db, ct)` | HTTP 404; no OutreachRecord | `StatusCode==404`; `db.OutreachRecords.Count()==0` |
| ES-002 | error | UpdateStatus with no JWT sub → 401 `[SOURCE:INFERRED]` Basis: `HandleUpdateStatus` reads JWT sub claim (OWASP A01 guard) before any DB access. | `DefaultHttpContext` with no claims | `HandleUpdateStatus(id:1, {Status:"NoShow"}, DefaultHttpContext, db, cache, jobs, ct)` | HTTP 401 | `StatusCode==401`; `mockJobs.Verify(j=>j.Enqueue(...), Times.Never)` |

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/NoShowOutreachEndpointTests.cs` | TC-001 through ES-002 test methods |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ApplicationDbContext` | In-Memory EF Core | `UseInMemoryDatabase(Guid.NewGuid().ToString())`; `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` | Per-test isolated store |
| `ICacheService` | `Mock<ICacheService>` | Default: `Returns(Task.CompletedTask)`; verify `DeleteAsync` called with correct key | `mockCache.Object` |
| `IBackgroundJobClient` | `Mock<IBackgroundJobClient>` | Default: no-op `Create` call; verify `Enqueue<SwapMonitorJob>` call count | `mockJobs.Object` |
| `HttpContext` (staff) | `DefaultHttpContext` | JWT sub claim via `ClaimsPrincipal` with `JwtRegisteredClaimNames.Sub` = staffId | StaffId integer |
| `HttpContext` (no claims) | `DefaultHttpContext` | Empty `ClaimsPrincipal` | 401 path |
| `Slot` navigation property | Seeded entity | Seed `Slot` + matching `Appointment.SlotId` for navigation fixup | Required by TC-005, TC-006 |

### Cache Key Format

`UpdateAppointmentStatusEndpoint` constructs the slot cache key using `GetSlotsEndpoint.CacheKeyPrefix`:
```csharp
$"{GetSlotsEndpoint.CacheKeyPrefix}{DateOnly.FromDateTime(appointment.Slot.SlotTime):yyyy-MM-dd}"
```
Verify with: `mockCache.Verify(c => c.DeleteAsync(It.Is<string>(k => k.StartsWith("slots:"))), Times.Once)`

### Helper Patterns

```csharp
private static ApplicationDbContext BuildDb() =>
    new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

private static HttpContext BuildStaffContext(int staffId)
{
    var ctx = new DefaultHttpContext();
    ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, staffId.ToString()),
    }));
    return ctx;
}

// Seed Slot + Appointment; returns (slot, appointment).
private static async Task<(Slot slot, Appointment appt)> SeedAppointmentWithSlot(
    ApplicationDbContext db,
    AppointmentStatus    status     = AppointmentStatus.Scheduled,
    bool                 isHighRisk = false,
    int                  riskScore  = 50)
{
    var slot = new Slot { SlotTime = DateTime.UtcNow.Date.AddHours(10), IsAvailable = false };
    db.Slots.Add(slot);
    await db.SaveChangesAsync();

    var appt = new Appointment
    {
        PatientId       = 1,
        SlotId          = slot.Id,
        Status          = status,
        IsHighRisk      = isHighRisk,
        NoShowRiskScore = riskScore,
    };
    db.Appointments.Add(appt);
    await db.SaveChangesAsync();
    return (slot, appt);
}
```

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| High-risk list | `IsHighRisk=true + false` | Only high-risk returned |
| Empty high-risk | No appointments | 200 empty array |
| Outreach created | `Notes="Left voicemail"`; staff 12 | `OutreachRecord.StaffId==12`, `Notes=="Left voicemail"` |
| NoShow transition | `Status=Scheduled`; `{Status:"NoShow"}` | Status=NoShow; slot.IsAvailable=true; AuditLog; jobs.Enqueue called |
| Non-Scheduled → NoShow | `Status=Arrived`; `{Status:"NoShow"}` | 409; no job enqueued |
| Non-NoShow status | `{Status:"Arrived"}` | 400; no DB change |
| Cancelled high-risk | `IsHighRisk=true, Status=Cancelled` | Not in GET results |

## Test Commands

- **Run Tests**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~NoShowOutreachEndpointTests"`
- **Run with Coverage**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~NoShowOutreachEndpointTests"`
- **Run Single Test**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~NoShowOutreachEndpointTests.TC_001"`

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**: `IsHighRisk` + `!Cancelled` filters; `Status != Scheduled` guard in `HandleUpdateStatus`; cache key construction; Hangfire enqueue; JWT sub guard (401 path); outreach 404 path

## Documentation References

- **Framework Docs**: [xUnit.net](https://xunit.net/docs/getting-started/netcore/cmdline)
- **Mocking Guide**: [Moq Quickstart](https://github.com/moq/moq4/wiki/Quickstart)
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/AiIntakeEndpointTests.cs`
- **Hangfire IBackgroundJobClient**: [Hangfire Client](https://docs.hangfire.io/en/latest/background-methods/calling-methods-in-background.html)

## Implementation Checklist

- [x] Create test file `tests/.../Features/NoShowOutreachEndpointTests.cs`
- [x] Set up `BuildDb()`, `BuildStaffContext()`, `SeedAppointmentWithSlot()` helpers
- [x] Configure `Mock<ICacheService>` and `Mock<IBackgroundJobClient>` per test
- [x] Implement positive test cases (TC-001 to TC-005)
- [x] Implement negative test cases (TC-006)
- [x] Implement edge case tests (EC-001, EC-002)
- [x] Implement error scenario tests (ES-001, ES-002)
- [x] Run test suite and validate coverage meets target
