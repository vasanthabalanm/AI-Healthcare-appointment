# Unit Test Plan - US_019

## Requirement Reference
- **User Story**: us_019
- **Story Location**: `.propel/context/tasks/EP-002/us_019/us_019.md`
- **Layer**: BE
- **Related Test Plans**: None (single-layer story)
- **Acceptance Criteria Covered**:
  - AC-001: Patient can browse available appointment slots filtered by date
  - AC-002: System returns cached slot list when available; populates cache on miss
  - AC-003: Patient can book an available slot; appointment created with Scheduled status and risk score
  - AC-004: System prevents concurrent booking of the same slot (slot unavailable → 409; concurrency exception → 409)
  - AC-005: Patient cannot have more than one active (Scheduled) appointment

## Test Plan Overview

Covers the backend unit tests for two tightly coupled handlers within US_019:
- `GET /slots?date=` via `GetSlotsEndpoint.HandleGetSlots` — slot list browsing with Redis cache
- `POST /appointments` via `BookAppointmentEndpoint.HandleBookAppointment` — atomic slot booking with risk-score assignment and Hangfire side-effects

Both handlers are tested as static methods invoked directly against an EF Core in-memory database. `ICacheService` and `IBackgroundJobClient` are mocked with Moq. `INoShowRiskScoreService` is also mocked to return a deterministic score.

AI Impact: **No** (no AIR-XXX requirements referenced in US_019).

## Dependent Tasks
- EP-TECH US_001–006 (infrastructure foundation — database seeding, JWT auth helpers) must be passing before running these tests.

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `GetSlotsEndpoint.HandleGetSlots` | static method | `src/ClinicalHealthcare.Api/Features/Appointments/GetSlotsEndpoint.cs` | Parse date param; cache hit returns list; cache miss queries DB and populates cache |
| `BookAppointmentEndpoint.HandleBookAppointment` | static method | `src/ClinicalHealthcare.Api/Features/Appointments/BookAppointmentEndpoint.cs` | Validate slot; check duplicate; save appointment; enqueue Hangfire jobs; invalidate cache |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | Cache hit returns 200 without DB hit | Cache returns non-null slot list for date key | `HandleGetSlots` called with valid date | 200 OK; `SetAsync` never called | `StatusCode == 200`; `SetAsync` mock verify `Times.Never` [SOURCE:INPUT] Basis: AC-001/AC-002 cache-first contract |
| TC-002 | positive | Cache miss queries DB and populates cache | Cache returns `null`; DB has 2 available slots | `HandleGetSlots` called with valid date | 200 OK; `SetAsync` called once with `CacheTtl = 60 s` | `StatusCode == 200`; slot list count = 2; `SetAsync` verify `Times.Once` [SOURCE:INPUT] Basis: AC-002 cache population |
| TC-003 | positive | Cache miss excludes unavailable slots | Cache returns `null`; DB has 1 available + 1 unavailable slot | `HandleGetSlots` called with valid date | 200 OK; only available slot returned | `StatusCode == 200`; result list count = 1 [SOURCE:INPUT] Basis: AC-001 availability filter |
| TC-004 | positive | Valid booking returns 201; slot marked unavailable | DB has available future slot; patient has no active appointments | `HandleBookAppointment` called with valid `SlotId` and JWT `sub` | 201 Created; slot `IsAvailable = false` in DB | `StatusCode == 201`; `db.Slots.Find(slotId).IsAvailable == false` [SOURCE:INPUT] Basis: AC-003 atomic booking |
| TC-005 | positive | `noShowRiskScore` stored on appointment; `IsHighRisk` set correctly | Risk service returns score 80; threshold = 70 | `HandleBookAppointment` called | 201; `Appointment.NoShowRiskScore == 80`; `IsHighRisk == true` | `db.Appointments.Single().NoShowRiskScore == 80`; `IsHighRisk == true` [SOURCE:INPUT] Basis: AC-003 risk score persistence |
| TC-006 | negative | Already-unavailable slot returns 409 | Slot exists with `IsAvailable = false` | `HandleBookAppointment` called | 409 Conflict; no appointment inserted | `StatusCode == 409`; `db.Appointments.Count() == 0` [SOURCE:INPUT] Basis: AC-004 slot conflict |
| TC-007 | negative | Patient with active appointment gets 409 | Slot is available; patient already has `Status = Scheduled` appointment | `HandleBookAppointment` called | 409 Conflict; no new appointment inserted | `StatusCode == 409`; appointment count unchanged [SOURCE:INPUT] Basis: AC-005 single-appointment guard |
| TC-008 | positive | Cache key deleted after booking | Successful booking | `HandleBookAppointment` completes | Cache `DeleteAsync` called with `slots:date:YYYY-MM-DD` key | `DeleteAsync` mock verify `Times.Once` with correct key [SOURCE:INPUT] Basis: AC-003 cache coherence |
| TC-009 | positive | Confirmation email job enqueued after booking | Successful booking | `HandleBookAppointment` completes | `jobs.Create` called with `SendConfirmationEmailJob` type | `jobs.Create(job.Type == typeof(SendConfirmationEmailJob))` `Times.Once` [SOURCE:INPUT] Basis: AC-003 side-effects |
| EC-001 | edge_case | Past slot date returns 400 | Slot `SlotTime` is 1 hour in the past | `HandleBookAppointment` called | 400 Bad Request | `StatusCode == 400` [SOURCE:INFERRED] Basis: endpoint guard `slot.SlotTime <= UtcNow` not explicit in AC but present in handler |
| EC-002 | edge_case | Null date query param returns 400 | No `?date=` query parameter | `HandleGetSlots(date: null)` called | 400 Bad Request | `StatusCode == 400` [SOURCE:INFERRED] Basis: input validation guard in handler |
| ES-001 | error | Zero SlotId returns 422 | `SlotId = 0` in request body | `HandleBookAppointment` called | 422 Unprocessable Entity | `StatusCode == 422` [SOURCE:INFERRED] Basis: `SlotId <= 0` validation guard in handler |

