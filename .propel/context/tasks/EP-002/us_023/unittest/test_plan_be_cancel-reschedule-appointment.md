# Unit Test Plan - US_023

## Requirement Reference
- **User Story**: us_023
- **Story Location**: `.propel/context/tasks/EP-002/us_023/us_023.md`
- **Layer**: BE
- **Related Test Plans**: None (single-layer story)
- **Acceptance Criteria Covered**:
  - AC-001: Patient can cancel a Scheduled appointment; slot ownership held until `SwapMonitorJob` runs; cache invalidated
  - AC-002: Cancellation within the cutoff window is rejected with 400 (cutoff from `AppSettings.CancellationCutoffHours`)
  - AC-003: Patient can reschedule to a different available future slot atomically; old slot released via `SwapMonitorJob`; reminder jobs updated
  - AC-004: Only the appointment owner can cancel/reschedule (403 for wrong patient)
  - AC-005: Stale reminder jobs (reminder, SMS, email) deleted on cancel and reschedule; new reminder scheduled after reschedule

## Test Plan Overview

Covers backend unit tests for two tightly coupled handlers in US_023:

1. **`CancelAppointmentEndpoint.HandleCancelAppointment`** (`DELETE /appointments/{id}`) — cancels a Scheduled appointment, deletes stale reminder jobs, enqueues `SwapMonitorJob`, invalidates Redis.
2. **`RescheduleAppointmentEndpoint.HandleRescheduleAppointment`** (`PATCH /appointments/{id}/reschedule`) — atomically moves the booking to a new available slot, cancels stale reminders, schedules new reminder, enqueues `SwapMonitorJob` for old slot, invalidates cache for both date keys.

Both handlers are tested as static methods against EF Core in-memory DB. `ICacheService` and `IBackgroundJobClient` are mocked with Moq.

AI Impact: **No** (no AIR-XXX requirements referenced in US_023).

