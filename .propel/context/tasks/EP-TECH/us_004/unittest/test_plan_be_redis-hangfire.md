# Unit Test Plan - TASK_004

## Requirement Reference

- **User Story**: US_004 — Upstash Redis and Hangfire background job infrastructure
- **Story Location**: `.propel/context/tasks/EP-TECH/us_004/us_004.md`
- **Layer**: BE
- **Related Test Plans**: `../us_002/unittest/test_plan_be_webapi-scaffold.md`, `../us_003/unittest/test_plan_be_ef-dual-dbcontext.md`
- **Acceptance Criteria Covered**:
  - AC-001: Redis health check registered and reflected in `/health`
  - AC-002: Hangfire dashboard accessible at `/hangfire` for Admin JWT only (403 for others)
  - AC-003: Global `AutomaticRetryAttribute` — 3 retries at 30 s / 60 s / 120 s intervals
  - AC-004: `ICacheService` cache-aside — Redis GET before DB; SET on miss with TTL
  - AC-005: `RedisConnectionException` caught → null returned; DB query proceeds; no crash

---

## Test Plan Overview

Validates the Redis cache-aside service and Hangfire dashboard authorization in isolation.
`CacheService` tests mock `IConnectionMultiplexer` / `IDatabase` — no live Redis connection
is required. `HangfireDashboardAuthFilter` tests supply crafted JWT-backed
`IDashboardContext` mocks. Retry policy configuration is verified by inspecting the
registered global Hangfire filters, not by waiting for real job retries.

---

## Dependent Tasks

- TASK_002 (US_002) — Web API scaffold (DI + health check pipeline)
- TASK_003 (US_003) — `ApplicationDbContext` connection string (Hangfire SQL Server store)

---

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `CacheService` | service class | `src/ClinicalHealthcare.Infrastructure/Cache/CacheService.cs` | Cache-aside: `GetAsync`, `SetAsync`, `DeleteAsync` |
| `ICacheService` | interface | `src/ClinicalHealthcare.Infrastructure/Cache/ICacheService.cs` | Cache abstraction |
| `CacheSettings` | configuration class | `src/ClinicalHealthcare.Infrastructure/Cache/CacheSettings.cs` | TTL constants (900 / 60 / 300 s) |
| `HangfireDashboardAuthFilter` | auth filter | `src/ClinicalHealthcare.Api/Infrastructure/HangfireDashboardAuthFilter.cs` | Admin-only Hangfire dashboard |

---

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 [SOURCE:INPUT] | positive | `GetAsync` returns deserialized value on Redis cache hit | Mock `IDatabase.StringGetAsync(key)` returns JSON-serialized object | `CacheService.GetAsync<T>(key)` called | Deserialized object returned; no exception | Return value equals original object; `StringGetAsync` called exactly once; no downstream DB call triggered |
| TC-002 [SOURCE:INPUT] | positive | `GetAsync` returns null on Redis cache miss | Mock `IDatabase.StringGetAsync(key)` returns `RedisValue.Null` | `CacheService.GetAsync<T>(key)` called | `null` returned; caller may proceed to DB | Return value is `null`; `StringGetAsync` called exactly once |
| TC-003 [SOURCE:INPUT] | positive | `SetAsync` stores JSON-serialized value with correct TTL | Mock `IDatabase.StringSetAsync` captures arguments | `CacheService.SetAsync<T>(key, value, ttl)` called | Value stored in Redis with specified TTL | `StringSetAsync` called with `key`, serialized JSON, and `TimeSpan` matching `ttl` |
| TC-004 [SOURCE:INPUT] | positive | `HangfireDashboardAuthFilter.Authorize` returns `true` for Admin JWT | Mock `IDashboardContext.GetHttpContext().User` has claim `role=Admin` | `Authorize(context)` called | Returns `true` | `Authorize` returns `true`; no exception |
| TC-005 [SOURCE:INPUT] | negative | `HangfireDashboardAuthFilter.Authorize` returns `false` for Staff JWT | Mock `IDashboardContext.GetHttpContext().User` has claim `role=staff` | `Authorize(context)` called | Returns `false` | `Authorize` returns `false` |
| EC-001 [SOURCE:INPUT] | edge_case | `GetAsync` catches `RedisConnectionException` and returns null | Mock `IDatabase.StringGetAsync` throws `RedisConnectionException` | `CacheService.GetAsync<T>(key)` called | `null` returned; no exception propagated to caller; warning logged | Return value is `null`; logger received `Warning` level call; no exception escapes |
| EC-002 [SOURCE:INPUT] | edge_case | `SetAsync` catches `RedisConnectionException` and logs warning | Mock `IDatabase.StringSetAsync` throws `RedisConnectionException` | `CacheService.SetAsync<T>(key, value, ttl)` called | No exception propagated; warning logged | No exception thrown; logger received `Warning` level call |
| ES-001 [SOURCE:INFERRED] | error | `DeleteAsync` on non-existent key does not throw | Mock `IDatabase.KeyDeleteAsync(key)` returns `false` (key not found) | `CacheService.DeleteAsync(key)` called | Method completes without exception | No exception thrown; `KeyDeleteAsync` called with correct key | Basis: callers must not handle `DeleteAsync` exceptions for cache-eviction clean-up paths |

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `tests/ClinicalHealthcare.Infrastructure.Tests/Cache/CacheServiceTests.cs` | TC-001–TC-003, EC-001, EC-002, ES-001 |
| CREATE | `tests/ClinicalHealthcare.Api.Tests/Infrastructure/HangfireDashboardAuthFilterTests.cs` | TC-004, TC-005 |
| CREATE | `tests/ClinicalHealthcare.Infrastructure.Tests/Cache/CacheServiceTestFixture.cs` | Shared mock `IConnectionMultiplexer` + `IDatabase` setup |

