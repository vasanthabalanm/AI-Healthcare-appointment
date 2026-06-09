# Unit Test Plan - US_022

## Requirement Reference
- **User Story**: us_022
- **Story Location**: `.propel/context/tasks/EP-002/us_022/us_022.md`
- **Layer**: BE
- **Related Test Plans**: None (single-layer story)
- **Acceptance Criteria Covered**:
  - AC-001: No-show risk score calculated and stored on appointment at booking time; score is capped at 100
  - AC-002: Score is the sum of three components: historical no-shows (×20, capped at 60), lead-time band (30/15/0), intake completeness (10/0)
  - AC-003: `IsHighRisk = true` when score ≥ `AppSettings.NoShowRiskThreshold` (default 70)
  - AC-004: `GET /schedule/today` returns `riskFlag` (`IsHighRisk`) per appointment row; cancelled appointments excluded

## Test Plan Overview

Covers backend unit tests for two components involved in no-show risk scoring:

1. **`NoShowRiskScoreService.CalculateAsync`** — pure calculation service tested against in-memory DB with seeded appointment history and intake records.
2. **`GetDailyScheduleEndpoint.HandleGetDailySchedule`** — staff schedule endpoint tested for `IsHighRisk` field inclusion and exclusion of cancelled appointments.

AI Impact: **No** (no AIR-XXX requirements referenced in US_022).

## Dependent Tasks
- EP-TECH US_001–006 (infrastructure foundation) and US_019 (booking — creates appointments with `IsHighRisk` field) must be passing.

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `NoShowRiskScoreService.CalculateAsync` | service method | `src/ClinicalHealthcare.Infrastructure/Services/NoShowRiskScoreService.cs` | Query historical no-shows + intake records; compute weighted risk score; cap at 100 |
| `GetDailyScheduleEndpoint.HandleGetDailySchedule` | static method | `src/ClinicalHealthcare.Api/Features/Staff/GetDailyScheduleEndpoint.cs` | Return today's appointments excluding Cancelled; include `riskFlag = IsHighRisk`; paginate at 50 |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | Maximum score — 3 no-shows, <24h lead, no intake → score ≥ 100 (capped) | 3 cancelled appointments (no-shows); `slotTime = UtcNow + 12h`; no IntakeRecord | `CalculateAsync(patientId, slotTime)` called | Score == 100 (capped from 60+30+10) | `score == 100` [SOURCE:INPUT] Basis: AC-001 cap + AC-002 all three components |
| TC-002 | positive | Minimum score — 0 no-shows, >72h lead, intake complete → score == 0 | No cancelled appointments; `slotTime = UtcNow + 96h`; IntakeRecord exists | `CalculateAsync` called | Score == 0 | `score == 0` [SOURCE:INPUT] Basis: AC-002 all components zero |
| TC-003 | positive | No-show component capped at 60 for 5 no-shows | 5 no-show appointments; >72h lead; intake complete | `CalculateAsync` called | Score == 60 (not 100) | `score == 60` [SOURCE:INPUT] Basis: AC-002 no-show cap |
| TC-004 | positive | Lead-time band 24–72h adds 15 points | 0 no-shows; `slotTime = UtcNow + 48h`; intake complete | `CalculateAsync` called | Score == 15 | `score == 15` [SOURCE:INPUT] Basis: AC-002 mid-band |
| TC-005 | positive | Lead-time <24h adds 30 points | 0 no-shows; `slotTime = UtcNow + 12h`; intake complete | `CalculateAsync` called | Score == 30 | `score == 30` [SOURCE:INPUT] Basis: AC-002 short lead |
| TC-006 | positive | No intake record adds 10 points | 0 no-shows; >72h lead; no IntakeRecord | `CalculateAsync` called | Score == 10 | `score == 10` [SOURCE:INPUT] Basis: AC-002 intake component |
| TC-007 | positive | `IsHighRisk = true` when score ≥ threshold (70) | Score calculated = 70 (exactly at threshold) | Booking handler compares against `AppSettings.NoShowRiskThreshold = 70` | `IsHighRisk == true` | `appointment.IsHighRisk == true` when `score >= 70` [SOURCE:INPUT] Basis: AC-003 |
| TC-008 | positive | Score never exceeds 100 even with all components at max | 10 no-shows; <24h lead; no intake (components: 60+30+10 = 100) | `CalculateAsync` called | Score == 100 (capped by `Math.Min`) | `score == 100` [SOURCE:INPUT] Basis: AC-001 |
| TC-009 | positive | `GetDailySchedule` returns `riskFlag = true` for high-risk appointment | Appointment with `IsHighRisk = true`; `Status = Scheduled`; slot on today | `HandleGetDailySchedule` called with `date = today` | 200 OK; response entry `IsHighRisk == true` | `data[0].RiskFlag == true` [SOURCE:INPUT] Basis: AC-004 |
| TC-010 | positive | `GetDailySchedule` excludes cancelled appointments | Two appointments: one Scheduled, one Cancelled; both on today | `HandleGetDailySchedule` called | Response contains only 1 entry | `totalCount == 1`; entry status is `Scheduled` [SOURCE:INPUT] Basis: AC-004 filter |
| EC-001 | edge_case | First-time patient (no history, no intake, far future slot) → score = 10 | 0 no-shows; >72h lead; no IntakeRecord | `CalculateAsync` called | Score == 10 (intake component only) | `score == 10` [SOURCE:INFERRED] Basis: new patient baseline — no-show and lead components are both 0; only intake gap contributes |
| EC-002 | edge_case | Lead-time boundary at exactly 72h → 15 points (mid-band not >72h band) | 0 no-shows; `slotTime = UtcNow + 72h exactly`; intake complete | `CalculateAsync` called | Score == 15 | `score == 15` [SOURCE:INFERRED] Basis: boundary condition — `24 ≤ leadHours ≤ 72 → 15` |
| ES-001 | error | Score at threshold exactly 70 → IsHighRisk classification | `NoShowRiskThreshold = 70`; calculated score = 70 | Booking or schedule check | `IsHighRisk == true` (threshold is inclusive ≥) | `appointment.IsHighRisk == true` [SOURCE:INFERRED] Basis: `score >= threshold` inclusive guard |

