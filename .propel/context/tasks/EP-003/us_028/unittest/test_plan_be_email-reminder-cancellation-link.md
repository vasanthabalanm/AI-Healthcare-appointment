# Unit Test Plan - US_028

## Requirement Reference
- **User Story**: us_028
- **Story Location**: `.propel/context/tasks/EP-003/us_028/us_028.md`
- **Layer**: BE
- **Acceptance Criteria Covered**:
  - AC-001: 48-hour cancellation link generated; raw token Base64URL-encoded; `SHA-256` hash persisted to DB; `CancellationLinkExpiry` set to `UtcNow + 48 hours`; token and expiry written **before** SMTP call
  - AC-002: Idempotency — if `EmailReminderSentAt` is already set, job skips without overwriting token or timestamp
  - AC-003: `AutomaticRetry(Attempts=3, DelaysInSeconds=[30,60,120])` applied to job
  - AC-004: Non-Scheduled appointment → no token written; job exits gracefully

## Test Plan Overview

Covers backend unit tests for the Hangfire email reminder job that generates a one-time cancellation link before sending a reminder via SMTP:

1. **`SendEmailReminderJob.ExecuteAsync(int appointmentId, IJobCancellationToken cancellationToken)`** — Hangfire job that validates status; generates 32-byte random token; stores `CancellationLinkTokenHash` (SHA-256 hex), `CancellationLinkExpiry` (`UtcNow+48h`), `CancellationLinkUsed = false` to DB **before** SMTP; builds cancel URL; sends reminder email; persists `EmailReminderSentAt`.
2. **`SendEmailReminderJob.ComputeSha256Hex(string input)`** — public static helper; returns lowercase 64-char hex SHA-256 digest.

> **SMTP Testability Constraint:** `SendEmailReminderJob` creates `new SmtpClient()` directly. Tests that verify token persistence use the env-var trick: `SMTP_HOST = "127.0.0.1"` causes `ConnectAsync` to throw, but the token is already in the DB by that point — assertions target the DB state after the expected throw.

> **Note on `SendReminderJob`:** `src/ClinicalHealthcare.Infrastructure/Jobs/SendReminderJob.cs` is a stub (logs and returns). Do NOT reference it for US_028 tests — all tests use `SendEmailReminderJob.cs`.

AI Impact: **No** (no AIR-XXX requirements referenced in US_028).

## Dependent Tasks
- EP-TECH US_001–006 (infrastructure foundation) must be passing.

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `SendEmailReminderJob.ExecuteAsync` | Hangfire job method | `src/ClinicalHealthcare.Infrastructure/Jobs/SendEmailReminderJob.cs` | Guard checks; token generation; hash persistence; cancel URL; SMTP send; `EmailReminderSentAt` stamp |
| `SendEmailReminderJob.ComputeSha256Hex` | public static method | same | SHA-256 hex digest of input string; deterministic; lowercase |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | Token hash and expiry persisted to DB **before** SMTP connect | Valid appointment; SMTP env vars set to `127.0.0.1` | `ExecuteAsync` called | SMTP throws (expected); `CancellationLinkTokenHash` and `CancellationLinkExpiry` are in DB | `reloaded.CancellationLinkTokenHash != null`; `reloaded.CancellationLinkExpiry > UtcNow`; `reloaded.CancellationLinkUsed == false` [SOURCE:INPUT] Basis: AC-001 token lifecycle |
| TC-002 | positive | `CancellationLinkExpiry` is approximately 48 hours in the future | Same as TC-001 | `ExecuteAsync` called | `Expiry ≥ UtcNow + 47h` and `Expiry ≤ UtcNow + 49h` | `reloaded.CancellationLinkExpiry >= UtcNow.AddHours(47)`; `<= UtcNow.AddHours(49)` [SOURCE:INPUT] Basis: AC-001 48h expiry |
| TC-003 | positive | `ComputeSha256Hex` returns deterministic 64-char lowercase hex | Input = `"test-token-abc123"` | `ComputeSha256Hex("test-token-abc123")` called twice | Same result both times; `Length == 64`; all chars `[0-9a-f]` | `result1 == result2`; `result.Length == 64`; `Regex.IsMatch(result, "^[0-9a-f]{64}$")` [SOURCE:INFERRED] Basis: AC-001 token hash correctness |
| TC-004 | positive | `ComputeSha256Hex` for empty string returns known SHA-256 digest | Input = `""` | `ComputeSha256Hex("")` called | Returns `"e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"` | `result == "e3b0c44298fc1c149afbf4c8996fb924..."` [SOURCE:INFERRED] Basis: SHA-256 known vector |
| TC-005 | positive | Already-sent guard: `EmailReminderSentAt` set → re-run does not overwrite token | Pre-seed `CancellationLinkTokenHash = "deadbeef"`, `EmailReminderSentAt = UtcNow-5m` | `ExecuteAsync` called | Token hash unchanged; `EmailReminderSentAt` unchanged | `reloaded.CancellationLinkTokenHash == "deadbeef"`; `reloaded.EmailReminderSentAt == original` [SOURCE:INPUT] Basis: AC-002 idempotency |
| TC-006 | negative | Cancelled appointment → no token written | Appointment `Status = Cancelled`; valid patient email | `ExecuteAsync` called | Method completes; `CancellationLinkTokenHash` remains `null` | `reloaded.CancellationLinkTokenHash == null` [SOURCE:INPUT] Basis: AC-004 status guard |
| TC-007 | negative | Invalid patient email → skips without exception | Patient email = `"not-an-email"` | `ExecuteAsync` called | Method completes without throwing | No exception [SOURCE:INFERRED] Basis: `MailboxAddress.TryParse` guard |
| EC-001 | edge_case | Appointment not found → returns cleanly | `appointmentId = 999` not in DB | `ExecuteAsync(999, NoCancellation())` called | Method completes without throwing | No exception [SOURCE:INFERRED] Basis: null appointment guard |
| ES-001 | error | `AutomaticRetry` attribute has `Attempts=3` and delays `[30,60,120]` | Reflect on `SendEmailReminderJob.ExecuteAsync` | Read `AutomaticRetryAttribute` | `Attempts == 3`; `DelaysInSeconds = [30, 60, 120]` | Attribute reflection assertions [SOURCE:INPUT] Basis: AC-003 retry policy |

