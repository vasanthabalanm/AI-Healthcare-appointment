# Unit Test Plan - TASK_012

## Requirement Reference
- **User Story**: us_012
- **Story Location**: `.propel/context/tasks/EP-001/us_012/us_012.md`
- **Layer**: BE
- **Related Test Plans**: None (single-layer feature)
- **Acceptance Criteria Covered**:
  - AC-001: `POST /auth/register` returns 201 with verification email dispatched
  - AC-002: `GET /auth/verify-email?token=` activates account, token consumed
  - AC-003: Duplicate email returns 409
  - AC-004: Expired verification token returns 400
  - AC-005: Resend verification (endpoint not yet implemented — tests pending endpoint creation)

## Test Plan Overview
Covers the patient self-registration vertical slice (`RegisterEndpoint`). Tests exercise handler functions directly (no WebApplicationFactory needed) using an InMemory EF Core database and a `FakeEmailService` stub. Rate-limiting (429) is an integration-test concern and is out of scope here. AC-005 resend endpoint tests are deferred until `POST /auth/resend-verification` is implemented.

## Dependent Tasks
- TASK_007 — `UserAccount` entity must exist in `ApplicationDbContext`

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `RegisterEndpoint.HandleRegister` | static method | `src/ClinicalHealthcare.Api/Features/Auth/RegisterEndpoint.cs` | Validates input, checks duplicate email, hashes password, generates token, inserts UserAccount, dispatches email |
| `RegisterEndpoint.HandleVerifyEmail` | static method | `src/ClinicalHealthcare.Api/Features/Auth/RegisterEndpoint.cs` | Validates token hash, checks expiry, activates account, consumes token |
| `RegisterEndpoint.ValidateRequest` | private static | `src/ClinicalHealthcare.Api/Features/Auth/RegisterEndpoint.cs` | Field-level validation returning error dictionary |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 [SOURCE:INPUT] | positive | Valid request returns HTTP 201 | No existing UserAccount for `alice@test.com`; all fields valid | `HandleRegister` called with valid `RegisterRequest` | HTTP 201 Created | `IStatusCodeHttpResult.StatusCode == 201` — Basis: AC-001 |
| TC-002 [SOURCE:INPUT] | positive | Created account has role=patient and IsActive=false | Fresh InMemory DB | `HandleRegister` called with valid request | UserAccount row inserted with correct values | `account.Role == "patient"`, `account.IsActive == false` — Basis: AC-001, AC-005 |
| TC-003 [SOURCE:INPUT] | positive | Verification token stored as SHA-256 hash with 24h expiry | Fresh InMemory DB | `HandleRegister` called with valid request | `VerificationTokenHash` != null, `VerificationTokenExpiry` ≈ now+24h | Hash not null; expiry in range `[now+23h59m, now+24h5s]` — Basis: AC-002 |
| TC-004 [SOURCE:INPUT] | positive | Verification email sent to registrant address | `FakeEmailService` stub injected | `HandleRegister` called | Email captured by stub with `to=registrant` and subject containing "Verify" | `fakeEmail.LastToEmail == "alice@test.com"` — Basis: AC-001 |
| TC-005 [SOURCE:INPUT] | positive | Valid token activates account and token is consumed | UserAccount with valid unexpired token; `VerificationTokenHash` set | `HandleVerifyEmail` called with matching raw token | HTTP 200; `IsActive=true`; `VerificationTokenHash=null` | Status 200, `account.IsActive == true`, `account.VerificationTokenHash == null` — Basis: AC-002 |
| TC-006 [SOURCE:INPUT] | negative | Duplicate email returns 409 | UserAccount already exists for `frank@test.com` | `HandleRegister` called with same email | HTTP 409 Conflict | `IStatusCodeHttpResult.StatusCode == 409` — Basis: AC-003 |
| TC-007 [SOURCE:INPUT] | negative | Expired verification token returns 400 | UserAccount with `VerificationTokenExpiry = UtcNow - 1min` | `HandleVerifyEmail` called with raw token | HTTP 400 Bad Request | `IStatusCodeHttpResult.StatusCode == 400` — Basis: AC-004 |
| TC-008 [SOURCE:INPUT] | negative | Password shorter than 8 characters returns 422 | Fresh InMemory DB | `HandleRegister` called with `Password = "short"` | HTTP 422 Unprocessable Entity | `IStatusCodeHttpResult.StatusCode == 422`; no UserAccount row inserted — Basis: AC-001 validation |
| EC-001 [SOURCE:INPUT] | edge_case | Token used twice returns 400 on second use | UserAccount with valid token; first `HandleVerifyEmail` call succeeds | `HandleVerifyEmail` called again with same raw token | HTTP 400 (token hash was nulled after first use) | `IStatusCodeHttpResult.StatusCode == 400` on second call — Basis: AC-002 one-time token |
| EC-002 [SOURCE:INFERRED] | edge_case | Case-variant duplicate email returns 409 | UserAccount exists for `Grace@test.com` | `HandleRegister` called with `grace@TEST.COM` | HTTP 409 | Status 409 — Basis: email normalisation to lowercase prevents case-variant duplicates |
| ES-001 [SOURCE:INFERRED] | error | Blank token string returns 400 | Fresh InMemory DB | `HandleVerifyEmail` called with `token = ""` | HTTP 400 Bad Request | `IStatusCodeHttpResult.StatusCode == 400` — Basis: missing token is not a valid lookup key |

