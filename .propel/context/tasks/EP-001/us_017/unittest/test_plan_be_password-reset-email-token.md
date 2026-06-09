# Unit Test Plan - TASK_017

## Requirement Reference
- **User Story**: us_017
- **Story Location**: `.propel/context/tasks/EP-001/us_017/us_017.md`
- **Layer**: BE
- **Related Test Plans**: None (single-layer feature)
- **Acceptance Criteria Covered**:
  - AC-001: `POST /auth/forgot-password` → 200 with generic message; token emailed to known active address; always 200 (no enumeration)
  - AC-002: Valid token within 60 min resets password; token marked used; Redis sessions revoked
  - AC-003: Expired reset token returns 400
  - AC-004: Already-used token returns 400
  - AC-005: New password failing complexity → 422; token NOT consumed

## Test Plan Overview
Covers the password reset vertical slice: `ForgotPasswordEndpoint.HandleForgotPassword` (token generation, email dispatch, cooldown, enumeration prevention) and `ResetPasswordEndpoint.HandleResetPassword` (token validation, PBKDF2 re-hash, Redis session revocation). Redis interactions mocked with Moq. Real `PasswordHasher<string>` verifies PBKDF2 hashing behavior. Both endpoints are tested as pure handler unit tests against an InMemory DbContext.

## Dependent Tasks
- TASK_007 — `UserAccount` entity with password-reset token fields must exist
- TASK_015 — Redis session revocation keys (`user-sessions:{userId}` set) established by TASK_015

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `ForgotPasswordEndpoint.HandleForgotPassword` | static method | `src/ClinicalHealthcare.Api/Features/Auth/ForgotPasswordEndpoint.cs` | Generates reset token, stores PBKDF2 hash, sends email; always returns 200 |
| `ForgotPasswordEndpoint.ComputePbkdf2Hash` | internal static | `src/ClinicalHealthcare.Api/Features/Auth/ForgotPasswordEndpoint.cs` | PBKDF2 SHA-256 hash used for token storage and verification |
| `ResetPasswordEndpoint.HandleResetPassword` | static method | `src/ClinicalHealthcare.Api/Features/Auth/ResetPasswordEndpoint.cs` | Validates token hash + expiry + used flag, updates password, revokes Redis sessions |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 [SOURCE:INPUT] | positive | Known active email → 200 and token stored with 60-min expiry | Active UserAccount seeded | `HandleForgotPassword` called with known email | HTTP 200; `PasswordResetTokenHash` != null; expiry ≈ now+60min | Status 200; `account.PasswordResetTokenHash != null`; expiry in range `[now+59m, now+60m5s]` — Basis: AC-001 |
| TC-002 [SOURCE:INPUT] | positive | Unknown email returns 200 same message (no enumeration) | Empty DB | `HandleForgotPassword` called with unregistered email | HTTP 200; no token stored | Status 200; no UserAccount row created — Basis: AC-001 enumeration prevention |
| TC-003 [SOURCE:INPUT] | positive | Valid token resets password and updates hash | Active UserAccount with valid unexpired token | `HandleResetPassword` called with correct token + new password | HTTP 200; `PasswordHash` updated | Status 200; `account.PasswordHash` differs from original hash — Basis: AC-002 |
| TC-004 [SOURCE:INPUT] | positive | Token marked used after successful reset | Active UserAccount with valid token | `HandleResetPassword` called | `PasswordResetTokenUsed = true` | `account.PasswordResetTokenUsed == true` — Basis: AC-002 single-use |
| TC-005 [SOURCE:INPUT] | positive | Redis sessions revoked after reset | Active UserAccount; Moq multiplexer | `HandleResetPassword` called | `SetMembersAsync` + `KeyDeleteAsync` called for user-sessions set | Moq verifies `SetMembersAsync($"user-sessions:{userId}")` invoked — Basis: AC-002 |
| TC-006 [SOURCE:INPUT] | negative | Expired reset token returns 400 | `PasswordResetTokenExpiry = UtcNow - 1min` | `HandleResetPassword` called | HTTP 400 | `IStatusCodeHttpResult.StatusCode == 400` — Basis: AC-003 |
| TC-007 [SOURCE:INPUT] | negative | Already-used token returns 400 | `PasswordResetTokenUsed = true` | `HandleResetPassword` called | HTTP 400 | `IStatusCodeHttpResult.StatusCode == 400` — Basis: AC-004 |
| TC-008 [SOURCE:INPUT] | negative | Password shorter than 8 chars returns 422 and token not consumed | Valid unexpired token | `HandleResetPassword` called with `newPassword = "short"` | HTTP 422; `PasswordResetTokenUsed` remains false | Status 422; `account.PasswordResetTokenUsed == false` — Basis: AC-005 |
| EC-001 [SOURCE:INPUT] | edge_case | Duplicate request within 5-minute cooldown returns 200 without issuing new token | `PasswordResetTokenIssuedAt = UtcNow - 3min` | `HandleForgotPassword` called again | HTTP 200; `PasswordResetTokenHash` unchanged | Token hash unchanged; no new email sent — Basis: per-email cooldown prevents flooding |
| EC-002 [SOURCE:INFERRED] | edge_case | Inactive account email returns 200 silently (no email) | Inactive UserAccount (`IsActive=false`) | `HandleForgotPassword` called | HTTP 200; no token set | `account.PasswordResetTokenHash == null`; no email dispatched — Basis: reset flow requires active account |
| ES-001 [SOURCE:INPUT] | error | Missing email/token/password returns 422 | Fresh DB | `HandleResetPassword` called with empty fields | HTTP 422 | `IStatusCodeHttpResult.StatusCode == 422` — Basis: AC-005 input validation |

