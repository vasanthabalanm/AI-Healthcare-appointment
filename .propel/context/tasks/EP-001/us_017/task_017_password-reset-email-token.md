# Task - TASK_017

## Requirement Reference

- **User Story**: US_017 — Password reset via email token
- **Story Location**: `.propel/context/tasks/EP-001/us_017/us_017.md`
- **Parent Epic**: EP-001

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `POST /auth/forgot-password` always returns 200 (no email enumeration) |
| AC-002 | Password reset token is valid for 60 minutes; stored as PBKDF2 SHA-256 hash |
| AC-003 | `POST /auth/reset-password` validates token; resets password with PBKDF2 SHA-256 100k iterations |
| AC-004 | All active Redis sessions for the user are revoked on successful password reset |
| AC-005 | Reset token is single-use; invalidated immediately after successful reset |

### Edge Cases

- Email not found → still returns 200 (no email enumeration); no email sent; no error logged
- Token already used → 400 `{"error":"Reset token is invalid or has expired"}`
- Token expired (>60 min) → 400 same message

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
| Backend | ASP.NET Core Web API | 8 LTS | Endpoint hosting |
| Backend | ASP.NET Core Identity | .NET 8 | PBKDF2 password hashing |
| Backend | MailKit | 4.x | Reset email delivery per design.md |
| Infrastructure | Upstash Redis | N/A | Session revocation on reset |

---

## Task Overview

Implement `POST /auth/forgot-password` (always 200, no email enumeration) and `POST /auth/reset-password`. Generate a 60-minute single-use token stored as PBKDF2 hash. Revoke all Redis sessions on successful reset.

---

## Dependent Tasks

- **TASK_001 (us_015)** — Redis allowlist (for session revocation pattern)
- **TASK_001 (us_012)** — `IEmailService`

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Auth/ForgotPasswordEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Auth/ResetPasswordEndpoint.cs`
- `src/ClinicalHealthcare.Infrastructure/Entities/UserAccount.cs` — reset token fields

---

## Implementation Plan

1. Add `PasswordResetTokenHash`, `PasswordResetTokenExpiry`, `PasswordResetTokenUsed` to `UserAccount` entity; create migration.
2. Implement `POST /auth/forgot-password` (`[AllowAnonymous]`): look up user by email; if not found → return 200 (no action); if found → generate reset token (`RandomNumberGenerator.GetBytes(32)`); hash via PBKDF2 SHA-256; store hash + expiry (`UtcNow.AddMinutes(60)`) + `Used=false`; send reset email via MailKit; return 200.
3. Implement `POST /auth/reset-password` (`[AllowAnonymous]`): accept `{email, token, newPassword}`; load user; verify `TokenUsed=false`, `Expiry > UtcNow`, hash matches; if invalid → 400; hash new password (100k PBKDF2 SHA-256 iterations); save; mark token as `Used=true`; revoke all Redis sessions for user (scan + delete `jti:*` keys by user pattern or store a user→jti index).
4. Redis session revocation: maintain a Redis set `user-sessions:{userId}` containing all active JTIs; on reset, delete all keys in the set + delete the set.
5. Update `LoginEndpoint` (us_015) to add JTI to the `user-sessions:{userId}` set.

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Auth/
├── LoginEndpoint.cs
├── RegisterEndpoint.cs
├── ExtendSessionEndpoint.cs
└── LogoutEndpoint.cs
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/Auth/ForgotPasswordEndpoint.cs` | POST /auth/forgot-password (always 200) |
| CREATE | `src/ClinicalHealthcare.Api/Features/Auth/ResetPasswordEndpoint.cs` | POST /auth/reset-password |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Entities/UserAccount.cs` | Add reset token hash/expiry/used fields |
| MODIFY | `src/ClinicalHealthcare.Api/Features/Auth/LoginEndpoint.cs` | Add JTI to user-sessions Redis set |
| CREATE | `src/ClinicalHealthcare.Infrastructure.SqlMigrations/Migrations/*_PasswordReset.cs` | Migration for reset token fields |

---

## External References

- [NIST Password Guidelines](https://pages.nist.gov/800-63-3/)
- [ASP.NET Core Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/)

---

## Build Commands

```bash
dotnet ef migrations add PasswordReset --project src/ClinicalHealthcare.Infrastructure.SqlMigrations --startup-project src/ClinicalHealthcare.Api --context ApplicationDbContext
dotnet build
```

---

## Implementation Validation Strategy

- `POST /auth/forgot-password` with unknown email → 200; no email sent.
- Valid reset flow: request → email → reset → login with new password succeeds.
- Expired token (>60 min) → 400.
- Used token → 400.
- After reset, old JWT returns 401 (Redis sessions revoked).

---

## Implementation Checklist

- [x] **[AC-001]** `POST /auth/forgot-password` always returns 200 regardless of email existence
- [x] **[AC-002]** Reset token valid 60 min; stored as PBKDF2 SHA-256 hash
- [x] **[AC-003]** `POST /auth/reset-password` validates token; re-hashes password with 100k iterations
- [x] **[AC-004]** All Redis sessions for user revoked on successful reset
- [x] **[AC-005]** Reset token marked `Used=true` immediately after successful reset (single-use)
- [x] User-sessions Redis set (`user-sessions:{userId}`) maintained for bulk revocation
- [x] `SMTP_*` env vars used for email (no hardcoded credentials)
- [x] `dotnet build` passes with 0 errors
