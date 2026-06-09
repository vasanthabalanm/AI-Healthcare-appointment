# Task - TASK_004

## Requirement Reference

- **User Story**: US_004 — Upstash Redis + Hangfire infrastructure
- **Story Location**: `.propel/context/tasks/EP-TECH/us_004/us_004.md`
- **Parent Epic**: EP-TECH

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | Redis health check registered; `/health` reflects Redis connectivity |
| AC-002 | Hangfire dashboard accessible at `/hangfire` for Admin role only |
| AC-003 | Hangfire job retry policy: 3 attempts with exponential backoff (30s, 60s, 120s) |
| AC-004 | `ICacheService` cache-aside helper encapsulates Redis get/set/delete |
| AC-005 | Redis unavailability causes cache miss (fall-through to DB); no exception propagated to callers |

### Edge Cases

- Redis connection fails → `ICacheService` catches `RedisException`, logs WARNING, returns null (cache miss); DB query proceeds
- Hangfire dashboard must return 403 for non-Admin JWT; anonymous access forbidden

---

## Design References

N/A — UI Impact: No

---

## AI References

N/A — AI Impact: No

---

## Mobile References

N/A — Mobile Impact: No

---

## Applicable Technology Stack

| Layer | Technology | Version | Justification |
|-------|-----------|---------|---------------|
| Infrastructure | Upstash Redis | N/A (OSS Redis-compatible) | Session/slot/360° cache per design.md |
| Backend | StackExchange.Redis | 2.x | .NET Redis client |
| Backend | Hangfire | 1.8.x | Background job queue per design.md |
| Backend | Hangfire.SqlServer | 1.8.x | SQL Server-backed Hangfire storage |
| Backend | ASP.NET Core Health Checks | 8.x | Redis health check integration |

---

## Task Overview

Configure Upstash Redis (via StackExchange.Redis) and Hangfire (SQL Server-backed) in the .NET 8 Web API. Implement a `CacheService` with cache-aside pattern and Redis fail-through. Restrict the Hangfire dashboard to Admin JWT. Register Redis health check in the existing health check pipeline.

---

## Dependent Tasks

- **TASK_001 (us_002)** — Web API scaffold (health check pipeline, DI setup)
- **TASK_001 (us_003)** — SQL Server `ApplicationDbContext` (Hangfire SQL Server storage)

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Program.cs` — Redis + Hangfire registration
- `src/ClinicalHealthcare.Infrastructure/Cache/CacheService.cs` — cache-aside implementation
- `src/ClinicalHealthcare.Infrastructure/Cache/ICacheService.cs` — abstraction interface
- `src/ClinicalHealthcare.Infrastructure/Cache/CacheSettings.cs` — TTL constants

---

## Implementation Plan

1. Add `StackExchange.Redis` NuGet; register `IConnectionMultiplexer` from env var `REDIS_CONNECTION_STRING`.
2. Add Redis health check via `AddRedis()` extension.
3. Create `CacheSettings` with `SessionTtlSeconds=900`, `SlotTtlSeconds=60`, `View360TtlSeconds=300`.
4. Implement `ICacheService` / `CacheService` with `GetAsync<T>`, `SetAsync<T>`, `DeleteAsync`; wrap all Redis calls in try/catch; return null on `RedisException`.
5. Add `Hangfire` + `Hangfire.SqlServer` NuGet; configure storage from `SQLSERVER_CONNECTION_STRING`.
6. Configure global retry filter: `new AutomaticRetryAttribute { Attempts = 3, DelaysInSeconds = new[] {30, 60, 120} }`.
7. Mount Hangfire dashboard at `/hangfire`; add authorization filter that validates `role=Admin` from JWT.
8. Register `ICacheService` in DI; confirm `ValidateOnBuild` passes.

---

## Current Project State

```
src/
├── ClinicalHealthcare.Api/
│   └── Program.cs
└── ClinicalHealthcare.Infrastructure/
    └── Data/
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Infrastructure/Cache/ICacheService.cs` | Cache abstraction |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Cache/CacheService.cs` | Redis cache-aside with fail-through |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Cache/CacheSettings.cs` | TTL constants (900/60/300) |
| CREATE | `src/ClinicalHealthcare.Api/Infrastructure/HangfireDashboardAuthFilter.cs` | Admin-only dashboard authorization |
| MODIFY | `src/ClinicalHealthcare.Api/Program.cs` | Register Redis, Hangfire, `ICacheService`; mount dashboard |

---

## External References

- [Hangfire Documentation](https://docs.hangfire.io/en/latest/)
- [StackExchange.Redis](https://stackexchange.github.io/StackExchange.Redis/)
- [ASP.NET Core Health Checks — Redis](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks#redis-probe)

---

## Build Commands

```bash
dotnet add src/ClinicalHealthcare.Infrastructure package StackExchange.Redis
dotnet add src/ClinicalHealthcare.Infrastructure package Hangfire
dotnet add src/ClinicalHealthcare.Infrastructure package Hangfire.SqlServer
dotnet build
```

---

## Implementation Validation Strategy

- `dotnet build` → 0 errors.
- `GET /health` with Redis running → `{"status":"Healthy"}`.
- `GET /health` with Redis stopped → `{"status":"Degraded"}` (health check reports degraded, app still runs).
- `GET /hangfire` with Admin JWT → dashboard loads.
- `GET /hangfire` with Staff JWT → 403.
- Unit test: `CacheService.GetAsync` returns null when `IConnectionMultiplexer` throws `RedisException`.

---

## Implementation Checklist

- [ ] **[AC-001]** Redis health check registered and reflected in `/health` endpoint
- [ ] **[AC-002]** Hangfire dashboard at `/hangfire` behind Admin-role JWT authorization filter
- [ ] **[AC-003]** Global `AutomaticRetryAttribute` with 3 attempts and `{30,60,120}` second delays
- [ ] **[AC-004]** `ICacheService` / `CacheService` with `GetAsync`, `SetAsync`, `DeleteAsync`
- [ ] **[AC-005]** `RedisException` caught in `CacheService`; returns null (cache miss); WARNING logged
- [ ] `CacheSettings` constants: `SessionTtlSeconds=900`, `SlotTtlSeconds=60`, `View360TtlSeconds=300`
- [ ] `REDIS_CONNECTION_STRING` env var read at startup; missing → startup failure
- [ ] `dotnet build` passes with 0 errors