## AI Component Test Cases
> Skipped — AI Impact = No (no AIR-XXX requirements in scope for this story).

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE (exists) | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/PasswordResetEndpointTests.cs` | Password reset endpoint tests — already implemented |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `IEmailService` | `FakeEmailService` stub or `Moq<IEmailService>` | Captures or discards email; no SMTP | `Task.CompletedTask` |
| `IConnectionMultiplexer` | `Moq<IConnectionMultiplexer>` | Returns mock `IDatabase`; `SetMembersAsync` returns empty set by default | `Mock<IDatabase>.Object` |
| `IPasswordHasher<string>` | Real `PasswordHasher<string>` | Real PBKDF2 hashing and verification | `PasswordVerificationResult` |
| `ApplicationDbContext` | EF Core InMemory | Isolated per-test; `TransactionIgnoredWarning` suppressed | In-memory store |
| `IConfiguration` | `ConfigurationBuilder` in-memory | `App:BaseUrl`, SMTP settings | IConfiguration |
| `ILoggerFactory` | `LoggerFactory.Create(_ => {})` | No-op | ILoggerFactory |

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Valid forgot-password | Known active email | HTTP 200; token stored; email sent |
| Unknown email | Unregistered address | HTTP 200; no token; no email |
| Valid reset | Correct token + new password | HTTP 200; password updated; token used |
| Expired token | `PasswordResetTokenExpiry = UtcNow - 1min` | HTTP 400 |
| Used token | `PasswordResetTokenUsed = true` | HTTP 400 |
| Weak new password | `newPassword = "short"` | HTTP 422; token not consumed |
| Cooldown | Token issued 3 min ago, second request | HTTP 200; no new token |
| Inactive account | `IsActive = false` | HTTP 200; no token; no email |
| Missing fields | Blank email/token/password | HTTP 422 |

## Test Commands
- **Run Tests**: `dotnet test ClinicalHealthcare.slnx --filter "FullyQualifiedName~PasswordResetEndpointTests" -v q`
- **Run with Coverage**: `dotnet test ClinicalHealthcare.slnx --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~PasswordResetEndpointTests"`
- **Run Single Test**: `dotnet test ClinicalHealthcare.slnx --filter "FullyQualifiedName~PasswordResetEndpointTests.ForgotPassword_KnownActiveEmail_Returns200AndSetsToken"`

## Coverage Target
- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**: `HandleResetPassword` expired/used/hash-mismatch token branches; `HandleForgotPassword` cooldown and inactive-account branches must have 100% coverage

## Documentation References
- **Framework Docs**: xUnit 2.x — https://xunit.net/docs
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/PasswordResetEndpointTests.cs`
- **Mocking Guide**: `Moq<IConnectionMultiplexer>` + `Moq<IDatabase>` for Redis session revocation verification

## Implementation Checklist
- [x] Create test file structure per Expected Changes (`PasswordResetEndpointTests.cs` exists)
- [x] Set up test data fixtures per Test Data section (`SeedUser` helper; inline token data)
- [x] Configure mocking dependencies per Mocking Strategy (Moq Redis, real hasher)
- [x] Implement positive test cases (TC-001 through TC-005)
- [x] Implement negative test cases (TC-006, TC-007, TC-008)
- [x] Implement edge case tests (EC-001, EC-002)
- [x] Implement error scenario tests (ES-001)
- [x] Run test suite and validate coverage meets target (101/101 tests green — 2026-05-24)
