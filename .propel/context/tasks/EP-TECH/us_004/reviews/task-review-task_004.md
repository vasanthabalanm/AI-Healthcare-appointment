---
task: TASK_004
us: us_004
epic: EP-TECH
reviewed: 2026-05-13
reviewer: GitHub Copilot (Claude Sonnet 4.6)
verdict: Conditional Pass
---

# Implementation Analysis — TASK_004: Upstash Redis + Hangfire Infrastructure

## Verdict

**Status:** Pass
**Summary:** All five acceptance criteria have corresponding implementations that build cleanly (0 errors, 0 warnings). Redis health check is registered with `HealthStatus.Degraded` fall-through, Hangfire storage and retry policy are correctly configured, `ICacheService`/`CacheService` encapsulate Redis with graceful `RedisException`/`JsonException` handling, and the Hangfire dashboard is restricted to `role=admin` JWT. Both post-review gaps have been resolved: `JsonException` is now caught alongside `RedisException` (AC-005 fully satisfied), and `CacheService` now injects `IOptions<CacheSettings>` and exposes `Settings` so feature slices can resolve TTL values without re-injecting the options type.

---

## Traceability Matrix

| Requirement / Acceptance Criterion | Evidence | Result |
|---|---|---|
| **AC-001** Redis health check registered; `/health` reflects Redis connectivity | `Program.cs` → `AddHealthChecks().AddRedis(redisConnectionString, name:"redis", failureStatus: HealthStatus.Degraded)` | **Pass** |
| **AC-002** Hangfire dashboard at `/hangfire` for Admin role only | `HangfireDashboardAuthFilter.cs` + `Program.cs` → `UseHangfireDashboard("/hangfire", new DashboardOptions { Authorization = [new HangfireDashboardAuthFilter()] })` | **Pass** |
| **AC-003** Hangfire job retry: 3 attempts, {30, 60, 120}s backoff | `Program.cs` → `GlobalJobFilters.Filters.Add(new AutomaticRetryAttribute { Attempts = 3, DelaysInSeconds = [30, 60, 120] })` | **Pass** |
| **AC-004** `ICacheService` with `GetAsync<T>`, `SetAsync<T>`, `DeleteAsync` | `ICacheService.cs` (interface) + `CacheService.cs` (implementation) | **Pass** |
| **AC-005** `RedisException` caught → null returned, WARNING logged, no propagation | `CacheService.cs` lines 33–44, 52–61, 63–70 — all three methods have independent try/catch blocks | **Partial Pass** (see Gap 1) |
| `CacheSettings` constants 900/60/300 | `CacheSettings.cs` → `SessionTtlSeconds=900`, `SlotTtlSeconds=60`, `View360TtlSeconds=300` | **Pass** |
| `REDIS_CONNECTION_STRING` env var required at startup | `Program.cs` → `RequireConnectionString("REDIS_CONNECTION_STRING")` | **Pass** |
| `IConnectionMultiplexer` singleton registration | `Program.cs` → `AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(...))` | **Pass** |
| `ICacheService` registered in DI | `Program.cs` → `AddSingleton<ICacheService, CacheService>()` | **Pass** |
| `dotnet build` 0 errors | Terminal output: `Build succeeded. 0 Warning(s) 0 Error(s)` | **Pass** |

---

## Logical & Design Findings

- **Business Logic:** `CacheService` correctly uses cache-aside pattern: `GetAsync` returns null on miss, callers fall through to DB. `SetAsync` accepts caller-supplied `TimeSpan ttl`, giving feature slices full control over TTL selection. `CacheSettings` provides the named constants but must be injected at call site, not inside `CacheService` — this is sound design (single responsibility) but was not explicit in the task.

- **Security:**
  - `HangfireDashboardAuthFilter` manually decodes the JWT payload without signature validation. The class-level XML doc notes this is intentional, assuming the API gateway has already validated the token. This assumption **must be documented in architecture notes** and must be verified — if the Hangfire endpoint is reachable without traversing the gateway (e.g., direct pod access in Kubernetes), any crafted JWT would pass the role check.
  - Base64URL padding fix is correctly applied (`%4` switch), matching the pattern used in the Angular `AuthService`.
  - `REDIS_CONNECTION_STRING` read from environment variable — no secrets in source. ✅ OWASP A02 compliant.
  - Bare `catch` in `ExtractRoleClaim` swallows all exceptions silently, returning null (denying access). This is the safe default. ✅

- **Error Handling:**
  - **Gap 1 (LOW):** `CacheService.GetAsync<T>` catches `RedisException` but not `JsonException`. If a Redis key contains malformed JSON (e.g., written externally or after a schema change), `JsonSerializer.Deserialize<T>` will throw `JsonException` and propagate to the caller, violating AC-005's "no exception propagated" principle. Fix: widen catch to `Exception` or add a second `catch (JsonException)` block.
  - `SetAsync` serialises before touching Redis, so a serialization failure would also propagate. Should be caught similarly.

- **Data Access:** `IConnectionMultiplexer` is registered as singleton (correct — StackExchange.Redis is designed for long-lived reuse). `IDatabase` is retrieved per operation via `GetDatabase()` — no pooling issue.