## AI Component Test Cases

> **AI Impact: No** — US_022 contains no AIR-XXX requirements. This section is skipped.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| EXISTS | `tests/ClinicalHealthcare.Infrastructure.Tests/Services/NoShowRiskScoreServiceTests.cs` | Tests for `NoShowRiskScoreService.CalculateAsync` (TC-001–008, EC-001, EC-002, ES-001 covered) |
| EXISTS | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/GetDailyScheduleEndpointTests.cs` | Tests for `GET /schedule/today` (TC-009, TC-010 covered) |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ApplicationDbContext` | EF Core In-Memory | `UseInMemoryDatabase(Guid.NewGuid().ToString())` with `TransactionIgnoredWarning` suppressed | Full in-memory DB per test |
| Seeded no-show history | Direct EF insert | Seed `Appointment` records with `Status = Cancelled` (no-shows) for target `patientId` | N/A |
| Seeded intake records | Direct EF insert | Seed `IntakeRecord` with `PatientId` and `IsLatest = true`, `IsDeleted = false` | N/A |
| `IOptions<AppSettings>` | `Options.Create(new AppSettings { NoShowRiskThreshold = 70 })` | Used by booking handler to set `IsHighRisk` | N/A |

## AI Mocking Strategy

> **AI Impact: No** — Skipped.

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| All-max components | 3 cancelled appts; `slotTime + 12h`; no intake | Score == 100 (capped) |
| All-zero components | 0 cancelled; `slotTime + 96h`; IntakeRecord seeded | Score == 0 |
| No-show cap | 5 cancelled; `slotTime + 96h`; IntakeRecord seeded | Score == 60 |
| Mid lead-time | 0 cancelled; `slotTime + 48h`; IntakeRecord seeded | Score == 15 |
| Short lead-time | 0 cancelled; `slotTime + 12h`; IntakeRecord seeded | Score == 30 |
| No intake | 0 cancelled; `slotTime + 96h`; no IntakeRecord | Score == 10 |
| High-risk schedule | Appointment `IsHighRisk = true`; `Status = Scheduled`; `SlotTime = today + 2h` | Schedule entry `riskFlag = true` |
| Cancelled excluded | 1 Scheduled + 1 Cancelled on same date | `totalCount == 1` |

## Test Commands

- **Run Tests**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --filter "FullyQualifiedName~NoShowRiskScoreServiceTests|FullyQualifiedName~GetDailyScheduleEndpointTests"`
- **Run with Coverage**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --collect:"XPlat Code Coverage" --results-directory ./coverage`
- **Run Single Test**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --filter "FullyQualifiedName~NoShowRiskScore_AllMax_Returns100"`

## Coverage Target

- **Line Coverage**: 92%
- **Branch Coverage**: 88%
- **Critical Paths**:
  - `CalculateAsync` — all three lead-time bands (< 24h, 24–72h, > 72h)
  - `CalculateAsync` — no-show cap at 60 (5+ no-shows)
  - `CalculateAsync` — intake component (has intake vs no intake)
  - `Math.Min(..., 100)` cap guard
  - `HandleGetDailySchedule` — cancelled appointment exclusion filter; `IsHighRisk` field mapping

## Documentation References

- **Framework Docs**: [xUnit 2.x — https://xunit.net](https://xunit.net)
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Services/NoShowRiskScoreServiceTests.cs`
- **Mocking Guide**: [Moq 4.x — https://github.com/devlooped/moq](https://github.com/devlooped/moq)

## Implementation Checklist

- [x] Create test file structure per Expected Changes
- [x] Set up test data fixtures per Test Data section
- [x] Configure mocking dependencies per Mocking Strategy
- [x] Implement positive test cases (TC-001 – TC-010)
- [x] Implement negative test cases (N/A — no negative inputs for pure calculation)
- [x] Implement edge case tests (EC-001, EC-002)
- [x] Implement error scenario tests (ES-001 threshold boundary)
- [x] Run test suite and validate coverage meets target (75 tests passed, 0 failed — 2026-05-24)
