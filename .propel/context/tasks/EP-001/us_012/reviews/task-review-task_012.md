# Implementation Analysis — task_012_patient-registration-email-verification.md

## Verdict

**Status:** Conditional Pass
**Summary:** TASK_012 implements all five acceptance criteria at the code level. The registration flow (POST /auth/register), email verification (GET /auth/verify-email), token security, duplicate detection, MailKit delivery, and PHI retention integration are all correct and fully unit-tested (100/100 pass). One HIGH severity gap is identified: the rate limiter uses `AddFixedWindowLimiter` (single global window) instead of a per-IP partitioned limiter as required by AC-004. Under concurrent multi-user load, 10 total requests per hour are permitted across all clients — not 10 per IP. Three MEDIUM findings also require attention: no rollback if email send fails post-save, missing index on `VerificationTokenHash`, and MaxLength constraints not enforced in manual validation. The task advances to Full Pass only after the per-IP rate limiter fix is applied and verified.

---

## Traceability Matrix

| Requirement / Acceptance Criterion | Evidence (file : function / line) | Result |
|---|---|---|
| AC-001: POST /auth/register returns 201 | `RegisterEndpoint.cs` : `HandleRegister()` L143 `Results.Created(...)` | Pass |
| AC-002: Token valid 24 h; stored hashed; sent via MailKit | `RegisterEndpoint.cs` : `HandleRegister()` L107–L138; `MailKitEmailService.cs` : `SendAsync()` | Pass |
| AC-002: Raw token in email link; SHA-256 hash in DB | `RegisterEndpoint.cs` : `ComputeSha256Hash()` L183; token stored via `VerificationTokenHash` | Pass |
| AC-002: Token expiry = UtcNow + 24 h | `RegisterEndpoint.cs` L109 `DateTime.UtcNow.AddHours(24)` | Pass |
| AC-003: Duplicate email → 409 | `RegisterEndpoint.cs` : `HandleRegister()` L96 `IgnoreQueryFilters().AnyAsync(...)` | Pass |
| AC-003: Case-insensitive duplicate detection | `RegisterEndpoint.cs` L94 `.Trim().ToLowerInvariant()` applied before AnyAsync | Pass |
| AC-004: Rate limit 10/IP/hour → 429 | `RegisterEndpoint.cs` : `AddServices()` L38 `AddFixedWindowLimiter` — **global window, not per-IP** | **Fail** |
| AC-005: Role=patient, IsActive=false | `RegisterEndpoint.cs` L112–L113 `Role = "patient"`, `IsActive = false` | Pass |
| GET /auth/verify-email activates account | `RegisterEndpoint.cs` : `HandleVerifyEmail()` L171 `account.IsActive = true` | Pass |
| Token consumed after first use | `RegisterEndpoint.cs` L172–L173 `VerificationTokenHash = null; VerificationTokenExpiry = null` | Pass |
| Expired token → 400 | `RegisterEndpoint.cs` L169 `account.VerificationTokenExpiry < DateTime.UtcNow` | Pass |
| SMTP credentials from env vars only | `MailKitEmailService.cs` : constructor `RequireEnv()` L28–L33 | Pass |
| Password hashed (PBKDF2) | `RegisterEndpoint.cs` L102 `hasher.HashPassword(normalizedEmail, request.Password)` | Pass |
| Schema migration applied | `20260514114322_UserAccountRegistrationFields.cs` — FirstName, LastName, VerificationTokenHash, VerificationTokenExpiry | Pass |
| PHI soft-delete retained (TASK_011 integration) | `ApplicationDbContext.InterceptPhiDeletes()` covers `UserAccount` | Pass |

---

## Logical & Design Findings

**Business Logic:**
- `Results.Created("/auth/register", body)` uses a static location URI. REST convention requires the newly created resource URI (e.g., `/auth/accounts/{id}`). Non-blocking but violates HTTP 201 semantics (Location header should point to the new resource).
- `VerificationTokenExpiry` uses `DateTime` (not `DateTimeOffset`). UTC is correctly used throughout but relying on `DateTime.UtcNow` requires the Kind to be set to UTC everywhere. Consistent usage observed — no bug, but `DateTimeOffset` would be more portable.