---

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `IConnectionMultiplexer` | `Mock<IConnectionMultiplexer>` (Moq) | Returns mock `IDatabase` from `.GetDatabase()` | Mock `IDatabase` |
| `IDatabase` | `Mock<IDatabase>` (Moq) | `StringGetAsync`, `StringSetAsync`, `KeyDeleteAsync` configured per test | Configured per test case |
| `ILogger<CacheService>` | `Mock<ILogger<CacheService>>` (Moq) | Captures log calls; verified in EC-001/EC-002 | Logger spy |
| `IDashboardContext` | `Mock<IDashboardContext>` (Moq) | Returns `DefaultHttpContext` with mocked `ClaimsPrincipal` | Admin or Staff `ClaimsPrincipal` |
| `ClaimsPrincipal` | constructed directly | `new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("role", "Admin") }))` | Admin or Staff claim set |

---

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Cache hit | Key `"patient:1:profile"`, Redis returns `{"id":1,"name":"Test"}` | Deserialized object `{ id: 1, name: "Test" }` |
| Cache miss | Key `"patient:1:profile"`, Redis returns `null` | `null` (caller proceeds to DB) |
| Redis exception | `StringGetAsync` throws `RedisConnectionException` | `null`; warning log emitted |
| Admin JWT | Claim `role=Admin` | `Authorize` returns `true` |
| Staff JWT | Claim `role=staff` | `Authorize` returns `false` |
| Anonymous | No claims | `Authorize` returns `false` |

---

## Test Commands

- **Run Tests**: `dotnet test tests/ --filter "Category=Cache|Category=Hangfire" --no-build`
- **Run with Coverage**: `dotnet test tests/ --collect:"XPlat Code Coverage" --results-directory ./coverage`
- **Run Single Test**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests/ --filter "FullyQualifiedName~CacheServiceTests"`

---

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 90%
- **Critical Paths**: `CacheService.GetAsync` exception branch; `CacheService.SetAsync` exception branch; `HangfireDashboardAuthFilter.Authorize` role check

---

## Documentation References

- **StackExchange.Redis Testing**: <https://stackexchange.github.io/StackExchange.Redis/>
- **Hangfire Authorization**: <https://docs.hangfire.io/en/latest/configuration/using-dashboard.html#configuring-authorization>
- **Moq Quickstart**: <https://github.com/devlooped/moq/wiki/Quickstart>

---

## Implementation Checklist

- [x] Create `CacheServiceTestFixture` with mock `IConnectionMultiplexer` and `IDatabase`
- [x] Implement TC-001/TC-002/TC-003 — `GetAsync` hit/miss/set with TTL assertions
- [x] Implement EC-001/EC-002 — `RedisConnectionException` fail-through and warning log
- [x] Implement ES-001 — `DeleteAsync` idempotence on missing key
- [x] Implement TC-004/TC-005 — `HangfireDashboardAuthFilter` Admin vs Staff JWT assertions
- [x] Run test suite; validate all 8 test cases pass
- [x] Verify branch coverage ≥ 90% on `CacheService.cs` and `HangfireDashboardAuthFilter.cs`
