# Implementation Analysis -- TASK_028: Email Appointment Reminders + Cancellation Link

## Verdict

**Status:** Conditional Pass
**Summary:** All four acceptance criteria are structurally implemented: MailKit SMTPS (AC-001),
48-hour single-use cancellation link with 256-bit entropy (AC-002), SHA-256 hash storage with
`CancellationLinkUsed` sentinel (AC-003), and `EmailReminderJobId` deletion on cancellation
(AC-004). Two HIGH findings block full acceptance: (1) no unit tests exist for
`SendEmailReminderJob` or `CancelByLinkEndpoint`, leaving the token lifecycle, retry semantics,
and edge-case validation paths entirely untested; (2) Hangfire retry generates a new cryptographic
token on each attempt, which invalidates the cancellation link already delivered to the patient's
inbox. Two MEDIUM findings cover a missing DB index and incomplete STARTTLS support.

---

## Rules Applied

- `security-standards-owasp` — OWASP A02 Cryptographic Failures: raw token never persisted,
  SHA-256 hash only; OWASP A07 Identification Failures: `AllowAnonymous` with CSPRNG token auth
- `dry-principle-guidelines` — `RequireEnv` pattern; `ComputeSha256Hex` shared between job and
  endpoint via `public static`
- `code-anti-patterns` — `RequireEnv` fail-fast vs silent default; `MailboxAddress.TryParse`
  instead of `.Parse`
- `unit-testing-standards` — required coverage of happy path, used-token guard, expired-token
  guard, not-found guard, email parse failure, retry idempotence
- `backend-development-standards` — Hangfire job shape, scoped DI, `IJobCancellationToken`;
  persist-before-send pattern for retry safety
- `language-agnostic-standards` — early return defensive guards; status guard before side effects
- `database-standards` — index on lookup column; migration reversibility

---

## Traceability Matrix

