# Task - TASK_015

## Requirement Reference

- **User Story**: US_015 — JWT session management + 15-min timeout
- **Story Location**: `.propel/context/tasks/EP-001/us_015/us_015.md`
- **Parent Epic**: EP-001

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | JWT access token TTL is 15 minutes |
| AC-002 | Valid token added to Redis allowlist on login; TTL reset on every authenticated request |
| AC-003 | `POST /auth/extend-session` resets Redis TTL to 15 minutes and returns a new token |
| AC-004 | `POST /auth/logout` removes token from Redis allowlist |
| AC-005 | 5 consecutive failed login attempts trigger account lockout |
| AC-006 | Redis unavailable → fall through to JWT signature-only validation (no allowlist check) |

### Edge Cases

- Token not in Redis allowlist (expired or logged out) → 401
- Redis unavailable → WARNING logged; JWT signature validation used as fallback; allowlist not enforced

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
| Backend | ASP.NET Core JWT Bearer | 8.x | Token validation per design.md |
| Backend | ASP.NET Core Identity | .NET 8 | Lockout management |
| Infrastructure | Upstash Redis | N/A | Token allowlist; TTL=900s (15 min) |
| Backend | StackExchange.Redis | 2.x | Redis client |

---

## Task Overview

Implement JWT login (`POST /auth/login`) with 15-minute TTL. Maintain a Redis allowlist keyed by JWT JTI. Reset TTL on every authenticated request via middleware. Add `POST /auth/extend-session` and `POST /auth/logout`. Enforce 5-attempt lockout via ASP.NET Core Identity. Implement Redis-unavailable fallback.

---

## Dependent Tasks

- **TASK_001 (us_004)** — Redis + `ICacheService`
- **TASK_001 (us_007)** — `UserAccount` entity

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Auth/LoginEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Auth/ExtendSessionEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Auth/LogoutEndpoint.cs`
- `src/ClinicalHealthcare.Api/Middleware/SessionTtlMiddleware.cs`
- `src/ClinicalHealthcare.Infrastructure/Auth/JwtTokenService.cs`

---

## Implementation Plan

1. Create `JwtTokenService`: generate JWT with 15-min expiry; include `jti` (GUID), `sub`, `role` claims; sign with `JWT_SECRET` env var.
2. Implement `POST /auth/login` (`[AllowAnonymous]`): validate credentials; check `IsActive`; track failed attempts via Identity lockout; on success, generate JWT, store `jti→userId` in Redis with TTL=900s; return token.
3. Create `SessionTtlMiddleware`: on every authenticated request, read `jti` from token; check Redis for key; if missing → 401; if Redis down → skip allowlist check + log WARNING; if found → reset TTL to 900s.
4. Implement `POST /auth/extend-session`: validate current JWT; generate new JWT; update Redis key with new jti; return new token.
5. Implement `POST /auth/logout`: read jti from token; delete Redis key; return 200.
6. Configure Identity lockout: `MaxFailedAccessAttempts = 5`, `LockoutDuration = TimeSpan.FromMinutes(15)`.
7. Register `SessionTtlMiddleware` in pipeline after `UseAuthentication()`.

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Auth/
├── RegisterEndpoint.cs
└── VerifyEmailEndpoint.cs
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/Auth/LoginEndpoint.cs` | POST /auth/login |
| CREATE | `src/ClinicalHealthcare.Api/Features/Auth/ExtendSessionEndpoint.cs` | POST /auth/extend-session |
| CREATE | `src/ClinicalHealthcare.Api/Features/Auth/LogoutEndpoint.cs` | POST /auth/logout |
| CREATE | `src/ClinicalHealthcare.Api/Middleware/SessionTtlMiddleware.cs` | TTL reset + allowlist check |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Auth/JwtTokenService.cs` | JWT generation + jti management |
| MODIFY | `src/ClinicalHealthcare.Api/Program.cs` | Register JWT bearer auth; register middleware; configure Identity lockout |

---

## External References

- [ASP.NET Core JWT Bearer Authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/jwt-authn)
- [ASP.NET Core Identity Lockout](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/accconfirm)

---

## Build Commands

```bash
dotnet build
```

---

## Implementation Validation Strategy

- Login → JWT returned; Redis key `jti:{jti}` exists with TTL ~900s.
- Authenticated request → Redis TTL reset.
- Request with expired/missing Redis key → 401.
- 5th failed login → account locked; 6th attempt → 423/lockout response.
- `POST /auth/logout` → Redis key deleted; subsequent request with same token → 401.
- Redis stopped → authenticated request passes JWT signature validation; WARNING in logs.

---

## Implementation Checklist

- [ ] **[AC-001]** JWT TTL is 15 minutes (`expires = UtcNow.AddMinutes(15)`)
- [ ] **[AC-002]** Redis allowlist: `jti` stored on login; TTL reset on every authenticated request
- [ ] **[AC-003]** `POST /auth/extend-session` resets TTL and returns new token
- [ ] **[AC-004]** `POST /auth/logout` deletes Redis allowlist entry
- [ ] **[AC-005]** 5-attempt lockout configured via ASP.NET Core Identity
- [ ] **[AC-006]** Redis-unavailable fallback: JWT signature only; WARNING logged
- [ ] `JWT_SECRET` env var used for signing; not hardcoded
- [ ] `dotnet build` passes with 0 errors
