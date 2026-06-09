---
task: TASK_017
story: US_017 — Password reset via email token
reviewer: analyze-implementation workflow
date: 2026-05-16
verdict: Conditional Pass
---

# Implementation Analysis — TASK_017: Password Reset via Email Token

## Verdict

**Status:** Conditional Pass
**Summary:** All five acceptance criteria are fully implemented, the EF migration is in place, and 19 targeted unit tests pass alongside the existing 167 regression tests (186 total). Two medium-severity findings require remediation before the endpoint is production-hardened: no minimum password complexity check on `POST /auth/reset-password` (asymmetric with `RegisterEndpoint`), and no rate limiting on `POST /auth/forgot-password` (email-flooding attack vector). Four low-severity test-coverage gaps are identified. No critical or high OWASP violations were detected.

---

## Traceability Matrix

| Requirement / AC | Evidence (file : function : line) | Result |
|---|---|---|
| AC-001 — `/auth/forgot-password` always returns 200 | `ForgotPasswordEndpoint.cs` : `HandleForgotPassword` L56-66 — empty email, unknown email, inactive account all return `Ok(200)` with identical message | **Pass** |
| AC-002 — Token valid 60 min; stored as PBKDF2-SHA256 hash | `ForgotPasswordEndpoint.cs` : `TokenExpiryMinutes = 60` L24; `ComputePbkdf2Hash` L99-116 using `Rfc2898DeriveBytes` SHA256 100k; `PasswordReset` migration L15-27 | **Pass** |
| AC-003 — `/auth/reset-password` validates token; re-hashes password 100k iterations | `ResetPasswordEndpoint.cs` : `HandleResetPassword` L67-90 (TokenUsed/Expiry/hash checks); L91 `hasher.HashPassword` (Identity V3 PBKDF2 100k) | **Pass** |
| AC-004 — All Redis sessions revoked on successful reset | `ResetPasswordEndpoint.cs` : `RevokeAllSessionsAsync` L125-165; `LoginEndpoint.cs` L138-148 (SetAdd on login); `ExtendSessionEndpoint.cs` L72-78 (rotate on extend); `LogoutEndpoint.cs` L52-58 (remove on logout) | **Pass** |
| AC-005 — Token single-use; invalidated immediately | `ResetPasswordEndpoint.cs` : L93-94 `TokenUsed=true; TokenExpiry=null` set before `SaveChangesAsync` L110 | **Pass** |
| Edge: email not found → 200 (forgot-password) | `HandleForgotPassword` L63-66 | **Pass** |
| Edge: inactive account → 200 (forgot-password) | `u.IsActive` filter in LINQ query L62 | **Pass** |
| Edge: token already used → 400 same message | `ResetPasswordEndpoint.cs` L78 `TokenUsed` check | **Pass** |
| Edge: token expired → 400 same message | `ResetPasswordEndpoint.cs` L81-82 `Expiry < UtcNow` check | **Pass** |
| Migration: `PasswordResetTokenHash/Expiry/Used` columns | `20260516131026_PasswordReset.cs` L15-27 | **Pass** |
| SMTP via env vars (no hardcoded credentials) | `MailKitEmailService` reads `SMTP_*` from config; no literals in endpoint | **Pass** |
| Angular FE: `forgotPassword()` + `resetPassword()` in `AuthService` | `auth.service.ts` L118-130 | **Pass** |
| Angular FE: `ForgotPasswordComponent` wired to API | `forgot-password.component.ts` + `.html` — reactive form, success/error states | **Pass** |
| Angular FE: `ResetPasswordComponent` with token from query params | `reset-password.component.ts` L37-44 `ActivatedRoute.snapshot.queryParamMap` | **Pass** |
| Angular FE: public endpoints skipped by session interceptor | `session.interceptor.ts` — `/auth/forgot-password` and `/auth/reset-password` added to `isPublic` | **Pass** |

---

## Logical & Design Findings

### F1 — LOW: Deterministic Salt in `ComputePbkdf2Hash`

**Location:** `ForgotPasswordEndpoint.cs` L108 — `saltBytes = SHA256.HashData(tokenBytes)`

**Issue:** Salt is derived deterministically from the token rather than using a random per-entry salt. NIST SP 800-63B recommends a random salt to prevent pre-computation attacks.

**Impact:** Practically near-zero — the raw token carries 256-bit entropy, so an attacker who can compute the PBKDF2 hash still needs the raw token value. However, this deviates from standard practice and could fail a formal security audit.