## AI Component Test Cases
> Skipped — AI Impact = No (no AIR-XXX requirements in scope for this story).

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE (exists) | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/RegisterEndpointTests.cs` | Unit tests for `RegisterEndpoint` — already implemented |
| CREATE (pending) | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/ResendVerificationEndpointTests.cs` | Unit tests for `POST /auth/resend-verification` (AC-005 — endpoint not yet implemented) |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `IEmailService` | `FakeEmailService` stub | Captures `toEmail`, `subject`, `htmlBody` on `SendAsync` | `Task.CompletedTask` |
| `IPasswordHasher<string>` | Real `PasswordHasher<string>` | ASP.NET Core Identity V3 PBKDF2 | Hashed password string |
| `IConfiguration` | `ConfigurationBuilder` in-memory | Provides `App:BaseUrl = "https://localhost:7001"` | IConfiguration |
| `ApplicationDbContext` | EF Core InMemory | Isolated per-test database (`Guid.NewGuid()`) | In-memory store |
| `HttpContext` | `new DefaultHttpContext()` | No IP address; rate limiting not invoked | DefaultHttpContext |

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Valid registration | `{ Email: "alice@test.com", Password: "P@ssword1", FirstName: "Alice", LastName: "Smith" }` | HTTP 201; `role=patient`; `IsActive=false` |
| Duplicate email | Same email twice | HTTP 409 on second attempt |
| Expired token | `VerificationTokenExpiry = UtcNow - 1min` | HTTP 400 |
| Short password | `Password: "short"` | HTTP 422 |
| Blank token | `token = ""` | HTTP 400 |

## Test Commands
- **Run Tests**: `dotnet test ClinicalHealthcare.slnx --filter "FullyQualifiedName~RegisterEndpointTests" -v q`
- **Run with Coverage**: `dotnet test ClinicalHealthcare.slnx --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~RegisterEndpointTests"`
- **Run Single Test**: `dotnet test ClinicalHealthcare.slnx --filter "FullyQualifiedName~RegisterEndpointTests.Register_WithValidRequest_Returns201"`

## Coverage Target
- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**: `HandleRegister` duplicate-email branch, `HandleVerifyEmail` expiry and consumed-token branches must have 100% coverage

## Documentation References
- **Framework Docs**: xUnit 2.x — https://xunit.net/docs
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/RegisterEndpointTests.cs`
- **Mocking Guide**: Real `PasswordHasher<string>` + `FakeEmailService` (no Moq needed for this endpoint)

## Implementation Checklist
- [x] Create test file structure per Expected Changes (`RegisterEndpointTests.cs` exists)
- [x] Set up test data fixtures per Test Data section (inline data in test methods)
- [x] Configure mocking dependencies per Mocking Strategy (`FakeEmailService` implemented in test file)
- [x] Implement positive test cases (TC-001 through TC-005)
- [x] Implement negative test cases (TC-006, TC-007, TC-008)
- [x] Implement edge case tests (EC-001, EC-002)
- [x] Implement error scenario tests (ES-001)
- [ ] Implement AC-005 resend-verification tests (pending `POST /auth/resend-verification` endpoint)
- [x] Run test suite and validate coverage meets target (101/101 tests green — 2026-05-24)
