# Bug Fix Task - BUG_002

## Bug Report Reference

- **Bug ID**: BUG_002
- **Source**: Developer report — registration flow manual QA
- **Short name**: `registration-email-not-sent`

---

## Bug Summary

### Issue Classification

- **Priority**: High
- **Severity**: Core feature completely broken — all new patient registrations receive no verification email
- **Affected Version**: Current HEAD (since US_026 was merged without removing stub)
- **Environment**: All environments; production and development alike

### Steps to Reproduce

1. Start the API with SMTP env vars configured (`SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`, `SMTP_PASS`, `SMTP_FROM_ADDRESS`).
2. Navigate to the frontend registration page (`/register`).
3. Fill in valid registration details with a real email address.
4. Submit the form.
5. **Expected**: HTTP 201 returned; verification email arrives at the provided address within 60 seconds.
6. **Actual**: HTTP 201 returned; "Check your inbox" overlay shown; **no email is ever received** (not in inbox, not in spam).

**Error Output**:
```text
No exception thrown — no error logged — email silently discarded.
NoOpEmailService.SendAsync returns Task.CompletedTask without any SMTP activity.
```

### Root Cause Analysis

- **File**: `src/ClinicalHealthcare.Api/Program.cs:130`
- **Component**: DI service registration (email)
- **Function**: Top-level builder configuration
- **Cause**: `Program.cs` registers `NoOpEmailService` as the singleton `IEmailService` globally, with the comment "stub — full MailKit wired in US_026". This stub was intended as a temporary placeholder. US_026 was subsequently implemented (`MailKitEmailService` fully built), but the stub registration was never removed. Because `RegisterEndpoint.AddServices()` contains the guard `if (!services.Any(d => d.ServiceType == typeof(IEmailService)))`, the guard evaluates `false` (stub already registered) and `MailKitEmailService` is **never registered**. At runtime, `emailService.SendAsync(...)` calls `NoOpEmailService.SendAsync` which returns `Task.CompletedTask` immediately — no SMTP connection is made, no email is sent. The registration handler completes without exception and returns HTTP 201, causing the frontend to correctly show the success overlay, masking the failure entirely.

**Why not caught earlier**: Unit tests inject `FakeEmailService` directly into the handler, bypassing the `Program.cs` DI registration. No integration test exercises the production DI composition root. The endpoint returns 201 with no observable error, so monitoring and logs show no anomaly.

**All affected email paths (same root cause):**
- `POST /auth/register` — verification email
- `POST /auth/forgot-password` — password reset link
- `POST /admin/users` — admin-created user welcome email
- `SwapMonitorJob` — swap offer notifications

### Impact Assessment

- **Affected Features**: Patient self-registration (email verification), forgot-password flow, admin user creation, swap-offer notifications
- **User Impact**: 100% of new registrations — users can never verify their email, `IsActive` remains `false`, account is unusable
- **Data Integrity Risk**: No — `UserAccount` rows are created correctly; only email delivery is broken
- **Security Implications**: Low — `IsActive=false` accounts with valid (but undelivered) tokens accumulate in the database. Tokens expire after 24 hours per the existing logic. No additional attack surface is introduced by this bug.

---

## Fix Overview

Remove the stale `NoOpEmailService` singleton registration from `Program.cs` and replace it with `MailKitEmailService`. This single-line change restores the production DI binding that was intended since US_026 was completed.

No handler or frontend changes are required — the fix is entirely in the DI composition root.

**Option selected**: Replace `NoOpEmailService` with `MailKitEmailService` in `Program.cs`. `[SOURCE:INFERRED]` — The code comment `"stub — full MailKit wired in US_026"` and the existence of `MailKitEmailService` as a complete implementation make this the unambiguous intended state. `Basis: Program.cs comment + RegisterEndpoint.AddServices guard design.`

---

## Fix Dependencies

- SMTP environment variables must be configured in the target runtime environment:
  - `SMTP_HOST`, `SMTP_PORT`, `SMTP_USER`, `SMTP_PASS`, `SMTP_FROM_ADDRESS`
  - Optional: `SMTP_FROM_NAME` (defaults to `"ClinicalHub"`)
