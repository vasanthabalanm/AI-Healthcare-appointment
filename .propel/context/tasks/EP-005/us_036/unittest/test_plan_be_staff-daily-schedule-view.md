# Unit Test Plan - TASK_036

## Requirement Reference
- **User Story**: us_036
- **Story Location**: `.propel/context/tasks/EP-005/us_036/us_036.md`
- **Layer**: BE
- **Related Test Plans**: `EP-004/us_031/unittest/test_plan_be_intake-editing-version-history.md` (IntakeRecord entity)
- **Acceptance Criteria Covered**:
  - AC-001: `GET /schedule/today` returns appointments ordered by `SlotTime ASC`
  - AC-002: Each appointment entry includes `intakeStatus` (`Submitted` / `Pending` / `NA`)
  - AC-003: `riskFlag=true` for `IsHighRisk=true`; `riskFlag=false` otherwise
  - AC-004: Optional `?date=` parameter; empty result → 200 not 404

## Test Plan Overview

Tests `GetDailyScheduleEndpoint.HandleGetDailySchedule`. The endpoint performs a `GroupJoin` between `Appointments` and `IntakeRecords` (scoped to the global query filter `IsLatest=true`). For InMemory tests, both `Slot` and `Appointment` entities must be seeded with matching FKs so EF Core resolves the `Slot` navigation property via change-tracker fixup. `intakeStatus` logic: if `IntakeRecord` exists → `Submitted`; if no intake AND patient `WalkIn=true` → `NA`; otherwise → `Pending`. Pagination returns 50 per page. **Gap noted:** US_036 edge case states schedule supports ≥ 100 appointments paginated at max 50/page; source uses `PageSize=50` constant; EC-001 verifies this boundary.

## Dependent Tasks

- TASK_001 (us_007) — `Appointment` + `Slot` entities
- TASK_001 (us_008) — `IntakeRecord` entity with `IsLatest` global query filter
- TASK_001 (us_022) — `Appointment.IsHighRisk` field

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `GetDailyScheduleEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Staff/GetDailyScheduleEndpoint.cs` | Date-scoped appointment schedule with intake status join and risk flag; pagination |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | Schedule returns appointments ordered by SlotTime ASC `[SOURCE:INPUT]` | Seed `Slot(SlotTime=today 10:00)` + `Slot(SlotTime=today 08:00)`; two corresponding `Appointment` rows; no IntakeRecords | `HandleGetDailySchedule(db, date:null, page:1, ct)` | HTTP 200; result ordered by SlotTime 08:00 then 10:00 | `StatusCode==200`; `data[0].SlotTime.Hour==8`; `data[1].SlotTime.Hour==10`; `totalCount==2` |
| TC-002 | positive | IntakeStatus=Submitted for patient with IsLatest IntakeRecord `[SOURCE:INPUT]` | Seed appointment (PatientId=3); seed `IntakeRecord(PatientId=3, IsLatest=true)` | `HandleGetDailySchedule(db, date:null, page:1, ct)` | HTTP 200; appointment entry has `IntakeStatus=="Submitted"` | `data[0].IntakeStatus=="Submitted"` |
| TC-003 | positive | IntakeStatus=Pending for patient without IntakeRecord `[SOURCE:INPUT]` | Seed appointment (PatientId=4, `Patient.WalkIn=false`); no IntakeRecord | `HandleGetDailySchedule(db, date:null, page:1, ct)` | HTTP 200; `IntakeStatus=="Pending"` | `data[0].IntakeStatus=="Pending"` |
| TC-004 | positive | IntakeStatus=NA for WalkIn patient `[SOURCE:INPUT]` | Seed appointment (PatientId=5); seed `UserAccount(Id=5, WalkIn=true)` as `Patient`; no IntakeRecord | `HandleGetDailySchedule(db, date:null, page:1, ct)` | HTTP 200; `IntakeStatus=="NA"` | `data[0].IntakeStatus=="NA"` |
| TC-005 | positive | riskFlag=true for IsHighRisk=true; riskFlag=false for IsHighRisk=false `[SOURCE:INPUT]` | Seed two appointments: one with `IsHighRisk=true`, one with `IsHighRisk=false` | `HandleGetDailySchedule(db, date:null, page:1, ct)` | HTTP 200; high-risk entry has `RiskFlag=true`; low-risk entry has `RiskFlag=false` | `data.Single(d=>d.RiskFlag).AppointmentId == highRiskAppt.Id`; `data.Single(d=>!d.RiskFlag).AppointmentId == lowRiskAppt.Id` |
| TC-006 | positive | Empty schedule → 200 with empty data and totalCount=0 `[SOURCE:INPUT]` | No appointments for today seeded | `HandleGetDailySchedule(db, date:null, page:1, ct)` | HTTP 200; `data` is empty; `totalCount==0` | `StatusCode==200`; `response.TotalCount==0`; `response.Data.Count==0` |
| TC-007 | positive | ?date= parameter queries non-today date `[SOURCE:INPUT]` | Seed appointment on `date=2026-06-01`; no appointments today | `HandleGetDailySchedule(db, date:new DateOnly(2026,6,1), page:1, ct)` | HTTP 200; appointment from that date returned | `response.TotalCount==1`; `response.Data[0].SlotTime.Date==new DateTime(2026,6,1)` |
| EC-001 | edge_case | 51 appointments → page 1 has 50; page 2 has 1 `[SOURCE:INPUT]` Basis: US_036 edge case: "≥ 100 appointments paginated, max 50 per page". Source uses `PageSize=50` constant. | Seed 51 appointments for today in different time slots | `HandleGetDailySchedule(db, date:null, page:1, ct)` then page 2 | Page 1: 50 entries; page 2: 1 entry; `pageCount==2` | `response.Data.Count==50`; `response.PageCount==2`; page-2 response `Data.Count==1` |
| ES-001 | error | Cancelled appointments excluded from schedule `[SOURCE:INFERRED]` Basis: `HandleGetDailySchedule` filters `Status != Cancelled` before all other logic; cancelled slots must not appear regardless of date or intake status. | Seed `Appointment(Status=Cancelled)` for today + `Appointment(Status=Scheduled)` for today | `HandleGetDailySchedule(db, date:null, page:1, ct)` | HTTP 200; only Scheduled appointment in result; `totalCount==1` | `response.TotalCount==1`; `response.Data[0].Status=="Scheduled"` |

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/GetDailyScheduleEndpointTests.cs` | TC-001 through ES-001 test methods |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ApplicationDbContext` | In-Memory EF Core | `UseInMemoryDatabase(Guid.NewGuid().ToString())`; `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` | Per-test isolated store |
| `Slot` navigation property | Seeded entity | Seed `Slot` record and matching `Appointment.SlotId` so EF Core resolves navigation via change-tracker fixup | Both entities seeded per test |
| `IntakeRecord` global filter | Seeded entity | Seed `IntakeRecord` with `IsLatest=true`; the global filter is honoured automatically | `IntakeRecord(PatientId, IsLatest=true)` |
| `UserAccount` navigation | Seeded entity | Seed `UserAccount` with matching `Id` = `Appointment.PatientId`; set `WalkIn` as needed | `WalkIn=true` or `false` per test |