## Dependent Tasks
- EP-TECH US_001–006 (infrastructure foundation) and US_019 (booking — creates Scheduled appointments) must be passing.

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `CancelAppointmentEndpoint.HandleCancelAppointment` | static method | `src/ClinicalHealthcare.Api/Features/Appointments/CancelAppointmentEndpoint.cs` | Guard checks (auth, status, cutoff); delete reminder jobs; set `Status=Cancelled`; enqueue SwapMonitorJob; invalidate cache |
| `RescheduleAppointmentEndpoint.HandleRescheduleAppointment` | static method | `src/ClinicalHealthcare.Api/Features/Appointments/RescheduleAppointmentEndpoint.cs` | Guard checks (auth, same slot, cutoff, new slot availability); EF transaction; delete old reminders; schedule new reminder; enqueue SwapMonitorJob for old slot; invalidate both cache keys |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | Cancel Scheduled appointment → 200; `Status=Cancelled`; slot stays unavailable | Appointment `Status=Scheduled`; slot `IsAvailable=false`; caller is owner | `HandleCancelAppointment(apptId)` called | 200 OK; `appointment.Status == Cancelled`; slot `IsAvailable` remains `false` | `StatusCode == 200`; DB asserted [SOURCE:INPUT] Basis: AC-001 |
| TC-002 | positive | Cancel enqueues `SwapMonitorJob` for released slot | Scheduled appointment; slot seeded | `HandleCancelAppointment(apptId)` completes | `jobs.Create` called with `SwapMonitorJob` type | `jobs.Create(job.Type == typeof(SwapMonitorJob))` `Times.Once` [SOURCE:INPUT] Basis: AC-001 side-effect |
| TC-003 | positive | Cancel deletes stale `ReminderJobId` via `ChangeState(DeletedState)` | Appointment has `ReminderJobId = "old-reminder-job"` | `HandleCancelAppointment` completes | `jobs.ChangeState("old-reminder-job", DeletedState, null)` called | `ChangeState` verify with jobId [SOURCE:INPUT] Basis: AC-005 |
| TC-004 | positive | Cancel deletes SMS reminder jobs when present | Appointment has `SmsReminderJobId48h` and `SmsReminderJobId2h` set | `HandleCancelAppointment` completes | Both `ChangeState` calls with `DeletedState` issued | Verify `ChangeState` `Times.AtLeast(2)` for SMS job IDs [SOURCE:INPUT] Basis: AC-005 |
| TC-005 | positive | Cancel invalidates slot-date cache key | Successful cancel | `HandleCancelAppointment` completes | `cache.DeleteAsync` called with `slots:date:YYYY-MM-DD` | `DeleteAsync` verify `Times.Once` with correct key [SOURCE:INPUT] Basis: AC-001 cache coherence |
| TC-006 | negative | Cancel within cutoff window returns 400 | Slot `SlotTime = UtcNow + 12h`; `CancellationCutoffHours = 24` | `HandleCancelAppointment(apptId)` called | 400 Bad Request | `StatusCode == 400` [SOURCE:INPUT] Basis: AC-002 |
| TC-007 | negative | Wrong patient cancel returns 403 | Appointment `PatientId = 1`; caller JWT `sub = "2"` | `HandleCancelAppointment(apptId)` called | 403 Forbidden | `StatusCode == 403` [SOURCE:INPUT] Basis: AC-004 |
| TC-008 | positive | Reschedule to valid new slot → 200; `appointment.SlotId` updated | Appointment `Status=Scheduled`; current slot `SlotTime = UtcNow + 72h`; new slot available | `HandleRescheduleAppointment(apptId, newSlotId)` called | 200 OK; `appointment.SlotId == newSlot.Id` | `StatusCode == 200`; `db.Appointments.Find(apptId).SlotId == newSlot.Id` [SOURCE:INPUT] Basis: AC-003 |
| TC-009 | positive | Reschedule — old slot stays unavailable; `SwapMonitorJob` enqueued for old slot | After reschedule | Handler completes | Old slot `IsAvailable == false`; `jobs.Create(SwapMonitorJob)` `Times.Once` | DB slot asserted; jobs mock verified [SOURCE:INPUT] Basis: AC-003 side-effect |
| TC-010 | positive | Reschedule deletes stale `ReminderJobId` and schedules new reminder | Appointment has `ReminderJobId = "stale-job"` | `HandleRescheduleAppointment` completes | `jobs.ChangeState("stale-job", DeletedState, null)` called; new reminder `Create` called | Verify stale delete + new schedule [SOURCE:INPUT] Basis: AC-005 |
| TC-011 | negative | Reschedule to unavailable new slot returns 409 | New slot `IsAvailable = false` | `HandleRescheduleAppointment` called | 409 Conflict | `StatusCode == 409` [SOURCE:INPUT] Basis: AC-003 slot conflict guard |
| EC-001 | edge_case | Cancel already-cancelled appointment returns 400 | Appointment `Status = Cancelled` | `HandleCancelAppointment` called | 400 Bad Request | `StatusCode == 400` [SOURCE:INFERRED] Basis: `Status != Scheduled` guard in handler |
| EC-002 | edge_case | Reschedule to same slot returns 400 | `NewSlotId == appointment.SlotId` | `HandleRescheduleAppointment` called | 400 Bad Request | `StatusCode == 400` [SOURCE:INFERRED] Basis: same-slot no-op guard in handler |
| EC-003 | edge_case | Reschedule within cutoff window returns 400 | Current slot `SlotTime = UtcNow + 12h`; `CancellationCutoffHours = 24` | `HandleRescheduleAppointment` called | 400 Bad Request | `StatusCode == 400` [SOURCE:INFERRED] Basis: cutoff enforced on current slot before allowing reschedule |
| ES-001 | error | Wrong patient reschedule returns 403 | Appointment `PatientId = 1`; caller JWT `sub = "2"` | `HandleRescheduleAppointment` called | 403 Forbidden | `StatusCode == 403` [SOURCE:INPUT] Basis: AC-004 |

## AI Component Test Cases

