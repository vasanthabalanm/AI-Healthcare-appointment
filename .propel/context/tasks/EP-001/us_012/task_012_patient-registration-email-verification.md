# Task - TASK_012

## Requirement Reference

- **User Story**: US_012 — Patient self-registration + email verification
- **Story Location**: `.propel/context/tasks/EP-001/us_012/us_012.md`
- **Parent Epic**: EP-001

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `POST /auth/register` returns HTTP 201 on success |
| AC-002 | Email verification token valid for 24 hours; sent via MailKit |
| AC-003 | Duplicate email returns HTTP 409 |
| AC-004 | Rate limit: 10 registration attempts per IP per hour |
| AC-005 | `UserAccount` created with `Role=Patient`, `IsActive=false` until email verified |

### Edge Cases

- Registration with already-verified email → 409 (duplicate detection before token generation)
- Rate limit exceeded → 429 Too Many Requests

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
| Backend | ASP.NET Core Identity | .NET 8 | User management + password hashing per design.md |
| Backend | MailKit | 4.x | Email delivery per design.md |
| Backend | ASP.NET Core Rate Limiting | 8.x (built-in) | Rate limit middleware per design.md |
| Database | SQL Server | 2022 / Express | `UserAccount` storage |

---

## Task Overview

Implement `POST /auth/register` vertical-slice feature. Create `UserAccount` with `Role=Patient` and `IsActive=false`. Generate a 24-hour email verification token and send it via MailKit SMTPS. Enforce duplicate-email 409 and IP-based rate limiting (10/hour).

---

## Dependent Tasks

- **TASK_001 (us_007)** — `UserAccount` entity must exist
- **TASK_001 (us_002)** — Web API feature endpoint infrastructure

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Auth/RegisterEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Auth/RegisterRequest.cs`
- `src/ClinicalHealthcare.Infrastructure/Email/IEmailService.cs`
- `src/ClinicalHealthcare.Infrastructure/Email/MailKitEmailService.cs`
- `src/ClinicalHealthcare.Api/Program.cs` — rate limit policy registration

---

## Implementation Plan

1. Create `RegisterRequest` DTO: `Email`, `Password`, `FirstName`, `LastName`.
2. Implement `RegisterEndpoint`: validate DTO; check `UserAccount` for duplicate email (409 if found); hash password with PBKDF2 SHA-256 (100k iterations via `PasswordHasher<T>`); insert `UserAccount` with `Role=Patient`, `IsActive=false`.
3. Generate email verification token: `Convert.ToBase64Url(RandomNumberGenerator.GetBytes(32))`; store hash + expiry (`UtcNow.AddHours(24)`) in `UserAccount.VerificationTokenHash`.
4. Create `IEmailService` / `MailKitEmailService`; send verification email via SMTPS port 465; credentials from env vars `SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`, `SMTP_PASS`.
5. Configure ASP.NET Core rate limiting: fixed window, 10 requests, 1-hour window, keyed by `RemoteIpAddress`; apply policy to `POST /auth/register`.
6. Return HTTP 201 `{"message":"Registration successful. Check your email to verify your account."}`.
7. Implement `GET /auth/verify-email?token=...` to set `IsActive=true` on valid unexpired token.

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Auth/
└── README.md
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/Auth/RegisterEndpoint.cs` | POST /auth/register handler |
| CREATE | `src/ClinicalHealthcare.Api/Features/Auth/RegisterRequest.cs` | Registration DTO |
| CREATE | `src/ClinicalHealthcare.Api/Features/Auth/VerifyEmailEndpoint.cs` | GET /auth/verify-email handler |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Email/IEmailService.cs` | Email abstraction |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Email/MailKitEmailService.cs` | MailKit SMTPS implementation |
| MODIFY | `src/ClinicalHealthcare.Api/Program.cs` | Register rate limit policy; register IEmailService |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Entities/UserAccount.cs` | Add VerificationTokenHash + VerificationTokenExpiry fields |

---

## External References

- [ASP.NET Core Rate Limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit)
- [MailKit Documentation](https://github.com/jstedfast/MailKit)
- [ASP.NET Core Identity Password Hasher](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity)

---

## Build Commands

```bash
dotnet add src/ClinicalHealthcare.Infrastructure package MailKit
dotnet build
```

---

## Implementation Validation Strategy

- `POST /auth/register` with valid data → 201; `UserAccount.IsActive=false` in DB.
- `POST /auth/register` same email twice → 409.
- 11th request from same IP within 1 hour → 429.
- `GET /auth/verify-email?token=<valid>` → `IsActive=true`.
- `GET /auth/verify-email?token=<expired>` → 400.
- Email sent to SMTP sandbox; message contains verification link.

---

## Implementation Checklist

- [x] **[AC-001]** `POST /auth/register` returns 201 on success
- [x] **[AC-002]** Email verification token generated, stored hashed, valid 24h; sent via MailKit
- [x] **[AC-003]** Duplicate email returns 409
- [x] **[AC-004]** Rate limit: 10/IP/hour; exceeding returns 429 — wired via `RequireRateLimiting` middleware; verified by code inspection (unit tests cannot simulate the middleware pipeline)
- [x] **[AC-005]** `UserAccount` created with `Role=Patient`, `IsActive=false`
- [x] `GET /auth/verify-email` activates account on valid token
- [x] SMTP credentials from env vars only (no hardcoded values)
- [x] `dotnet build` passes with 0 errors
