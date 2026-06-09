# Implementation Analysis -- task_015_jwt-session-management-redis-allowlist.md

## Verdict

**Status:** Conditional Pass
**Summary:** All six acceptance criteria have functional implementations that build and pass the
existing 130-test suite. However, the review uncovered one critical deployment blocker (missing
EF Core migration for the two new `UserAccount` lockout columns), a total absence of task-specific
unit/integration tests, a pattern deviation where all three endpoints directly call
`IConnectionMultiplexer` rather than the project-standard `ICacheService` abstraction, and a
logging gap where Redis failures at login/extend/logout time are silently swallowed instead of
emitting the WARNING required by AC-006. Until the migration is generated and the test suite is
extended, this implementation is **not production-deployable**.

---

## Traceability Matrix

| Requirement / Acceptance Criterion | Evidence (file : line) | Result |
|---|---|---|
| AC-001 JWT TTL = 15 minutes | `JwtTokenService.cs:16` — `TokenExpirySeconds=900`; `L52` — `AddSeconds(900)`; `Program.cs:131` — `ClockSkew=TimeSpan.Zero` | Pass |
| AC-002 Redis allowlist: jti stored on login; TTL reset on every authenticated request | `LoginEndpoint.cs:109-117` — `StringSetAsync("session:{jti}", userId, 900s)`; `SessionTtlMiddleware.cs:78` — `KeyExpireAsync(key, 900s)` | Pass |
| AC-003 `POST /auth/extend-session` resets TTL + returns new token | `ExtendSessionEndpoint.cs:57-67` — new jti stored, old jti deleted | Pass |
| AC-004 `POST /auth/logout` removes allowlist entry | `LogoutEndpoint.cs:42-45` — `KeyDeleteAsync("session:{jti}")` | Pass |
| AC-005 5 consecutive failed logins → account lockout | `LoginEndpoint.cs:88-93` — `FailedLoginAttempts++`; if `>= 5` → `LockoutEnd=UtcNow+15min`; L76 checks `LockoutEnd > UtcNow` → 423 | Pass |
| AC-006 Redis unavailable → signature-only fallback; WARNING logged | `SessionTtlMiddleware.cs:85-91` — `RedisException` caught + `LogWarning`; but `LoginEndpoint.cs:113-117` and `ExtendSessionEndpoint.cs:69-72` silently swallow Redis errors | **Gap** |
| `JWT_SECRET` env var used; not hardcoded | `JwtTokenService.cs:22` — `GetEnvironmentVariable("JWT_SECRET")` | Pass |
| `ClockSkew = TimeSpan.Zero` enforcing exact TTL | `Program.cs:131` | Pass |
| `SessionTtlMiddleware` registered after `UseAuthentication` | `Program.cs:190` — after L188 `UseAuthentication()` | Pass |
| `SessionTtlMiddleware` registered before `UseAuthorization` | `Program.cs:190` — before L198 `UseAuthorization()` | Pass |
| Identity lockout configured in DI (task spec step 6) | `Program.cs` — no `AddIdentity`/`LockoutOptions` registration; manual field-based implementation used instead | **Gap** |
| EF Core migration for new `FailedLoginAttempts`/`LockoutEnd` columns | `ApplicationDbContextModelSnapshot.cs` — neither column present; no new migration file | **Fail** |

---

## Logical & Design Findings

### Business Logic

- **F5 — `PasswordVerificationResult.SuccessRehashNeeded` unhandled (MEDIUM)**
  `LoginEndpoint.cs:83` compares `verifyResult == PasswordVerificationResult.Failed` only.
  If the hasher upgrades its internal format (`SuccessRehashNeeded`), the account is correctly
  unlocked but the stored hash is never re-hashed to the new format, leaving the account
  permanently behind on the algorithm version.

- **F7 — No audit log entry on account lockout (LOW)**
  The rest of the codebase uses `AuditLogHelper.Stage()` for security-sensitive mutations.
  Account lockout is security-critical but generates no `AuditLog` record, breaking
  audit trail completeness.

- Lockout counter is reset to `0` at the moment of lockout (`LoginEndpoint.cs:92`), not at
  the moment of successful unlock. This means a user locked out and then logging in after
  expiry resumes from 0 — correct behaviour. Confirmed.

### Security

- **JWT_SECRET minimum-length guard** (`JwtTokenService.cs:27-30`) — positive: enforces ≥32
  bytes at startup. Not in task spec; added proactively.

- **F4 — AC-006 logging gap in Login / ExtendSession / Logout (MEDIUM)**
  `LoginEndpoint.cs:113-117` catches `RedisException` silently. AC-006 states "Redis unavailable
  → WARNING logged". Only `SessionTtlMiddleware` emits the required WARNING. All three endpoint
  Redis failure paths must also log at WARNING level to satisfy AC-006.

