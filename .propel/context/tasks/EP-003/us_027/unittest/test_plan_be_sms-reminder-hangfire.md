# Unit Test Plan - US_027

## Requirement Reference
- **User Story**: us_027
- **Story Location**: `.propel/context/tasks/EP-003/us_027/us_027.md`
- **Layer**: BE
- **Acceptance Criteria Covered**:
  - AC-001: SMS reminder sent via `ISmsGateway.SendAsync` with appointment date, time, and `APT-{id:D6}` reference
  - AC-002: Patient phone number normalised to E.164 format before `SendAsync` call
  - AC-003: Missing or invalid phone number → skips gracefully; no exception thrown
  - AC-004: `AutomaticRetry(Attempts=3, DelaysInSeconds=[30,60,120])` attribute applied to job
  - AC-005: Non-Scheduled appointment (e.g. Cancelled, Completed) → skips without sending SMS

## Test Plan Overview

Covers backend unit tests for the Hangfire SMS reminder job:

1. **`SendSmsReminderJob.ExecuteAsync(int appointmentId, string reminderLabel, IJobCancellationToken cancellationToken)`** — Hangfire job that loads appointment, validates status, normalises phone to E.164, builds ≤160-char SMS body, and calls `ISmsGateway.SendAsync`.
2. **`PhoneNormalizer.ToE164(string? phone)`** — normalises raw phone number strings; returns `null` for invalid/missing inputs (tested indirectly through job).

> **`ISmsGateway` is injectable** (`SendSmsReminderJob(db, sms, logger)`) — all tests use `Mock<ISmsGateway>()` without any env-var workaround. This is the key difference from US_026 and US_028.

AI Impact: **No** (no AIR-XXX requirements referenced in US_027).

## Dependent Tasks
- EP-TECH US_001–006 (infrastructure foundation) must be passing.

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `SendSmsReminderJob.ExecuteAsync` | Hangfire job method | `src/ClinicalHealthcare.Infrastructure/Jobs/SendSmsReminderJob.cs` | Load appointment; status guard; normalise phone; build body; call `ISmsGateway.SendAsync` |
| `PhoneNormalizer.ToE164` | static method | `src/ClinicalHealthcare.Infrastructure/PhoneNormalizer.cs` | Parse and format phone to E.164; return `null` for invalid inputs |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | Valid E.164 phone → `ISmsGateway.SendAsync` called once with `+1...` format | Scheduled appointment with phone `"+14155552671"` | `ExecuteAsync(apptId, "T-24h")` called | `SendAsync` called exactly once with `"+14155552671"` as `to` parameter | `sms.Verify(s => s.SendAsync("+14155552671", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once)` [SOURCE:INPUT] Basis: AC-001, AC-002 |
| TC-002 | positive | Raw phone format normalised to E.164 before gateway call | Scheduled appointment with phone `"(415) 555-2671"` | `ExecuteAsync` called | `SendAsync` called with `"+14155552671"` (E.164 normalised) | `capturedTo == "+14155552671"` [SOURCE:INPUT] Basis: AC-002 E.164 normalisation |
| TC-003 | positive | SMS body contains `reminderLabel`, appointment datetime, and `APT-{id:D6}` ref | Scheduled appointment with valid phone; `reminderLabel = "T-2h"` | `ExecuteAsync(apptId, "T-2h")` | SMS body includes `"T-2h"`, `"APT-"`, datetime in ISO format | `body.Contains("T-2h")`; `body.Contains($"APT-{appt.Id:D6}")` [SOURCE:INPUT] Basis: AC-001 content |
| TC-004 | positive | SMS body is ≤ 160 chars (single-segment; no MMS needed) | Scheduled appointment; `reminderLabel = "T-48h"` | `ExecuteAsync` called | Body length ≤ 160 | `capturedBody.Length <= 160` [SOURCE:INPUT] Basis: AC-001 body length constraint |
| TC-005 | negative | Null phone number → skips gracefully; `SendAsync` never called; no exception | Patient has `PhoneNumber = null` | `ExecuteAsync` called | Method completes; `SendAsync Times.Never` | `sms.Verify(Times.Never)`; no exception [SOURCE:INPUT] Basis: AC-003 no phone |
| TC-006 | negative | Invalid phone (non-parseable string) → skips gracefully; `SendAsync` never called | Patient has `PhoneNumber = "not-a-phone"` | `ExecuteAsync` called | Method completes; `SendAsync Times.Never` | `sms.Verify(Times.Never)`; no exception [SOURCE:INFERRED] Basis: AC-003 invalid phone |
| TC-007 | negative | Cancelled appointment → skips without SMS | Appointment `Status = Cancelled` | `ExecuteAsync` called | `SendAsync Times.Never` | `sms.Verify(Times.Never)` [SOURCE:INPUT] Basis: AC-005 status guard |
| EC-001 | edge_case | Appointment not found → returns cleanly; `SendAsync` never called | `appointmentId = 999` not in DB | `ExecuteAsync(999, "T-24h")` called | Method completes without exception | No exception; `sms.Verify(Times.Never)` [SOURCE:INFERRED] Basis: null appointment guard |
| ES-001 | error | `AutomaticRetry` attribute has `Attempts=3` and delays `[30,60,120]` | Reflect on `SendSmsReminderJob.ExecuteAsync` | Read `AutomaticRetryAttribute` | `Attempts == 3`; delays match | Attribute reflection assertions [SOURCE:INPUT] Basis: AC-004 retry policy |