## AI Component Test Cases

> **AI Impact: No** — US_019 contains no AIR-XXX requirements. This section is skipped.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| EXISTS | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/GetSlotsEndpointTests.cs` | Tests for `GET /slots` (TC-001, TC-002, TC-003, EC-002 covered) |
| EXISTS | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/BookAppointmentEndpointTests.cs` | Tests for `POST /appointments` (TC-004, TC-006, TC-007, TC-008, TC-009, EC-001, ES-001 covered; TC-005 may need validation) |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ICacheService` | `Mock<ICacheService>` | `GetAsync<List<SlotDto>>(key, ct)` returns `null` for miss; returns pre-built list for hit | Cache hit: `List<SlotDto>` — Cache miss: `null` |
| `ICacheService.SetAsync` | `Mock<ICacheService>` | Verify called with `cacheKey`, `slots`, `TimeSpan.FromSeconds(60)` | `Task.CompletedTask` |
| `ICacheService.DeleteAsync` | `Mock<ICacheService>` | Verify called with `slots:date:YYYY-MM-DD` | `Task.CompletedTask` |
| `IBackgroundJobClient` | `Mock<IBackgroundJobClient>` | Verify `Create(job => job.Type == typeof(SendConfirmationEmailJob))` | Returns job ID string |
| `INoShowRiskScoreService` | `Mock<INoShowRiskScoreService>` | `CalculateAsync(patientId, slotTime, ct)` returns deterministic score | `Task.FromResult(80)` for high-risk; `Task.FromResult(20)` for low-risk |
| `HttpContext` | `DefaultHttpContext` | Injects `ClaimsPrincipal` with `sub = patientId.ToString()` | N/A |
| `IOptions<AppSettings>` | `Options.Create(...)` | `CancellationCutoffHours = 24`, `NoShowRiskThreshold = 70` | N/A |

## AI Mocking Strategy

> **AI Impact: No** — Skipped.

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Valid future slot | `SlotId = seeded_id`, `SlotTime = UtcNow.AddHours(72)`, `IsAvailable = true` | 201; appointment in DB |
| Slot unavailable | `SlotId = seeded_id`, `IsAvailable = false` | 409 Conflict |
| Past slot | `SlotId = seeded_id`, `SlotTime = UtcNow.AddHours(-1)`, `IsAvailable = true` | 400 Bad Request |
| Duplicate active appt | patient already has `Status = Scheduled` appointment | 409 Conflict |
| Zero SlotId | `SlotId = 0` in `BookAppointmentRequest` | 422 Unprocessable Entity |
| Cache hit | `ICacheService.GetAsync` returns `[{Id:1, SlotTime:..., IsAvailable:true}]` | 200; `SetAsync` not called |
| Cache miss | `ICacheService.GetAsync` returns `null`; DB has 2 available slots for date | 200; 2 slots; `SetAsync` called with 60 s TTL |

## Test Commands

- **Run Tests**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --filter "FullyQualifiedName~GetSlotsEndpointTests|FullyQualifiedName~BookAppointmentEndpointTests"`
- **Run with Coverage**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --collect:"XPlat Code Coverage" --results-directory ./coverage`
- **Run Single Test**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --filter "FullyQualifiedName~BookAppointment_ValidSlot_Returns201"`

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**:
  - `HandleBookAppointment` — all guard branches (slot null, unavailable, past date, duplicate patient, invalid SlotId)
  - `HandleGetSlots` — cache hit vs cache miss branching; null/invalid date input guard
  - `noShowRiskScore` persistence and `IsHighRisk` threshold comparison

## Documentation References

- **Framework Docs**: [xUnit 2.x — https://xunit.net/docs/getting-started/netcore/cmdline](https://xunit.net)
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/GetSlotsEndpointTests.cs`
- **Mocking Guide**: [Moq 4.x — https://github.com/devlooped/moq](https://github.com/devlooped/moq)

## Implementation Checklist

- [x] Create test file structure per Expected Changes
- [x] Set up test data fixtures per Test Data section
- [x] Configure mocking dependencies per Mocking Strategy
- [x] Implement positive test cases (TC-001 – TC-009)
- [x] Implement negative test cases (TC-006, TC-007)
- [x] Implement edge case tests (EC-001, EC-002)
- [x] Implement error scenario tests (ES-001)
- [x] Run test suite and validate coverage meets target (75 tests passed, 0 failed — 2026-05-24)