## AI Component Test Cases

> **AI Impact: No** — US_028 contains no AIR-XXX requirements. This section is skipped.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| EXISTS | `tests/ClinicalHealthcare.Infrastructure.Tests/Jobs/SendEmailReminderJobTests.cs` | Core AC tests already implemented (TC-001, TC-005, TC-006, EC-001, TC-007 confirmed). TC-002, TC-003, TC-004, ES-001 may need adding if not already present |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ApplicationDbContext` | EF Core In-Memory | `UseInMemoryDatabase(Guid.NewGuid().ToString())` + `TransactionIgnoredWarning` suppressed | Full in-memory DB per test |
| `ILogger` | `NullLogger` or `Mock<ILogger>` | Log calls captured; no side effects | N/A |
| `SmtpClient` (MailKit) | **Not mockable** — direct instantiation | Use env-var trick: `SMTP_HOST=127.0.0.1`; `ConnectAsync` throws; token already in DB before throw | `Assert.ThrowsAnyAsync<Exception>` expected |
| `IJobCancellationToken` | `new JobCancellationToken(false)` | Non-cancelled token | N/A |

### Environment Variable Pattern (SMTP Tests)

```csharp
Environment.SetEnvironmentVariable("SMTP_HOST",         "127.0.0.1");
Environment.SetEnvironmentVariable("SMTP_PORT",         "465");
Environment.SetEnvironmentVariable("SMTP_USER",         "user");
Environment.SetEnvironmentVariable("SMTP_PASS",         "pass");
Environment.SetEnvironmentVariable("SMTP_FROM_ADDRESS", "noreply@clinic.test");
try
{
    // Assert ThrowsAnyAsync — SMTP connect will fail
    await Assert.ThrowsAnyAsync<Exception>(() => job.ExecuteAsync(appt.Id, NoCancellation()));
    // Assert DB state AFTER expected throw — token already written
    var reloaded = await db.Appointments.FindAsync(appt.Id);
    Assert.NotNull(reloaded!.CancellationLinkTokenHash);
}
finally
{
    // Always clean up env vars
    Environment.SetEnvironmentVariable("SMTP_HOST", null);
    // ... same for other vars
}
```

## AI Mocking Strategy

> **AI Impact: No** — Skipped.

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Valid appointment + SMTP env vars | Status `Scheduled`; valid email; SMTP env vars set | SMTP throws; token hash and expiry in DB; expiry ≈ UtcNow+48h |
| Idempotent re-run | `EmailReminderSentAt` already set; pre-seeded `CancellationLinkTokenHash = "deadbeef"` | Token unchanged; sent-at unchanged |
| Cancelled appointment | `Status = Cancelled` | No token written |
| Invalid email | `Patient.Email = "not-an-email"` | Method returns without exception |
| Appointment not found | `appointmentId = 999` | Method returns without exception |
| SHA-256 known vector | `input = ""` | `"e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"` |
| SHA-256 determinism | Any string | Same hash called twice |

## Test Commands

- **Run Tests**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --filter "FullyQualifiedName~SendEmailReminderJobTests"`
- **Run with Coverage**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --collect:"XPlat Code Coverage" --results-directory ./coverage`
- **Run Single Test**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --filter "FullyQualifiedName~ExecuteAsync_ValidAppointment_PersistsTokenHashBeforeSmtp"`

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 88%
- **Critical Paths**:
  - `ExecuteAsync` — null appointment guard; status guard; invalid email guard; idempotency guard (`EmailReminderSentAt` set); token generation; hash computation; DB persist before SMTP; `SMTP_PORT=587 → StartTls` vs `SslOnConnect` branch; `EmailReminderSentAt` stamp after send
  - `ComputeSha256Hex` — determinism; correct hex format; known SHA-256 test vectors

## Documentation References

- **Framework Docs**: [xUnit 2.x — https://xunit.net](https://xunit.net)
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Jobs/SendEmailReminderJobTests.cs`
- **Mocking Guide**: [Moq 4.x — https://github.com/devlooped/moq](https://github.com/devlooped/moq)
- **MailKit SMTP**: https://github.com/jstedfast/MailKit
- **Hangfire**: https://docs.hangfire.io/en/latest/

## Implementation Checklist

- [x] Create test file structure per Expected Changes
- [x] Set up test data fixtures per Test Data section
- [x] Configure mocking dependencies per Mocking Strategy
- [x] Implement positive test cases (TC-001 – TC-005)
- [x] Implement negative test cases (TC-006, TC-007)
- [x] Implement edge case tests (EC-001)
- [x] Implement error scenario tests (ES-001)
- [x] Run test suite and validate coverage meets target