## AI Component Test Cases

> **AI Impact: No** — US_027 contains no AIR-XXX requirements. This section is skipped.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| EXISTS | `tests/ClinicalHealthcare.Infrastructure.Tests/Jobs/SendSmsReminderJobTests.cs` | All test cases for this story already implemented and passing (7 tests confirmed) |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ApplicationDbContext` | EF Core In-Memory | `UseInMemoryDatabase(Guid.NewGuid().ToString())` + `TransactionIgnoredWarning` suppressed | Full in-memory DB per test |
| `ISmsGateway` | `Mock<ISmsGateway>` | `SendAsync(to, body, ct)` → `Task.CompletedTask` by default; use `Callback<string, string, CancellationToken>` to capture params for body assertions | Configurable |
| `ILogger` | `NullLogger` or `Mock<ILogger>` | Log calls captured; no side effects | N/A |
| `IJobCancellationToken` | `new JobCancellationToken(false)` | Non-cancelled token | N/A |

## AI Mocking Strategy

> **AI Impact: No** — Skipped.

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Valid E.164 phone | Patient phone `+14155552671`; Status `Scheduled`; `reminderLabel = "T-24h"` | `SendAsync` called once with `+14155552671`; body ≤ 160 chars |
| Raw format phone | Patient phone `(415) 555-2671`; Status `Scheduled` | `SendAsync` called with `+14155552671` |
| Null phone | `PhoneNumber = null`; Status `Scheduled` | `SendAsync` not called |
| Invalid phone | `PhoneNumber = "abc-xyz"`; Status `Scheduled` | `SendAsync` not called |
| Cancelled appointment | Status `Cancelled`; valid phone | `SendAsync` not called |
| Appointment not found | `appointmentId = 999` | Method completes; no exception |
| SMS body content | `reminderLabel = "T-2h"`, `appt.Id = 42` | Body contains `"T-2h"` and `"APT-000042"` |

## Test Commands

- **Run Tests**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --filter "FullyQualifiedName~SendSmsReminderJobTests"`
- **Run with Coverage**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --collect:"XPlat Code Coverage" --results-directory ./coverage`
- **Run Single Test**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --filter "FullyQualifiedName~ExecuteAsync_ValidPhone_SendsSmsWithE164"`

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 88%
- **Critical Paths**:
  - `ExecuteAsync` — null appointment guard; status guard (`!= Scheduled → return`); `PhoneNormalizer.ToE164` null return guard; `SendAsync` invocation
  - SMS body composition — label injection; `APT-{id:D6}` format; 160-char limit

## Documentation References

- **Framework Docs**: [xUnit 2.x — https://xunit.net](https://xunit.net)
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Jobs/SendSmsReminderJobTests.cs`
- **Mocking Guide**: [Moq 4.x — https://github.com/devlooped/moq](https://github.com/devlooped/moq)
- **Hangfire**: https://docs.hangfire.io/en/latest/
- **E.164 Phone Format**: https://www.twilio.com/docs/glossary/what-e164

## Implementation Checklist

- [x] Create test file structure per Expected Changes
- [x] Set up test data fixtures per Test Data section
- [x] Configure mocking dependencies per Mocking Strategy
- [x] Implement positive test cases (TC-001 – TC-004)
- [x] Implement negative test cases (TC-005 – TC-007)
- [x] Implement edge case tests (EC-001)
- [x] Implement error scenario tests (ES-001)
- [x] Run test suite and validate coverage meets target
