# Task - TASK_028

## Requirement Reference

- **User Story**: US_028 — Email appointment reminders + cancellation link
- **Story Location**: `.propel/context/tasks/EP-003/us_028/us_028.md`
- **Parent Epic**: EP-003

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | Email reminder sent via MailKit (SMTPS port 465 or STARTTLS port 587) |
| AC-002 | Reminder email contains a single-use cancellation link valid for 48 hours |
| AC-003 | Cancellation link token is single-use; invalidated on first use |
| AC-004 | Pending reminder email jobs cancelled when appointment is cancelled |

### Edge Cases

- Cancellation link used after appointment already cancelled → 400 `{"error":"Appointment already cancelled"}`
- Expired cancellation link token (>48h) → 400 `{"error":"Cancellation link expired"}`

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
| Backend | MailKit | 4.x | SMTPS/STARTTLS email per design.md |
| Backend | Hangfire | 1.8.x | Scheduled reminder job |
| Database | SQL Server | 2022 / Express | Cancellation token storage |

---

## Task Overview

Implement `SendEmailReminderJob` (Hangfire). Generate a single-use 48-hour cancellation token in the reminder email. Implement `GET /appointments/cancel?token=...` to process link-based cancellation. Cancel pending jobs when appointment is cancelled.

---

## Dependent Tasks

- **TASK_001 (us_012)** — `IEmailService`
- **TASK_001 (us_023)** — Cancellation flow (job cancellation on cancel)
- **TASK_001 (us_004)** — Hangfire

---

## Impacted Components

- `src/ClinicalHealthcare.Infrastructure/Jobs/SendEmailReminderJob.cs`
- `src/ClinicalHealthcare.Api/Features/Appointments/CancelByLinkEndpoint.cs`
- `src/ClinicalHealthcare.Infrastructure/Entities/Appointment.cs` — add `EmailReminderJobId`, cancellation token fields

---

## Implementation Plan

1. Add `EmailReminderJobId`, `CancellationLinkToken`, `CancellationLinkExpiry`, `CancellationLinkUsed` to `Appointment`; create migration.
2. Implement `SendEmailReminderJob.Execute(appointmentId)`: load Appointment + patient; generate cancellation token (`RandomNumberGenerator.GetBytes(32)` → Base64URL); store hash + expiry (`UtcNow.AddHours(48)`); build email body with cancellation link `{BASE_URL}/appointments/cancel?token={token}`; send via MailKit.
3. Implement `GET /appointments/cancel?token=...` (`[AllowAnonymous]`): find Appointment by token hash; validate not used + not expired + status = Scheduled; mark `CancellationLinkUsed=true`; cancel appointment (status=Cancelled, slot released, SwapMonitor triggered); return 200.
4. In cancel flow (us_023): cancel `EmailReminderJobId` via `BackgroundJob.Delete(...)`.
5. Store `EmailReminderJobId` on Appointment when job is scheduled.

---

## Current Project State

```
src/ClinicalHealthcare.Infrastructure/Jobs/
├── SendConfirmationEmailJob.cs
├── SendSmsReminderJob.cs
└── SendReminderJob.cs  (stub)
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Jobs/SendEmailReminderJob.cs` | Replace stub; generate cancellation link token |
| CREATE | `src/ClinicalHealthcare.Api/Features/Appointments/CancelByLinkEndpoint.cs` | GET /appointments/cancel?token= |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Entities/Appointment.cs` | Add cancellation token + email job ID fields |
| CREATE | `src/ClinicalHealthcare.Infrastructure.SqlMigrations/Migrations/*_AppointmentCancellationToken.cs` | Migration |

---

## External References

- [MailKit STARTTLS](https://github.com/jstedfast/MailKit)
- [Single-Use Tokens Pattern](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html)

---

## Build Commands

```bash
dotnet ef migrations add AppointmentCancellationToken --project src/ClinicalHealthcare.Infrastructure.SqlMigrations --startup-project src/ClinicalHealthcare.Api --context ApplicationDbContext
dotnet build
```

---

## Implementation Validation Strategy

- Email received contains valid cancellation link.
- Follow link → appointment cancelled; slot released.
- Follow same link again → 400 (used token).
- Follow link after 48h → 400 (expired).
- Cancel appointment via API → `EmailReminderJobId` deleted from Hangfire.

---

## Implementation Checklist

- [x] **[AC-001]** Reminder email sent via MailKit (SMTPS 465 or STARTTLS 587)
- [x] **[AC-002]** Reminder email contains single-use cancellation link (token valid 48h)
- [x] **[AC-003]** Cancellation token invalidated (`CancellationLinkUsed=true`) on first use
- [x] **[AC-004]** `EmailReminderJobId` stored; deleted from Hangfire on appointment cancellation
- [x] `CancelByLinkEndpoint` validates token not expired, not used, appointment not already cancelled
- [x] Token stored as hash in DB (not plaintext)
- [x] SMTP credentials from env vars
- [x] `dotnet build` passes with 0 errors