**Security:**
- **OWASP A07 (Identification and Authentication Failures)**: Rate limiter is global (`AddFixedWindowLimiter`), not per-IP. A single attacker can exhaust 10 slots per hour across all IPs, but more importantly, 10 different IPs can each make 10 attempts without restriction — the global limit provides no per-IP protection. Fix: replace with `AddPolicy(...)` + `RateLimitPartition.GetFixedWindowLimiter` keyed by `RemoteIpAddress`.
- **OWASP A03 (Injection)**: `account.FirstName` is interpolated into the HTML email body without HTML encoding: `<p>Welcome to ClinicalHub, {account.FirstName}!</p>`. If a patient's first name contains HTML/script characters, they would be injected into the email body. Mitigation: apply `System.Web.HttpUtility.HtmlEncode(account.FirstName)` (or `System.Net.WebUtility.HtmlEncode`).
- Email validation in `ValidateRequest()` checks only for `@` presence — weaker than the `[EmailAddress]` DataAnnotation on the DTO. Consider using `System.Net.Mail.MailAddress` try-parse for consistent validation.

**Error Handling:**
- `emailService.SendAsync(...)` is called after `await db.SaveChangesAsync(...)` (L133). If SMTP fails, the account is committed to the DB with a valid token hash, but the user never receives the verification email. There is no mechanism to resend the token. This creates orphaned accounts that cannot be activated. Mitigation: either (a) wrap in a transaction and roll back on email failure, or (b) expose a "resend verification email" endpoint.
- No error handling around `int.Parse(RequireEnv("SMTP_PORT"))` in `MailKitEmailService` constructor — an invalid port string throws `FormatException`, not the `InvalidOperationException` used for missing env vars. The error message will be cryptic. Fix: use `int.TryParse` with a descriptive `InvalidOperationException`.

**Data Access:**
- `VerificationTokenHash` column (`nvarchar(128)`) has no database index. `GET /auth/verify-email` performs `FirstOrDefaultAsync(u => u.VerificationTokenHash == tokenHash)` — this is a full table scan on `UserAccounts` with `IgnoreQueryFilters()`. At production scale, add an index.
- Duplicate email check uses `AnyAsync` (single query, no materialisation) — efficient. ✅
- `IgnoreQueryFilters()` correctly bypasses the `HasQueryFilter(!IsDeleted)` on both the duplicate check and verify-email lookup. ✅

**Performance:**
- `MailKitEmailService` creates a new `SmtpClient` per email (open/auth/send/disconnect). This is correct for correctness but inefficient at scale. Acceptable for current load profile; a connection pool would be needed for bulk email.

**Patterns & Standards:**
- Vertical-slice `IEndpointDefinition` pattern correctly followed. ✅
- `public static` handler methods enable direct unit testing without HTTP host. ✅
- `AddServices` guard (`if (!services.Any(d => d.ServiceType == typeof(IEmailService)))`) correctly prevents double-registration in tests. ✅
- `RegistrationRateLimitPolicy` exposed as `public const` for testability. ✅

---

## Test Review

**Existing Tests (12 unit tests in `RegisterEndpointTests.cs` — all pass):**

| Test | AC | Result |
|---|---|---|
| `Register_WithValidRequest_Returns201` | AC-001 | Pass |
| `Register_CreatesAccount_WithPatientRole_AndIsActiveFalse` | AC-005 | Pass |
| `Register_SetsVerificationTokenHash_And24hExpiry` | AC-002 | Pass |
| `Register_TokenHash_IsDifferentFromRawToken` | AC-002 | Pass |
| `Register_SendsVerificationEmail_ToCorrectAddress` | AC-002 | Pass |
| `Register_DuplicateEmail_Returns409` | AC-003 | Pass |
| `Register_DuplicateEmail_CaseInsensitive_Returns409` | AC-003 | Pass |
| `VerifyEmail_WithValidToken_ActivatesAccount` | verify-email | Pass |
| `VerifyEmail_WithExpiredToken_Returns400` | verify-email | Pass |
| `VerifyEmail_WithInvalidToken_Returns400` | verify-email | Pass |
| `VerifyEmail_TokenIsConsumed_AfterFirstUse` | verify-email | Pass |
| `Register_ShortPassword_Returns422` | validation | Pass |

**Missing Tests (recommended additions):**