| Requirement / Acceptance Criterion | Evidence (file : line) | Result |
|---|---|---|
| AC-001 — Email sent via MailKit SMTPS port 465 | `SendEmailReminderJob.cs` — `smtpClient.ConnectAsync(..., SecureSocketOptions.SslOnConnect)` | Pass |
| AC-001 — SMTP credentials from env vars | `SendEmailReminderJob.cs` — `RequireEnv("SMTP_HOST/PORT/USER/PASS/FROM_ADDRESS")` | Pass |
| AC-001 — STARTTLS port 587 option | **PARTIAL** — `SslOnConnect` hardcoded; port 587 will fail at runtime | **Partial** |
| AC-002 — 48-hour expiry on cancellation link | `SendEmailReminderJob.cs` L91 `appointment.CancellationLinkExpiry = DateTime.UtcNow.AddHours(48)` | Pass |
| AC-002 — Single-use token in email body | `SendEmailReminderJob.cs` — `cancelUrl` embedded in HTML body | Pass |
| AC-002 — 256-bit token entropy | `SendEmailReminderJob.cs` L83 `RandomNumberGenerator.GetBytes(32)` | Pass |
| AC-002 — Base64URL encoding (URL-safe) | `SendEmailReminderJob.cs` — `Base64UrlEncode` strips `=`, replaces `+`/`/` | Pass |
| AC-003 — SHA-256 hash stored, not raw token | `Appointment.cs` `CancellationLinkTokenHash [MaxLength(64)]`; `ComputeSha256Hex` called before save | Pass |
| AC-003 — `CancellationLinkUsed = true` on first use | `CancelByLinkEndpoint.cs` L74 `appointment.CancellationLinkUsed = true` | Pass |
| AC-003 — Used-token guard returns 400 | `CancelByLinkEndpoint.cs` L69 `if (appointment.CancellationLinkUsed) → BadRequest` | Pass |
| AC-003 — Expired-token guard returns 400 | `CancelByLinkEndpoint.cs` L72 `if (expiry < DateTime.UtcNow) → BadRequest` | Pass |
| AC-003 — Token persist-before-send (retry safety) | `SendEmailReminderJob.cs` L97 `SaveChangesAsync` before `smtpClient.ConnectAsync` | Pass |
| AC-003 — Retry overwrites token (idempotence break) | **FAIL** — new `RandomNumberGenerator.GetBytes(32)` on every retry; prior link invalidated | **Fail** |
| AC-004 — `EmailReminderJobId` stored on booking | `BookAppointmentEndpoint.cs` L150–154 `jobs.Schedule<SendEmailReminderJob>` → `appointment.EmailReminderJobId` | Pass |
| AC-004 — `EmailReminderJobId` deleted on API cancel | `CancelAppointmentEndpoint.cs` L99–100 `ChangeState(EmailReminderJobId, DeletedState)` | Pass |
| AC-004 — `EmailReminderJobId` deleted on link cancel | `CancelByLinkEndpoint.cs` L79–80 `ChangeState(EmailReminderJobId, DeletedState)` | Pass |
| AC-004 — `EmailReminderJobId` deleted + re-created on reschedule | `RescheduleAppointmentEndpoint.cs` L131–196 — delete old, schedule new | Pass |
| Edge: invalid token → 400 | `CancelByLinkEndpoint.cs` L67 `if (appointment is null) → BadRequest` | Pass |
| Edge: appointment already cancelled → 400 | `CancelByLinkEndpoint.cs` L76 `if (status != Scheduled) → BadRequest` | Pass |
| All reminder job IDs cleared on link cancel | `CancelByLinkEndpoint.cs` L77–84 — clears `ReminderJobId`, `EmailReminderJobId`, `SmsReminderJobId48h/2h` | Pass |
| `SwapMonitorJob` enqueued on link cancel | `CancelByLinkEndpoint.cs` L87 `jobs.Enqueue<SwapMonitorJob>` | Pass |
| Slot cache invalidated on link cancel | `CancelByLinkEndpoint.cs` L91–96 `cache.DeleteAsync(dateKey)` | Pass |
| EF migration adds 4 new columns | `20260518122923_AppointmentCancellationToken.cs` — `EmailReminderJobId`, `CancellationLinkTokenHash`, `CancellationLinkExpiry`, `CancellationLinkUsed` | Pass |
| Migration `Down()` reverses all columns | `20260518122923_AppointmentCancellationToken.cs` — 4 `DropColumn` calls | Pass |
| DB index on `CancellationLinkTokenHash` | **FAIL** — no `HasIndex` in `ApplicationDbContext.cs` or migration | **Fail** |
| `MailboxAddress.TryParse` guard on patient email | `SendEmailReminderJob.cs` L107 `MailboxAddress.TryParse(patient.Email, ...)` | Pass |
| `APP_BASE_URL` from env var with safe default | `SendEmailReminderJob.cs` L101 `GetEnvironmentVariable("APP_BASE_URL") ?? "http://localhost:5153"` | Pass |
| Unit tests for `SendEmailReminderJob` | No test file in `tests/.../Jobs/` | **Gap** |
| Unit tests for `CancelByLinkEndpoint` | No test file in `tests/.../Features/` | **Gap** |
| `dotnet build` 0 errors | Confirmed — `Build succeeded. 0 Warning(s). 0 Error(s).` | Pass |
| `dotnet test` 326 passing | Confirmed — 13 Api.Tests + 313 Infrastructure.Tests | Pass |

---

## Logical & Design Findings

### Business Logic

- `CancelByLinkEndpoint` correctly orders validation: used-flag check before expiry check before
  status check. This ordering ensures that a used token cannot be exploited via a race condition
  where `CancellationLinkUsed` is false but the appointment is already cancelled ✅
- The `AllowAnonymous` + CSPRNG token pattern is the correct design for email link cancellation;
  no JWT is required or appropriate here ✅
- `BodyBuilder.HtmlBody` only (no plain-text fallback). Email clients that disable HTML will show
  a blank body. Low severity for internal tooling but worth noting ✅

### Security

- Raw token never persisted to DB — only `ComputeSha256Hex(rawToken)` stored ✅
- `ComputeSha256Hex` uses `SHA256.HashData` (no padding, no IV) — appropriate for
  secret-token hashing (not password hashing; PBKDF2 not required here) ✅
- Token entropy: 32 bytes = 256 bits — meets OWASP A07 minimum ✅
- No timing oracle: comparison is performed by DB lookup (hash equality), not a
  string-level loop ✅
