# Task - TASK_013b

## Requirement Reference

- **User Story**: US_013 — Admin user account lifecycle management
- **Story Location**: `.propel/context/tasks/EP-001/us_013/us_013.md`
- **Parent Epic**: EP-001

### Gap Origin

TASK_013 AC-005 sends a credential-setup email to new Staff/Admin accounts containing the link:

```
{App:BaseUrl}/auth/setup-credentials?token=<token>
```

The token is stored in `UserAccount.VerificationTokenHash` / `VerificationTokenExpiry` (48-hour window). However, **no endpoint exists to consume this link**. A Staff or Admin user clicking the email link receives a 404. This task closes that gap.

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `POST /auth/setup-credentials` validates the setup token and sets the user's password; returns 200 |
| AC-002 | Token is single-use; a second call with the same token returns 400 |
| AC-003 | Expired token (> 48 hours) returns 400 |
| AC-004 | New password must meet complexity requirements (min 8 characters); returns 422 on violation |
| AC-005 | AuditLog entry written after successful credential setup (`action = "CREDENTIALS-SET"`) |

### Edge Cases

- Token not found in DB → 400 (same response as expired — no enumeration)
- Account is `IsActive=false` at time of setup → still process (account becomes usable after setup)
- Token valid but `VerificationTokenExpiry` is null → treat as expired → 400
- Whitespace-only password → 422 before any DB query
- Concurrent calls with the same token → last-writer-wins on `VerificationTokenHash = null` clear; idempotency not required

---

## Design References

N/A — UI Impact: No (Angular `SetupCredentialsComponent` is a separate FE task if needed)

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
| Backend | ASP.NET Core Web API | 8 LTS | Vertical-slice `IEndpointDefinition` pattern |
| Backend | ASP.NET Core Identity | .NET 8 | `IPasswordHasher<string>` for PBKDF2 hashing |
| Database | SQL Server | 2022 / Express | `UserAccount` token fields already exist |

---

## Task Overview

Implement `POST /auth/setup-credentials` — the endpoint that Staff and Admin users land on after clicking the credential-setup link in their welcome email. Validates the one-time token stored by `CreateUserEndpoint`, sets the account password, invalidates the token, and writes an audit entry.

---

## Dependent Tasks

- **TASK_013** — `CreateUserEndpoint` issues the setup token and stores it in `VerificationTokenHash` / `VerificationTokenExpiry`
- **TASK_013** — `UserAccount` entity already has `VerificationTokenHash`, `VerificationTokenExpiry`, `PasswordHash`
- **TASK_016** — `AuditLogHelper.Stage()` already exists for writing audit entries
- **TASK_017** — `ResetPasswordEndpoint` is the closest pattern to follow (same token lifecycle)

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Auth/SetupCredentialsEndpoint.cs` — new file
- `tests/ClinicalHealthcare.Infrastructure.Tests/Features/SetupCredentialsEndpointTests.cs` — new test file

---

## Implementation Plan

1. Create `POST /auth/setup-credentials` as a new `IEndpointDefinition` in the `Auth` feature folder.
2. Request body: `{ token: string, newPassword: string }`.
3. Validate inputs — 422 if `newPassword.Length < 8` or either field is blank.
4. Compute `SHA-256(token)` → look up `UserAccount` where `VerificationTokenHash = hash`.
5. If not found → 400 `{ "error": "Invalid or expired setup link." }` (no enumeration).
6. If `VerificationTokenExpiry < UtcNow` or `VerificationTokenExpiry == null` → 400 same message.
7. If token already consumed (`VerificationTokenHash == null`) → 400 same message.
8. Hash new password with `IPasswordHasher<string>` (PBKDF2 SHA-256 via ASP.NET Core Identity).
9. Update `UserAccount`: set `PasswordHash`, clear `VerificationTokenHash = null`, clear `VerificationTokenExpiry = null`.
10. Call `AuditLogHelper.Stage(db, "UserAccount", account.Id, account.Id, "CREDENTIALS-SET", null, after)`.
11. `SaveChangesAsync`.
12. Return 200 `{ "message": "Credentials set successfully. You can now log in." }`.

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Auth/
├── ForgotPasswordEndpoint.cs   (token lookup pattern — reference)
├── ResetPasswordEndpoint.cs    (token lifecycle pattern — primary reference)
├── RegisterEndpoint.cs         (verification token pattern — secondary reference)
└── LoginEndpoint.cs
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/Auth/SetupCredentialsEndpoint.cs` | `POST /auth/setup-credentials` — validates setup token, sets password, writes audit |
| CREATE | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/SetupCredentialsEndpointTests.cs` | Unit tests covering all ACs and edge cases |

---

## External References

- [ASP.NET Core Minimal APIs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis)
- [IPasswordHasher](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.identity.ipasswordhasher-1)

---

## Build Commands

```bash
dotnet build
dotnet test --no-build
```

---

## Implementation Validation Strategy

- Valid token → 200; `PasswordHash` updated in DB; `VerificationTokenHash` null in DB; `AuditLog` entry written.
- Same token second time → 400.
- Expired token (expiry in past) → 400.
- `newPassword` length < 8 → 422.
- Unknown token → 400.
- `dotnet build` → 0 errors; all tests pass.

---

## Implementation Checklist

- [x] **[AC-001]** `POST /auth/setup-credentials` validates token and sets password; returns 200
- [x] **[AC-002]** Token is single-use; second call returns 400
- [x] **[AC-003]** Expired token returns 400
- [x] **[AC-004]** Password < 8 chars returns 422
- [x] **[AC-005]** AuditLog entry written with `action = "CREDENTIALS-SET"`
- [x] Token lookup uses `VerificationTokenHash` (same field as patient email verification)
- [x] Handler is `public static` (allows direct invocation in unit tests — project convention)
- [x] `dotnet build` passes with 0 errors
- [x] All unit tests pass