- For local development without a real SMTP server, use a local mail catcher (e.g., [Mailpit](https://mailpit.axllent.org/) or [MailHog](https://github.com/mailhog/MailHog)) — **do not hardcode credentials in source files**.

---

## Impacted Components

### Backend (C# / ASP.NET Core)

- `src/ClinicalHealthcare.Api/Program.cs` — MODIFY — replace `NoOpEmailService` with `MailKitEmailService` in the singleton DI registration

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | `src/ClinicalHealthcare.Api/Program.cs` | Replace `AddSingleton<IEmailService, NoOpEmailService>()` with `AddSingleton<IEmailService, MailKitEmailService>()`; update inline comment |

---

## Implementation Plan

1. Open `src/ClinicalHealthcare.Api/Program.cs`.
2. Locate line `builder.Services.AddSingleton<IEmailService, NoOpEmailService>();` (currently line ~130).
3. Replace `NoOpEmailService` with `MailKitEmailService`.
4. Update the comment from `"// stub — full MailKit wired in US_026"` to `"// MailKit SMTPS — credentials from env vars SMTP_HOST/PORT/USER/PASS/FROM_ADDRESS"`.
5. Build the solution and run all tests.

---

## Regression Prevention Strategy

- [x] Existing unit test `Register_SendsVerificationEmail_ToCorrectAddress` in `RegisterEndpointTests.cs` already asserts `fakeEmail.LastToEmail == "eve@test.com"` and `fakeEmail.LastSubject` contains `"Verify"` — confirms the handler calls `emailService.SendAsync` once per registration
- [ ] Verify `dotnet test` passes (720 existing tests)
- [ ] Manual smoke test: register with a real email address on a dev environment configured with Mailpit; confirm delivery

---

## Rollback Procedure

1. **Detection**: Email delivery fails again (no emails received after registration).
2. **Revert**: In `Program.cs`, change `MailKitEmailService` back to `NoOpEmailService` — this re-enables the no-op stub and restores the 201 response without email delivery (reverts to pre-fix behaviour).
3. **Data recovery**: None required — no data was corrupted. `UserAccount` rows created during the broken period can be manually activated via DB update if needed: `UPDATE UserAccounts SET IsActive=1, VerificationTokenHash=NULL, VerificationTokenExpiry=NULL WHERE IsActive=0`.

---

## External References

- `NoOpEmailService`: `src/ClinicalHealthcare.Infrastructure/Email/NoOpEmailService.cs`
- `MailKitEmailService`: `src/ClinicalHealthcare.Infrastructure/Email/MailKitEmailService.cs`
- Registration handler: `src/ClinicalHealthcare.Api/Features/Auth/RegisterEndpoint.cs`
- TASK_012 original spec: `.propel/context/tasks/EP-001/us_012/task_012_patient-registration-email-verification.md`
- MailKit docs: https://github.com/jstedfast/MailKit

---

## Build Commands

```bash
cd d:/BRD-Healthcare/Clinical-Healthcare
dotnet build src/ClinicalHealthcare.Api/ClinicalHealthcare.Api.csproj --configuration Release
dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --configuration Release --logger "console;verbosity=quiet"
```

---

## Implementation Validation Strategy

- [ ] `dotnet build` exits 0 — no compile errors
- [ ] `dotnet test` 720/720 pass — no regressions
- [ ] Bug no longer reproducible: `POST /auth/register` with SMTP configured → email arrives within 60 seconds
- [ ] `ForgotPassword`, `CreateUser`, and `SwapMonitorJob` email paths are also restored (side-benefit)

---

## Implementation Checklist

- [x] `Program.cs` — replace `NoOpEmailService` with `MailKitEmailService` in singleton registration
- [x] `Program.cs` — update inline comment to remove stale US_026 stub reference
- [x] `dotnet build` passes — 0 errors
- [x] `dotnet test` passes — 720/720 green