**Recommendation:** If stricter compliance is required, add a `PasswordResetTokenSalt` (byte[16]) column, generate it with `RandomNumberGenerator.GetBytes(16)`, and store alongside the hash. Defer until a compliance audit requires it.

---

### F2 — MEDIUM: No Minimum Password Complexity on `/auth/reset-password`

**Location:** `ResetPasswordEndpoint.cs` — `HandleResetPassword`, no length check before `hasher.HashPassword`

**Issue:** `RegisterEndpoint.cs` L201 enforces `Password.Length >= 8` and returns a validation error if shorter. `ResetPasswordEndpoint` accepts any non-empty `NewPassword` (the `string.IsNullOrWhiteSpace` check at L62 only rejects empty strings). A user can reset their password to `"a"`.

**Impact:** Asymmetric security rule. A user who forgets their password can reset to a weaker value than they were permitted to set during registration.

**Fix:** Add the same check before hashing:

```csharp
if (request.NewPassword.Length < 8)
    return Results.UnprocessableEntity(new { error = "Password must be at least 8 characters." });
```

**ETA:** 30 min — **Risk: MEDIUM**

---

### F3 — MEDIUM: No Rate Limiting on `POST /auth/forgot-password`

**Location:** `ForgotPasswordEndpoint.cs` — no IP or per-email throttle

**Issue:** An attacker can call `/auth/forgot-password` with a victim's email address at high frequency. Each request: (1) overwrites the stored reset token (making any previously issued link invalid), (2) sends a new email. This is an email-flooding / denial-of-reset attack.

**Impact:** The victim receives a stream of reset emails and any link they click before the latest one will return 400 (invalid/expired token because it was overwritten). Disruptive, not destructive.

**Fix (recommended):** Add a `PasswordResetTokenIssuedAt` (DateTimeOffset?) column and reject new token generation if `IssuedAt > UtcNow.AddMinutes(-5)` — return 200 with the same message (no enumeration). Alternatively, integrate `AspNetCoreRateLimit` middleware on this route.

**ETA:** 1–2 h — **Risk: MEDIUM**

---

### Business Logic: Email Sent After DB Save

**Location:** `ForgotPasswordEndpoint.cs` L83-90 — `SaveChangesAsync` is called before `emailService.SendAsync`

**Note:** If the SMTP call fails (network error, mailbox full), the token is persisted in the DB but the user never receives the link. The user can request a new token (which overwrites the old one) without consequence. This ordering (save-then-send) is the correct approach to prevent phantom tokens from existing without a corresponding DB record. However, if `emailService.SendAsync` throws, the caller receives a 500 unhandled exception. No try/catch wraps the email send.

**Severity:** LOW — add a try/catch around the email send to return 200 with a logged warning rather than propagating a 500:

```csharp
try
{
    await emailService.SendAsync(account.Email, subject, htmlBody, ct);
}
catch (Exception ex)
{
    logger.LogWarning(ex, "ForgotPasswordEndpoint: failed to send reset email to {Email}.", account.Email);
}
return Results.Ok(new { message = "If that email is registered you will receive a reset link." });
```

**ETA:** 20 min — **Risk: LOW**

---

## Test Review

### Existing Tests (19 tests — all pass)

| Test | AC Covered | Status |
|---|---|---|
| `ForgotPassword_EmptyEmail_Returns200` | AC-001 | ✅ |
| `ForgotPassword_UnknownEmail_Returns200_NoEmailSent` | AC-001 edge | ✅ |
| `ForgotPassword_InactiveAccount_Returns200_NoEmailSent` | AC-001 edge | ✅ |
| `ForgotPassword_KnownEmail_Returns200_SendsEmail_StoresHash` | AC-001, AC-002 | ✅ |
| `ForgotPassword_TokenExpiry_IsApproximately60Minutes` | AC-002 | ✅ |
| `ForgotPassword_SameResponseMessage_ForKnownAndUnknownEmail` | AC-001 | ✅ |
| `ResetPassword_ValidToken_Returns200_PasswordChanged` | AC-003 | ✅ |
| `ResetPassword_ValidToken_MarksTokenUsed` | AC-005 | ✅ |
| `ResetPassword_ValidToken_ResetsFailedLoginCounters` | AC-003 bonus | ✅ |
| `ResetPassword_AlreadyUsedToken_Returns400` | AC-005 edge | ✅ |
| `ResetPassword_ExpiredToken_Returns400` | AC-002 edge | ✅ |
| `ResetPassword_WrongToken_Returns400` | AC-003 edge | ✅ |
| `ResetPassword_UnknownEmail_Returns400` | AC-003 edge | ✅ |
| `ResetPassword_MissingFields_Returns422` | Input validation | ✅ |
| `ResetPassword_NoTokenStored_Returns400` | AC-003 edge | ✅ |
| `ResetPassword_ValidToken_RevokesAllRedisSessions` | AC-004 | ✅ |
| `ResetPassword_RedisDown_StillReturns200_PasswordChanged` | AC-004 resilience | ✅ |
| `ComputePbkdf2Hash_SameInput_ReturnsSameHash` | AC-002 determinism | ✅ |
| `ComputePbkdf2Hash_DifferentInputs_ReturnDifferentHashes` | AC-002 uniqueness | ✅ |

