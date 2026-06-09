# Unit Test Plan - TASK_015

## Requirement Reference
- **User Story**: us_015
- **Story Location**: `.propel/context/tasks/EP-001/us_015/us_015.md`
- **Layer**: BE
- **Related Test Plans**: None (single-layer feature)
- **Acceptance Criteria Covered**:
  - AC-001: Login issues JWT with 15-minute TTL; Redis allowlist entry created
  - AC-002: Authenticated request resets Redis TTL; request succeeds
  - AC-003: Token absent from Redis → 401 regardless of JWT validity
  - AC-004: `POST /auth/extend-session` issues new token and rotates Redis entry
  - AC-005: `POST /auth/logout` removes token from Redis
  - AC-006: 5 consecutive failed logins trigger account lockout (15-minute)

## Test Plan Overview
Covers the JWT session management vertical slice: `JwtTokenService` (token generation), `LoginEndpoint.HandleLogin` (credential validation, Redis write), `LogoutEndpoint.HandleLogout` (Redis DEL), `ExtendSessionEndpoint.HandleExtendSession` (token rotation), and `SessionTtlMiddleware` (Redis allowlist check + TTL reset per request). Redis interactions are mocked via `Moq<IConnectionMultiplexer>`. JWT operations use a real `JwtTokenService` with a test secret set in the environment.

## Dependent Tasks
- TASK_007 — `UserAccount` entity must exist in `ApplicationDbContext`

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `JwtTokenService` | service class | `src/ClinicalHealthcare.Infrastructure/Auth/JwtTokenService.cs` | Generates HMAC-SHA256 signed JWTs; validates secret length at startup |
| `LoginEndpoint.HandleLogin` | static method | `src/ClinicalHealthcare.Api/Features/Auth/LoginEndpoint.cs` | Validates credentials, manages lockout, issues JWT, writes Redis allowlist |
| `LogoutEndpoint.HandleLogout` | static method | `src/ClinicalHealthcare.Api/Features/Auth/LogoutEndpoint.cs` | Deletes jti from Redis allowlist |
| `ExtendSessionEndpoint.HandleExtendSession` | static method | `src/ClinicalHealthcare.Api/Features/Auth/ExtendSessionEndpoint.cs` | Issues new JWT, rotates Redis allowlist (DEL old, SET new) |
| `SessionTtlMiddleware` | middleware class | `src/ClinicalHealthcare.Api/Middleware/SessionTtlMiddleware.cs` | Checks jti exists in Redis; resets TTL; returns 401 if absent |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 [SOURCE:INPUT] | positive | JWT token expiry = 15 minutes from issuance | `JWT_SECRET` env var set to 32-byte test secret | `JwtTokenService.GenerateToken(1, "patient")` called | Parsed JWT `ValidTo` ≈ now + 15 min | `expiry` in range `[now+895s, now+905s]` — Basis: AC-001, `TokenExpirySeconds = 900` |
| TC-002 [SOURCE:INPUT] | positive | JWT contains required claims: sub, role, jti | `JWT_SECRET` env var set | `GenerateToken(42, "admin")` called | JWT parsed | `parsed.Subject == "42"`, claim `role=admin` present, `parsed.Id != null` — Basis: AC-001 |
| TC-003 [SOURCE:INPUT] | positive | Valid credentials return HTTP 200 with access token | Active UserAccount seeded; correct password | `HandleLogin` called with matching email/password | HTTP 200 | `IStatusCodeHttpResult.StatusCode == 200` — Basis: AC-001 |
| TC-004 [SOURCE:INPUT] | positive | 5 consecutive failed logins set LockoutEnd | Active UserAccount seeded | `HandleLogin` called 5× with wrong password | `LockoutEnd` set in future | `account.LockoutEnd > DateTimeOffset.UtcNow` — Basis: AC-006 |
| TC-005 [SOURCE:INPUT] | positive | SessionTtlMiddleware resets Redis TTL and passes request through | Moq Redis returns `KeyExists = true` | `SessionTtlMiddleware.InvokeAsync` called with authenticated context | Next delegate called; `KeyExpire` invoked | `nextCalled == true`; `KeyExpireAsync` verified on mock — Basis: AC-002 |
| TC-006 [SOURCE:INPUT] | negative | jti absent from Redis returns 401 | Moq Redis returns `KeyExists = false` | `SessionTtlMiddleware.InvokeAsync` called | HTTP 401 | `ctx.Response.StatusCode == 401` — Basis: AC-003 |
| TC-007 [SOURCE:INPUT] | positive | Logout deletes jti from Redis | Authenticated context with jti claim | `HandleLogout` called with Moq multiplexer | `KeyDelete` called on Redis with `jti:{jti}` key | `KeyDeleteAsync` verified on mock — Basis: AC-005 |
| TC-008 [SOURCE:INPUT] | positive | Extend session issues new JWT and rotates Redis allowlist | Authenticated context with jti + sub + role claims | `HandleExtendSession` called | HTTP 200 with new token; old jti deleted, new jti set | `StringSetAsync` verified for new jti; `KeyDeleteAsync` verified for old jti — Basis: AC-004 |
| EC-001 [SOURCE:INPUT] | edge_case | Redis unavailable at login falls back to signature-only; login returns 200 | `IConnectionMultiplexer.GetDatabase()` throws `RedisException` | `HandleLogin` called with valid credentials | HTTP 200 | Status 200; Redis exception swallowed — Basis: AC-006 fallback (Redis unavailable must not block login) |
| EC-002 [SOURCE:INPUT] | edge_case | Missing JWT_SECRET environment variable throws at constructor | `JWT_SECRET` env var is null | `new JwtTokenService()` called | `InvalidOperationException` thrown | `Assert.Throws<InvalidOperationException>(() => new JwtTokenService())` — Basis: fail-fast security requirement |
| ES-001 [SOURCE:INFERRED] | error | Authenticated context with no jti claim returns 401 | Authenticated user but no jti claim in `ClaimsPrincipal` | `SessionTtlMiddleware.InvokeAsync` called | HTTP 401 | `ctx.Response.StatusCode == 401` — Basis: jti is required to look up Redis allowlist |