- [ ] Unit: `Register_EmptyEmail_Returns422` — null/empty email field returns 422 with `errors.email`
- [ ] Unit: `Register_EmptyFirstName_Returns422` — null/empty first name field returns 422
- [ ] Unit: `Register_EmptyLastName_Returns422` — null/empty last name field returns 422
- [ ] Unit: `Register_EmailWithoutAtSign_Returns422` — malformed email returns 422
- [ ] Unit: `Register_DuplicateSoftDeletedEmail_Returns409` — soft-deleted account still blocks re-registration
- [ ] Unit: `VerifyEmail_NullToken_Returns400` — null/whitespace token returns 400 (currently hits `string.IsNullOrWhiteSpace` guard)
- [ ] Integration: `Register_RateLimit_11thRequest_Returns429` — requires `WebApplicationFactory`; confirms per-IP 429 behavior after 10 attempts

---

## Validation Results

**Commands Executed:**

```
dotnet test tests/ClinicalHealthcare.Infrastructure.Tests/ClinicalHealthcare.Infrastructure.Tests.csproj
```

**Outcomes:**

```
Passed! - Failed: 0, Passed: 100, Skipped: 0, Total: 100, Duration: 7 s
```

All 100 tests pass. Includes 88 prior-task tests (TASK_010/011) + 12 new TASK_012 unit tests.

---

## Fix Plan (Prioritized)

1. **Fix per-IP rate limiter** — `RegisterEndpoint.cs` : `AddServices()` — Replace `AddFixedWindowLimiter` with `AddPolicy(...)` + `RateLimitPartition.GetFixedWindowLimiter` keyed by `context.Connection.RemoteIpAddress` — ETA 30 min — Risk: **High** (AC-004 behaviorally incorrect without this fix)

2. **Add index on VerificationTokenHash** — new SQL migration `AddVerificationTokenHashIndex` — `ALTER INDEX` / `migrationBuilder.CreateIndex` on `UserAccounts.VerificationTokenHash` — ETA 15 min — Risk: **Medium** (performance degradation at scale)

3. **Enforce MaxLength in ValidateRequest()** — `RegisterEndpoint.cs` : `ValidateRequest()` — Add `r.Email.Length > 256` and `r.Password.Length > 128` guards — ETA 15 min — Risk: **Medium** (potential DB truncation / silent data loss for oversized inputs)

4. **HTML-encode FirstName in email body** — `RegisterEndpoint.cs` : `HandleRegister()` L122 — Replace `{account.FirstName}` with `{System.Net.WebUtility.HtmlEncode(account.FirstName)}` — ETA 10 min — Risk: **Low** (HTML injection in email body)

5. **Harden SMTP_PORT parsing** — `MailKitEmailService.cs` : constructor — Replace `int.Parse(RequireEnv("SMTP_PORT"))` with `int.TryParse` + descriptive `InvalidOperationException` — ETA 10 min — Risk: **Low** (startup error clarity)

6. **Document email-before-save trade-off** — `RegisterEndpoint.cs` — Add XML-doc comment on `HandleRegister` noting that email send failure results in an unverifiable account; add resend endpoint to backlog — ETA 5 min — Risk: **Low** (operational awareness)

---

## Appendix

**Rules Applied:**

- `rules/security-standards-owasp.md` — OWASP A03 (injection), A07 (auth failures), A09 (logging)
- `rules/backend-development-standards.md` — service/controller patterns, async/await
- `rules/dotnet-architecture-standards.md` — .NET vertical-slice, DI, minimal API patterns
- `rules/language-agnostic-standards.md` — KISS, YAGNI, naming consistency
- `rules/database-standards.md` — index strategy, migration quality
- `rules/dry-principle-guidelines.md` — no duplication observed
- `rules/code-anti-patterns.md` — no god objects or magic constants

**Search Evidence:**

- `RegisterEndpoint.cs` — `src/ClinicalHealthcare.Api/Features/Auth/RegisterEndpoint.cs`
- `RegisterRequest.cs` — `src/ClinicalHealthcare.Api/Features/Auth/RegisterRequest.cs`
- `MailKitEmailService.cs` — `src/ClinicalHealthcare.Infrastructure/Email/MailKitEmailService.cs`
- `IEmailService.cs` — `src/ClinicalHealthcare.Infrastructure/Email/IEmailService.cs`
- `UserAccount.cs` — `src/ClinicalHealthcare.Infrastructure/Entities/UserAccount.cs`
- `ApplicationDbContext.cs` — `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs`
- Migration — `src/ClinicalHealthcare.Infrastructure.SqlMigrations/Migrations/20260514114322_UserAccountRegistrationFields.cs`
- Tests — `tests/ClinicalHealthcare.Infrastructure.Tests/Features/RegisterEndpointTests.cs`