> **AI Impact: No** — US_023 contains no AIR-XXX requirements. This section is skipped.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| EXISTS | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/CancelAppointmentEndpointTests.cs` | Tests for `DELETE /appointments/{id}` (TC-001–007, EC-001 covered) |
| EXISTS | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/RescheduleAppointmentEndpointTests.cs` | Tests for `PATCH /appointments/{id}/reschedule` (TC-008–011, EC-002–003, ES-001 covered) |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ApplicationDbContext` | EF Core In-Memory | `UseInMemoryDatabase(Guid.NewGuid().ToString())` with `TransactionIgnoredWarning` suppressed | Full in-memory DB per test |
| `ICacheService` | `Mock<ICacheService>` | `DeleteAsync(key, ct)` — verify `Times.Once` with `slots:date:YYYY-MM-DD` | `Task.CompletedTask` |
| `IBackgroundJobClient` | `Mock<IBackgroundJobClient>` | `Create(job => job.Type == typeof(SwapMonitorJob))` — verify `Times.Once`; `ChangeState(jobId, DeletedState, null)` — verify stale job deletion | Returns job ID string; `Returns(true)` for `ChangeState` |
| `IOptions<AppSettings>` | `Options.Create(new AppSettings { CancellationCutoffHours = 24 })` | Used for cutoff calculation in both cancel and reschedule | N/A |
| `HttpContext` | `DefaultHttpContext` | Injects `ClaimsPrincipal` with `sub = patientId.ToString()` | N/A |

## AI Mocking Strategy

> **AI Impact: No** — Skipped.

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Valid cancel | `Status=Scheduled`; `SlotTime = UtcNow + 72h`; `PatientId = caller.Id` | 200; `Status=Cancelled`; slot stays unavailable |
| Within cutoff cancel | `Status=Scheduled`; `SlotTime = UtcNow + 12h`; `CancellationCutoffHours = 24` | 400 Bad Request |
| Wrong patient cancel | `Appointment.PatientId = 1`; JWT `sub = "2"` | 403 Forbidden |
| Already cancelled | `Status=Cancelled` | 400 Bad Request |
| With reminder job | `Appointment.ReminderJobId = "old-reminder-job"` | `ChangeState("old-reminder-job", DeletedState, null)` called |
| Valid reschedule | Current slot `+72h`; new slot `+96h`, `IsAvailable=true` | 200; `SlotId` updated |
| Unavailable new slot | New slot `IsAvailable=false` | 409 Conflict |
| Same slot reschedule | `NewSlotId == appointment.SlotId` | 400 Bad Request |
| Wrong patient reschedule | `Appointment.PatientId = 1`; JWT `sub = "2"` | 403 Forbidden |

## Test Commands

- **Run Tests**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --filter "FullyQualifiedName~CancelAppointmentEndpointTests|FullyQualifiedName~RescheduleAppointmentEndpointTests"`
- **Run with Coverage**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --collect:"XPlat Code Coverage" --results-directory ./coverage`
- **Run Single Test**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --filter "FullyQualifiedName~CancelAppointment_Scheduled_Returns200"`

## Coverage Target

- **Line Coverage**: 92%
- **Branch Coverage**: 88%
- **Critical Paths**:
  - `HandleCancelAppointment` — cutoff guard (`<=` boundary), status guard, wrong-patient guard; reminder job deletion when IDs set/null; `SwapMonitorJob` enqueue
  - `HandleRescheduleAppointment` — same-slot guard; cutoff guard; new-slot unavailable guard; stale reminder deletion; `SwapMonitorJob` enqueue for old slot; both cache keys invalidated

## Documentation References

- **Framework Docs**: [xUnit 2.x — https://xunit.net](https://xunit.net)
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/CancelAppointmentEndpointTests.cs`
- **Mocking Guide**: [Moq 4.x — https://github.com/devlooped/moq](https://github.com/devlooped/moq)

## Implementation Checklist

- [x] Create test file structure per Expected Changes
- [x] Set up test data fixtures per Test Data section
- [x] Configure mocking dependencies per Mocking Strategy
- [x] Implement positive test cases (TC-001 – TC-010)
- [x] Implement negative test cases (TC-006, TC-007, TC-011)
- [x] Implement edge case tests (EC-001, EC-002, EC-003)
- [x] Implement error scenario tests (ES-001)
- [x] Run test suite and validate coverage meets target (75 tests passed, 0 failed — 2026-05-24)