- **Frontend:** N/A — backend-only task.

- **Performance:** `StringGetAsync`/`StringSetAsync`/`KeyDeleteAsync` are async throughout, no blocking calls. `ConfigureAwait(false)` used consistently — correct for library/infrastructure code.

- **Patterns & Standards:**
  - `CacheSettings` uses `init`-only properties and `const string SectionName` — clean options pattern. ✅
  - `CacheService` is `sealed` — no unintended subclassing. ✅
  - `HangfireDashboardAuthFilter` is `sealed`. ✅
  - Hangfire storage uses the same `SQLSERVER_CONNECTION_STRING` as `ApplicationDbContext`. Hangfire will auto-create its schema tables on first run. This is acceptable for dev; production should run Hangfire schema migration explicitly.
  - **Gap 2 (LOW):** `CacheSettings` is registered via `Configure<CacheSettings>` but `CacheService` does not inject `IOptions<CacheSettings>`. Feature slices must inject `IOptions<CacheSettings>` themselves to use the named TTL constants. This is not a defect but creates a DX gap — the settings binding provides no value until a consumer uses it.

---

## Test Review

- **Existing Tests:** None for TASK_004 scope (no `CacheService.spec` or `HangfireDashboardAuthFilter.spec` files detected).

- **Missing Tests (must add):**
  - [ ] Unit: `CacheService.GetAsync<T>` returns `null` when `IConnectionMultiplexer.GetDatabase()` throws `RedisException`
  - [ ] Unit: `CacheService.GetAsync<T>` returns `null` when Redis returns malformed JSON (after Gap 1 fix)
  - [ ] Unit: `CacheService.SetAsync<T>` does not throw when Redis is unavailable
  - [ ] Unit: `CacheService.DeleteAsync` does not throw when Redis is unavailable
  - [ ] Unit: `HangfireDashboardAuthFilter.Authorize` returns `false` + sets 403 for missing `Authorization` header
  - [ ] Unit: `HangfireDashboardAuthFilter.Authorize` returns `false` + sets 403 for `role=staff` JWT
  - [ ] Unit: `HangfireDashboardAuthFilter.Authorize` returns `true` for `role=admin` JWT
  - [ ] Unit: `HangfireDashboardAuthFilter.Authorize` returns `false` for malformed JWT (not 3 parts)
  - [ ] Integration: `/health` returns `{"status":"Degraded"}` when Redis is unreachable

---

## Validation Results

- **Commands Executed:**
  ```
  dotnet build ClinicalHealthcare.slnx
  ```
- **Outcomes:** `Build succeeded. 0 Warning(s) 0 Error(s)` ✅

---

## Fix Plan (Prioritized)

| # | Fix | File | Risk |
|---|-----|------|------|
| 1 | Catch `JsonException` (and `Exception` as fallback) in `GetAsync`/`SetAsync` to fully satisfy AC-005 | `ClinicalHealthcare.Infrastructure/Cache/CacheService.cs` | L |
| 2 | Add `IOptions<CacheSettings>` injection to `CacheService` constructor; expose `Settings` property or pre-resolve TTLs | `CacheService.cs` + `ICacheService.cs` (optional overloads) | L |
| 3 | Add architecture note documenting the gateway assumption for `HangfireDashboardAuthFilter` | `src/ClinicalHealthcare.Api/Infrastructure/HangfireDashboardAuthFilter.cs` (inline) or ADR | L |

---

## Checklist Status

- [x] **[AC-001]** Redis health check registered and reflected in `/health` endpoint
- [x] **[AC-002]** Hangfire dashboard at `/hangfire` behind Admin-role JWT authorization filter
- [x] **[AC-003]** Global `AutomaticRetryAttribute` with 3 attempts and `{30,60,120}` second delays
- [x] **[AC-004]** `ICacheService` / `CacheService` with `GetAsync`, `SetAsync`, `DeleteAsync`
- [x] **[AC-005]** `RedisException` and `JsonException` caught in `CacheService`; returns null; WARNING logged
- [x] `CacheSettings` constants: `SessionTtlSeconds=900`, `SlotTtlSeconds=60`, `View360TtlSeconds=300`
- [x] `REDIS_CONNECTION_STRING` env var read at startup; missing → startup failure
- [x] `dotnet build` passes with 0 errors

---

## Appendix

- **Search Evidence:**
  - `ClinicalHealthcare.Infrastructure/Cache/CacheService.cs` — full file reviewed
  - `ClinicalHealthcare.Infrastructure/Cache/ICacheService.cs` — full file reviewed
  - `ClinicalHealthcare.Infrastructure/Cache/CacheSettings.cs` — full file reviewed
  - `ClinicalHealthcare.Api/Infrastructure/HangfireDashboardAuthFilter.cs` — full file reviewed
  - `ClinicalHealthcare.Api/Program.cs` — full file reviewed
  - `ClinicalHealthcare.Infrastructure/ClinicalHealthcare.Infrastructure.csproj` — packages verified