- `MailboxAddress.TryParse` prevents `FormatException` propagation on malformed email ✅
- SMTP credentials retrieved via `RequireEnv` at call time; not stored in constructor
  fields ✅

### Error Handling

- `RequireEnv` throws `InvalidOperationException` on missing env var → Hangfire moves job to
  dead-letter queue immediately (fail-fast; appropriate for infrastructure misconfiguration) ✅
- `IJobCancellationToken.ShutdownToken` passed to all async DB and SMTP calls → graceful
  shutdown on Hangfire server stop ✅
- Non-existent token → DB lookup returns `null` → `BadRequest` with opaque error message
  (does not leak DB structure) ✅

---

## Findings

### F1 [HIGH] — No unit tests for `SendEmailReminderJob` or `CancelByLinkEndpoint`

Neither `SendEmailReminderJob.ExecuteAsync` nor `CancelByLinkEndpoint.HandleCancelByLink` has any
test coverage. The token lifecycle (generation → hash → DB persist → email send), all five guard
branches in `CancelByLinkEndpoint`, and the retry behaviour are completely untested.

**Missing tests (must add):**

- `SendEmailReminderJob_AppointmentNotFound_Skips` — no appointment in DB; no exception thrown
- `SendEmailReminderJob_CancelledAppointment_SkipsWithoutEmail` — status = Cancelled; verify
  no SMTP call
- `SendEmailReminderJob_NullPatient_SkipsWithWarning` — patient navigation null; LogWarning
- `SendEmailReminderJob_InvalidEmail_SkipsWithWarning` — `patient.Email = "not-an-email"`;
  `TryParse` fails; LogWarning; no SMTP call
- `SendEmailReminderJob_ValidAppointment_PersistsTokenHashBeforeSend` — verify
  `CancellationLinkTokenHash` is non-null and `CancellationLinkExpiry` ~ UtcNow+48h
- `CancelByLink_ValidToken_CancelsAppointment` — happy path; status = Cancelled, used = true
- `CancelByLink_AlreadyUsedToken_Returns400` — `CancellationLinkUsed = true` → 400
- `CancelByLink_ExpiredToken_Returns400` — `CancellationLinkExpiry < UtcNow` → 400
- `CancelByLink_TokenNotFound_Returns400` — no matching hash → 400
- `CancelByLink_AlreadyCancelledAppointment_Returns400` — status = Cancelled → 400
- `CancelByLink_ValidToken_DeletesAllReminderJobIds` — verify all four job ID deletes

### F2 [HIGH] — Hangfire retry invalidates the patient's inbox link

`SendEmailReminderJob.ExecuteAsync` always generates a fresh 32-byte token regardless of whether
a valid token already exists. The persist-before-send ordering protects against a crash *after*
`SaveChangesAsync` but *before* `SmtpClient.SendAsync` — in this case, the retry overwrites the
DB hash with a new token while the patient's email (if delivered) contains the old token. The
patient's cancellation link becomes permanently broken.

**File:** `src/ClinicalHealthcare.Infrastructure/Jobs/SendEmailReminderJob.cs` — step 2 (token
generation, lines 83–97)

**Fix:** Before generating a new token, check whether a valid unexpired token already exists:

```csharp
// Reuse existing token if valid (idempotent retry).
if (appointment.CancellationLinkTokenHash is null ||
    appointment.CancellationLinkExpiry is null ||
    appointment.CancellationLinkExpiry <= DateTime.UtcNow)
{
    var rawBytes  = RandomNumberGenerator.GetBytes(32);
    var rawToken  = Base64UrlEncode(rawBytes);
    var tokenHash = ComputeSha256Hex(rawToken);

    appointment.CancellationLinkTokenHash = tokenHash;
    appointment.CancellationLinkExpiry    = DateTime.UtcNow.AddHours(48);
    appointment.CancellationLinkUsed      = false;

    await _db.SaveChangesAsync(cancellationToken.ShutdownToken);
}
// else: reuse the existing token — do NOT call SaveChangesAsync here.
var existingHash = appointment.CancellationLinkTokenHash!;
// Re-derive cancelUrl from raw token is not possible; must store raw token temporarily
// or change architecture. See note below.
```