### Helper Patterns

```csharp
private static ApplicationDbContext BuildDb() =>
    new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

// Seed a Slot + Appointment pair and return the appointment.
private static async Task<Appointment> SeedAppointmentWithSlot(
    ApplicationDbContext db,
    int       patientId,
    DateTime  slotTime,
    AppointmentStatus status    = AppointmentStatus.Scheduled,
    bool      isHighRisk        = false)
{
    var slot = new Slot { SlotTime = slotTime, IsAvailable = false };
    db.Slots.Add(slot);
    await db.SaveChangesAsync();

    var appt = new Appointment
    {
        PatientId      = patientId,
        SlotId         = slot.Id,
        Status         = status,
        IsHighRisk     = isHighRisk,
        NoShowRiskScore = isHighRisk ? 80 : 30,
    };
    db.Appointments.Add(appt);
    await db.SaveChangesAsync();
    return appt;
}
```

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Ordered by time | Two slots: 10:00 and 08:00 | Result order: 08:00, 10:00 |
| IntakeStatus=Submitted | `IntakeRecord(PatientId=X, IsLatest=true)` | `IntakeStatus=="Submitted"` |
| IntakeStatus=Pending | No intake; `WalkIn=false` | `IntakeStatus=="Pending"` |
| IntakeStatus=NA | No intake; `WalkIn=true` | `IntakeStatus=="NA"` |
| riskFlag | `IsHighRisk=true` | `RiskFlag=true` |
| Empty | No appointments | `TotalCount==0`, `Data==[]` |
| Pagination | 51 appointments | Page 1: 50, Page 2: 1 |
| Cancelled excluded | `Status=Cancelled` appointment | Not in results |

## Test Commands

- **Run Tests**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~GetDailyScheduleEndpointTests"`
- **Run with Coverage**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~GetDailyScheduleEndpointTests"`
- **Run Single Test**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~GetDailyScheduleEndpointTests.TC_001"`

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**: `intakeStatus` ternary (all three branches: Submitted/NA/Pending); `riskFlag` boolean; `Status != Cancelled` filter; pagination boundary (page 1 of 2; page 2 of 2)

## Documentation References

- **Framework Docs**: [xUnit.net](https://xunit.net/docs/getting-started/netcore/cmdline)
- **Mocking Guide**: [Moq Quickstart](https://github.com/moq/moq4/wiki/Quickstart)
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/AuditLogEndpointTests.cs`
- **EF Core GroupJoin**: [Complex Query Operators](https://learn.microsoft.com/en-us/ef/core/querying/complex-query-operators)

## Implementation Checklist

- [x] Create test file `tests/.../Features/GetDailyScheduleEndpointTests.cs`
- [x] Set up `BuildDb()` and `SeedAppointmentWithSlot()` helpers
- [x] Implement positive test cases (TC-001 to TC-007)
- [x] Implement edge case tests (EC-001)
- [x] Implement error scenario tests (ES-001)
- [x] Run test suite and validate coverage meets target