- **F6 — Redis key prefix deviates from spec (LOW)**
  Task spec's Implementation Validation Strategy states key pattern `jti:{jti}`.
  All files use `session:{jti}` (constant `LoginEndpoint.SessionKeyPrefix = "session:"`).
  Functionally consistent internally, but monitoring queries and Redis introspection tools built
  against the spec pattern will not match.

- `ValidateIssuer = false` and `ValidateAudience = false` in `Program.cs:128-129` — acceptable
  for a single-tenant internal API but should be documented as a conscious decision. No issuer
  or audience scope boundary is enforced.

### Error Handling

- **F4** (see Security above) — Silent `RedisException` swallowing in Login, ExtendSession,
  Logout violates AC-006's WARNING requirement.

- `SessionTtlMiddleware` returns `StatusCode(401)` but does not write a response body.
  Clients receive an empty 401 with no `WWW-Authenticate` header or JSON error payload,
  making debugging difficult. Low severity for initial delivery.

### Data Access

- **F1 — Missing EF Core migration (CRITICAL)**
  `UserAccount.FailedLoginAttempts` and `UserAccount.LockoutEnd` added to
  `src/ClinicalHealthcare.Infrastructure/Entities/UserAccount.cs:41-44` but no
  corresponding migration was generated. `ApplicationDbContextModelSnapshot.cs` does not
  contain these columns. Deploying to an existing SQL Server instance will cause runtime
  `SqlException: Invalid column name 'FailedLoginAttempts'` on the first login attempt.

- **F3 — Direct `IConnectionMultiplexer` use bypasses `ICacheService` abstraction (HIGH)**
  The project establishes `ICacheService` as the project-standard non-throwing Redis wrapper
  (TASK_001). All three endpoints (`LoginEndpoint`, `ExtendSessionEndpoint`, `LogoutEndpoint`)
  and `SessionTtlMiddleware` bypass this abstraction and call
  `IConnectionMultiplexer.GetDatabase()` directly. This:
  - Duplicates Redis error-handling logic (three separate `try/catch RedisException` blocks)
  - Does not benefit from the centralized serialisation/deserialisation helpers in `CacheService`
  - Produces an inconsistent pattern: `ICacheService` injected in some places, raw multiplexer
    in others
  The TTL reset in `SessionTtlMiddleware` also uses a two-operation `KeyExists` + `KeyExpire`
  sequence. These are not atomic — a key could expire between the two calls, leading the
  middleware to wrongly reset a TTL on a key that had just been deleted.

- **Non-atomic two-operation check + expire (MEDIUM)**
  `SessionTtlMiddleware.cs:72-78`: `KeyExistsAsync` followed by `KeyExpireAsync` are two
  separate Redis commands with no transaction or Lua script. Under high concurrency or just
  after the key's natural TTL expiry, the key can vanish between the two calls. Use
  `IDatabase.StringGetSetExpiryAsync` (Redis 6.2+) or a Lua script for atomicity.

### Performance

- No concerns at current scale. `SessionTtlMiddleware` adds one Redis round-trip per
  authenticated request, which is the intended design for allowlist enforcement.

### Patterns & Standards

- **F2 — Zero task-specific tests (HIGH)** — see Test Review below.

- `ExtendSessionEndpoint` references `LoginEndpoint.SessionKeyPrefix` constant directly,
  creating cross-endpoint coupling. The constant should be extracted to a shared
  `AuthConstants` static class. Low severity.

---

## Test Review

### Existing Tests

- `ClinicalHealthcare.Api.Tests` — 10 tests (passes: 10). Includes convention test and
  `RbacViolationHandlerTests`. No TASK_015 tests.
- `ClinicalHealthcare.Infrastructure.Tests` — 120 tests (passes: 120). No TASK_015 tests.

**Total TASK_015-specific tests: 0 of an estimated 20+ required.**

### Missing Tests (must add)

- [ ] **Unit** — `JwtTokenService_GenerateToken_ReturnsDifferentJtiOnEachCall`
- [ ] **Unit** — `JwtTokenService_Constructor_ThrowsWhenJwtSecretMissing`
- [ ] **Unit** — `JwtTokenService_Constructor_ThrowsWhenSecretTooShort`
- [ ] **Unit** — `JwtTokenService_GenerateToken_TokenExpiresIn15Minutes`
- [ ] **Integration** — `Login_ValidCredentials_Returns200WithBearer`
- [ ] **Integration** — `Login_InvalidPassword_Returns401`
- [ ] **Integration** — `Login_InactiveAccount_Returns401`
- [ ] **Integration** — `Login_FifthFailedAttempt_SetsLockout` (AC-005)
- [ ] **Integration** — `Login_LockedAccount_Returns423` (AC-005)
- [ ] **Integration** — `Login_MissingEmailOrPassword_Returns422`
- [ ] **Unit (middleware)** — `SessionTtlMiddleware_UnauthenticatedRequest_PassesThrough`
- [ ] **Unit (middleware)** — `SessionTtlMiddleware_ValidJti_ResetsTtlAndContinues` (AC-002)
- [ ] **Unit (middleware)** — `SessionTtlMiddleware_MissingJti_Returns401`
- [ ] **Unit (middleware)** — `SessionTtlMiddleware_KeyNotInRedis_Returns401`
- [ ] **Unit (middleware)** — `SessionTtlMiddleware_RedisDown_PassesThroughWithWarning` (AC-006)
- [ ] **Integration** — `ExtendSession_ValidToken_ReturnsNewToken` (AC-003)
- [ ] **Integration** — `ExtendSession_RotatesRedisJti` (AC-003)
- [ ] **Integration** — `Logout_ValidToken_DeletesRedisKey` (AC-004)
- [ ] **Integration** — `Logout_AfterLogout_TokenInvalid` (AC-004)
- [ ] **Negative** — `Login_EmptyEmail_Returns422`