**Architecture note:** Because only the hash is stored, the raw token cannot be recovered on retry
for inclusion in the email. Two viable approaches:

1. **(Recommended)** Encrypt the raw token with a server-side key and store the ciphertext as a
   second column (`CancellationLinkTokenEncrypted`). On retry, decrypt to recover the raw token.
2. **Simpler:** Accept that the first successful delivery is the canonical one; on retry (which
   means SMTP failed), generate a new token and re-deliver. Guard idempotence at the SMTP layer by
   checking whether the first send was confirmed via a delivery webhook or simply accept re-send.

The simplest production-safe fix is **option 2** with a guard:

```csharp
// If a non-expired token exists AND the appointment was already emailed,
// skip re-sending to avoid duplicate emails.
// This requires an `EmailReminderSentAt` timestamp field on Appointment.
if (appointment.EmailReminderSentAt is not null) return;
```

### F3 [MEDIUM] — Missing DB index on `CancellationLinkTokenHash`

`CancelByLinkEndpoint` performs `FirstOrDefaultAsync(a => a.CancellationLinkTokenHash == tokenHash)`
with no index on that column. On a table with thousands of appointments this is a full table scan
on every cancellation request.

**File:** `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs` and
`20260518122923_AppointmentCancellationToken.cs`

**Fix — add in `ApplicationDbContext.OnModelCreating`:**

```csharp
e.HasIndex(a => a.CancellationLinkTokenHash)
 .IsUnique()
 .HasFilter("[CancellationLinkTokenHash] IS NOT NULL")
 .HasDatabaseName("IX_Appointments_CancellationLinkTokenHash");
```

**Also add to migration `Up()`:**

```csharp
migrationBuilder.CreateIndex(
    name: "IX_Appointments_CancellationLinkTokenHash",
    table: "Appointments",
    column: "CancellationLinkTokenHash",
    unique: true,
    filter: "[CancellationLinkTokenHash] IS NOT NULL");
```

### F4 [MEDIUM] — STARTTLS (port 587) not supported despite AC-001 specification

AC-001 states: "Email reminder sent via MailKit (SMTPS port 465 **or** STARTTLS port 587)". The
implementation hardcodes `SecureSocketOptions.SslOnConnect`. If `SMTP_PORT=587`, MailKit will
attempt a TLS handshake immediately on connect, which most SMTP servers reject on port 587,
causing a connection failure and Hangfire retries.

**File:** `src/ClinicalHealthcare.Infrastructure/Jobs/SendEmailReminderJob.cs` — SMTP connect

**Fix:**

```csharp
var secureOptions = smtpPort == 587
    ? SecureSocketOptions.StartTls
    : SecureSocketOptions.SslOnConnect;

await smtpClient.ConnectAsync(smtpHost, smtpPort, secureOptions,
    cancellationToken.ShutdownToken);
```

### F5 [LOW] — Redundant `SaveChangesAsync` calls in `BookAppointmentEndpoint`

`BookAppointmentEndpoint.cs` ends with two consecutive conditional `SaveChangesAsync` calls:

```csharp
if (appointment.SmsReminderJobId48h is not null || appointment.SmsReminderJobId2h is not null)
    await db.SaveChangesAsync(ct); // persist SMS job IDs

if (appointment.EmailReminderJobId is not null)
    await db.SaveChangesAsync(ct); // persist email reminder job ID
```

When both SMS and email jobs are scheduled, this performs two separate DB round-trips for the same
tracked entity. The second `SaveChangesAsync` already includes the SMS IDs (same tracked entity),
making the first call redundant.

**Fix:** Collapse into a single conditional:

```csharp
if (appointment.SmsReminderJobId48h is not null  ||
    appointment.SmsReminderJobId2h  is not null  ||
    appointment.EmailReminderJobId  is not null)
    await db.SaveChangesAsync(ct);
```

---

## Test Review

### Existing Tests