### Missing Tests (should add)

- [ ] **Unit (LOW):** `ForgotPassword_CaseInsensitiveEmail_Returns200` — verify `"USER@EXAMPLE.COM"` triggers reset for seeded `"user@example.com"`. Tests the `normalizedEmail = email.Trim().ToLowerInvariant()` path.
- [ ] **Unit (LOW):** `ResetPassword_AuditLogEntry_IsWritten` — assert `db.AuditLogs.Count == 1` and `action == "PASSWORD-RESET"` after successful reset.
- [ ] **Unit (LOW):** `ForgotPassword_ResetLinkUrl_ContainsEmailAndToken` — capture `emailService.SendAsync` `htmlBody` argument and assert it contains `?email=` and `?token=` with the correct values.
- [ ] **Unit (LOW):** `ForgotPassword_SecondRequest_OverwritesPreviousToken` — call forgot-password twice; verify first token hash is replaced; old token's hash no longer matches.

---

## Validation Results

| Command | Outcome |
|---|---|
| `dotnet build --no-restore` | ✅ 0 errors, 0 warnings |
| `dotnet test --no-build` | ✅ 186/186 passed (10 Api.Tests + 176 Infrastructure.Tests, includes 19 new TASK_017 tests) |
| `npx ng build --configuration development` | ✅ Build complete — `forgot-password-component` 11.36 kB, `reset-password-component` 11.90 kB |

---

## Fix Plan (Prioritized)

| # | Finding | Files / Functions | ETA | Risk |
|---|---------|-------------------|-----|------|
| 1 | **F2** — Add `NewPassword.Length >= 8` check | `ResetPasswordEndpoint.cs` : `HandleResetPassword` before `hasher.HashPassword` | 30 min | MEDIUM |
| 2 | **F3** — Add per-email reset cooldown | `UserAccount.cs` : add `PasswordResetTokenIssuedAt`; `ForgotPasswordEndpoint.cs` : check before generating token + new migration | 1–2 h | MEDIUM |
| 3 | **Email send error handling** — wrap `emailService.SendAsync` in try/catch | `ForgotPasswordEndpoint.cs` : `HandleForgotPassword` L83-90 | 20 min | LOW |
| 4 | **Test F4** — AuditLog entry assertion | `PasswordResetEndpointTests.cs` | 20 min | LOW |
| 5 | **Test F5/F6** — Case-insensitive email + reset link URL tests | `PasswordResetEndpointTests.cs` | 30 min | LOW |

---

## Appendix

### Rules Applied

- `rules/security-standards-owasp.md` — OWASP A01-A09 alignment
- `rules/code-anti-patterns.md` — no magic constants, early returns, no god objects
- `rules/language-agnostic-standards.md` — KISS, YAGNI, clear naming
- `rules/backend-development-standards.md` — vertical-slice pattern, handler accessibility
- `rules/dry-principle-guidelines.md` — `ComputePbkdf2Hash` as single source of truth
- `rules/typescript-styleguide.md` — Angular signals, reactive forms, typed observables
- `rules/frontend-development-standards.md` — standalone components, lazy routes

### Search Evidence

| Pattern | File | Purpose |
|---|---|---|
| `ComputePbkdf2Hash` | `ForgotPasswordEndpoint.cs` L99 | Hash function — public static, shared with ResetPasswordEndpoint |
| `user-sessions:` | `LoginEndpoint.cs` L138, `ExtendSessionEndpoint.cs` L72, `LogoutEndpoint.cs` L52, `ResetPasswordEndpoint.cs` L155 | Redis set maintenance across 4 endpoints |
| `PasswordReset` migration | `20260516131026_PasswordReset.cs` | Schema change verified in snapshot |
| `Password.Length < 8` | `RegisterEndpoint.cs` L201 | Benchmark for F2 asymmetry finding |
| `isPublic` interceptor | `session.interceptor.ts` L27-32 | Both reset endpoints excluded from session refresh |