---

## Validation Results

**Commands Executed:**

```
dotnet build
dotnet test --no-build --logger "console;verbosity=minimal"
```

**Outcomes:**

```
Build: succeeded — 0 errors, 0 warnings
Tests: total=130, failed=0, passed=130, skipped=0
```

All pre-existing tests pass. No TASK_015 tests exist in the suite.

---

## Fix Plan (Prioritized)

1. **Generate EF Core migration for lockout columns** — `UserAccount.cs` (entity already
   updated); run `dotnet ef migrations add AddUserAccountLockout --project
   src/ClinicalHealthcare.Infrastructure.SqlMigrations --startup-project
   src/ClinicalHealthcare.Api` — **ETA 0.5h** — Risk: **H** (production-blocking)

2. **Add WARNING logs to Login/ExtendSession/Logout Redis catch blocks** —
   `LoginEndpoint.cs:113`, `ExtendSessionEndpoint.cs:68`, `LogoutEndpoint.cs:40` —
   Inject `ILogger<T>` or `ILoggerFactory`; call `LogWarning` in each catch — **ETA 1h** —
   Risk: **M** (AC-006 compliance)

3. **Add TASK_015 unit + integration tests (20+ test cases)** —
   `tests/ClinicalHealthcare.Api.Tests/Features/Auth/` (new directory) —
   **ETA 4h** — Risk: **M** (quality gate; no coverage without this)

4. **Replace direct `IConnectionMultiplexer` calls with `ICacheService`** —
   `LoginEndpoint.cs`, `ExtendSessionEndpoint.cs`, `LogoutEndpoint.cs`, `SessionTtlMiddleware.cs`
   — Refactor to use `ICacheService.SetAsync`, `GetAsync`, `DeleteAsync`; resolve atomic
   check+expire issue in middleware using `ICacheService` or a Lua script extension —
   **ETA 2h** — Risk: **M** (pattern consistency; atomic correctness)

5. **Handle `PasswordVerificationResult.SuccessRehashNeeded`** — `LoginEndpoint.cs:83` —
   Add `|| verifyResult == PasswordVerificationResult.SuccessRehashNeeded` branch that
   re-saves updated hash before issuing token — **ETA 0.5h** — Risk: **L**

6. **Extract `SessionKeyPrefix` to shared `AuthConstants`** —
   `src/ClinicalHealthcare.Api/Auth/AuthConstants.cs` (new) — Remove coupling between
   `ExtendSessionEndpoint` and `LoginEndpoint` — **ETA 0.25h** — Risk: **L**

7. **Add audit log entry on account lockout** — `LoginEndpoint.cs:90-93` —
   Call `AuditLogHelper.Stage(db, "UserAccount", account.Id, null, "LOCKOUT", ...)` —
   **ETA 0.5h** — Risk: **L**

---

## Appendix

### Rules Applied

- `rules/security-standards-owasp.md` — OWASP A02 (Cryptographic failures: JWT secret handling,
  key length enforcement), A07 (Identification and Authentication Failures: lockout, token
  validation)
- `rules/code-anti-patterns.md` — Duplicate Redis error-handling logic; cross-endpoint constant
  coupling
- `rules/dry-principle-guidelines.md` — `IConnectionMultiplexer` repeated three times instead of
  delegating to `ICacheService`
- `rules/backend-development-standards.md` — Pattern adherence; transaction/atomic operation
  analysis
- `rules/language-agnostic-standards.md` — Missing abstraction boundary; naming consistency
- `rules/dotnet-architecture-standards.md` — Infrastructure abstraction layer (`ICacheService`)
  bypassed at API layer

### Search Evidence

```
grep "SessionTtlMiddleware" Program.cs                     -> line 190
grep "FailedLoginAttempts" SqlMigrations/**/*.cs            -> 0 matches (migration missing)
grep "FailedLoginAttempts" Infrastructure/Entities/*.cs     -> UserAccount.cs:41
file_search tests/**/*LoginEndpoint*.cs                     -> 0 files
file_search tests/**/*SessionTtlMiddleware*.cs              -> 0 files
grep "IConnectionMultiplexer" LoginEndpoint.cs              -> line 59 (direct use)
grep "session:" LoginEndpoint.cs                            -> SessionKeyPrefix = "session:"
```