| File | Status |
|---|---|
| `BookAppointmentEndpointTests.cs` | Verifies booking flow; **`EmailReminderJobId` scheduling NOT verified** |
| `CancelAppointmentEndpointTests.cs` | Verifies `ReminderJobId` + `SmsReminderJobId48h/2h` deletion; **`EmailReminderJobId` NOT verified** |
| `RescheduleAppointmentEndpointTests.cs` | Verifies reschedule; **`EmailReminderJobId` re-schedule NOT verified** |
| *(none)* | `SendEmailReminderJob.ExecuteAsync` — zero coverage |
| *(none)* | `CancelByLinkEndpoint.HandleCancelByLink` — zero coverage |

### Missing Tests (must add)

**F1 — Required additions:**

- [ ] `SendEmailReminderJob_AppointmentNotFound_Skips`
- [ ] `SendEmailReminderJob_CancelledAppointment_SkipsWithoutEmail`
- [ ] `SendEmailReminderJob_NullPatient_SkipsWithWarning`
- [ ] `SendEmailReminderJob_InvalidEmail_SkipsWithWarning`
- [ ] `SendEmailReminderJob_ValidAppointment_PersistsTokenHashBeforeSend`
- [ ] `CancelByLink_ValidToken_CancelsAppointment`
- [ ] `CancelByLink_AlreadyUsedToken_Returns400`
- [ ] `CancelByLink_ExpiredToken_Returns400`
- [ ] `CancelByLink_TokenNotFound_Returns400`
- [ ] `CancelByLink_AlreadyCancelledAppointment_Returns400`
- [ ] `CancelByLink_ValidToken_DeletesAllReminderJobIds`

---

## Validation Results

**Commands Executed:**

```bash
dotnet build --no-restore
dotnet test --no-build
```

**Outcomes:** Build: 0 errors, 0 warnings. Tests: 326 passed (13 Api.Tests + 313
Infrastructure.Tests), 0 failed. The retry-token invalidation (F2), missing index (F3), and
STARTTLS gap (F4) are not caught by the current test suite.

---

## Fix Plan (Prioritized)

| # | Finding | Severity | File(s) | Fix | Risk |
|---|---|---|---|---|---|
| F1 | No unit tests for `SendEmailReminderJob` + `CancelByLinkEndpoint` | HIGH | `tests/.../Jobs/`, `tests/.../Features/` | Add 11 tests listed above | Low |
| F2 | Hangfire retry generates new token, breaks inbox link | HIGH | `SendEmailReminderJob.cs` L83–97 | Reuse existing valid token; add `EmailReminderSentAt` guard | Medium |
| F3 | No DB index on `CancellationLinkTokenHash` | MEDIUM | `ApplicationDbContext.cs`, `AppointmentCancellationToken` migration | Add filtered unique index | Low |
| F4 | `SslOnConnect` hardcoded; port 587 unsupported | MEDIUM | `SendEmailReminderJob.cs` — SMTP connect | Select `SecureSocketOptions` based on port | Low |
| F5 | Redundant `SaveChangesAsync` in `BookAppointmentEndpoint` | LOW | `BookAppointmentEndpoint.cs` L179–184 | Collapse into single conditional save | Low |

---

## Appendix

### Search Evidence

| Pattern | Match |
|---|---|
| `SendEmailReminderJob` in `tests/` | No matches |
| `CancelByLink` in `tests/` | No matches |
| `CancellationLinkTokenHash` in `ApplicationDbContext.cs` | No `HasIndex` — no index |
| `CancellationLinkTokenHash` in migration | Column added; no `CreateIndex` call |
| `SecureSocketOptions` in `SendEmailReminderJob.cs` | `SslOnConnect` only — no STARTTLS branch |
| `SaveChangesAsync` in `BookAppointmentEndpoint.cs` | 4 calls total; last two are redundant on the same entity |
| `EmailReminderJobId` in `BookAppointmentEndpoint.cs` | Scheduled at T-48h; ID stored if non-null |
| `EmailReminderJobId` in `CancelAppointmentEndpoint.cs` | `ChangeState(DeletedState)` if non-null |
| `EmailReminderJobId` in `RescheduleAppointmentEndpoint.cs` | Delete + re-schedule + persist |
| `EmailReminderJobId` in `CancelByLinkEndpoint.cs` | `ChangeState(DeletedState)` if non-null |