## AI Component Test Cases
> Skipped — AI Impact = No (no AIR-XXX requirements in scope for this story).

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE (exists) | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/JwtSessionTests.cs` | JWT session management tests — already implemented |
| CREATE (mock) | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/JwtSessionTests.cs` | `NullMultiplexer()` helper and `BuildAuthenticatedContext()` helper in same file |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `IConnectionMultiplexer` | `Moq<IConnectionMultiplexer>` | Returns mock `IDatabase`; configurable per test | `Mock<IDatabase>.Object` |
| `IDatabase` (Redis) | `Moq<IDatabase>` | `StringSetAsync` returns `true`; `KeyExistsAsync` returns `true`/`false` per scenario; `KeyExpireAsync` returns `true` | Configurable `bool` |
| `ApplicationDbContext` | EF Core InMemory | Isolated per-test; `TransactionIgnoredWarning` suppressed | In-memory store |
| `IPasswordHasher<string>` | Real `PasswordHasher<string>` | Real PBKDF2 verify | `PasswordVerificationResult` |
| `IJwtTokenService` | Real `JwtTokenService` | Reads `JWT_SECRET` env var (set to test secret in each test) | Real JWT string |
| `ILoggerFactory` | `LoggerFactory.Create(_ => {})` | No-op logger | ILoggerFactory |

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Valid login | `email=test@example.com`, `password=ValidPass1!`, active account | HTTP 200 with access_token |
| Invalid password | Correct email, wrong password | HTTP 401 |
| Locked account | `LockoutEnd = UtcNow + 15min` | HTTP 423 |
| 5 failed logins | 5× wrong password | `LockoutEnd` set in future |
| Redis down at login | `GetDatabase()` throws | HTTP 200 (fallback) |
| JWT_SECRET null | Env var not set | `InvalidOperationException` at JwtTokenService() |
| jti absent from Redis | `KeyExistsAsync` returns false | HTTP 401 from middleware |

## Test Commands
- **Run Tests**: `dotnet test ClinicalHealthcare.slnx --filter "FullyQualifiedName~JwtSessionTests" -v q`
- **Run with Coverage**: `dotnet test ClinicalHealthcare.slnx --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~JwtSessionTests"`
- **Run Single Test**: `dotnet test ClinicalHealthcare.slnx --filter "FullyQualifiedName~JwtSessionTests.Login_ValidCredentials_Returns200"`

## Coverage Target
- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**: `SessionTtlMiddleware` Redis-present and Redis-absent branches; `LoginEndpoint` lockout counter and lockout-trigger branches; `JwtTokenService` constructor secret-length validation must have 100% coverage

## Documentation References
- **Framework Docs**: xUnit 2.x — https://xunit.net/docs; StackExchange.Redis Moq patterns
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/JwtSessionTests.cs`
- **Mocking Guide**: `Moq<IConnectionMultiplexer>` + `Moq<IDatabase>` — see JwtSessionTests helpers `NullMultiplexer()` and `BrokenMultiplexer()`

## Implementation Checklist
- [x] Create test file structure per Expected Changes (`JwtSessionTests.cs` exists)
- [x] Set up test data fixtures per Test Data section (`CreateActiveUser` helper; inline data)
- [x] Configure mocking dependencies per Mocking Strategy (Moq Redis, real JwtTokenService)
- [x] Implement positive test cases (TC-001 through TC-005, TC-007, TC-008)
- [x] Implement negative test cases (TC-006 — Redis absent → 401)
- [x] Implement edge case tests (EC-001, EC-002)
- [x] Implement error scenario tests (ES-001)
- [x] Run test suite and validate coverage meets target (101/101 tests green — 2026-05-24)
