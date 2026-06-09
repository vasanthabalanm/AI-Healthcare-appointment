# Task - TASK_027

## Requirement Reference

- **User Story**: US_027 — SMS appointment reminders (T-48h + T-2h)
- **Story Location**: `.propel/context/tasks/EP-003/us_027/us_027.md`
- **Parent Epic**: EP-003

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | SMS reminders sent at T-48h and T-2h before appointment |
| AC-002 | Phone number normalized to E.164 format before sending |
| AC-003 | If patient has no phone number, SMS is gracefully skipped (no error) |
| AC-004 | `ISmsGateway` abstraction used; Twilio/Vonage sandbox for testing |
| AC-005 | Pending SMS jobs cancelled when appointment is cancelled |

### Edge Cases

- Invalid phone number format → log WARNING; skip SMS; no error thrown
- SMS gateway returns error → Hangfire retries 3× (inherits global policy)

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
| Backend | Hangfire | 1.8.x | Scheduled SMS jobs per design.md |
| Backend | ISmsGateway (abstraction) | N/A | SMS abstraction per design.md |

---

## Task Overview

Implement `ISmsGateway` abstraction with a Twilio/Vonage sandbox implementation. Implement `SendSmsReminderJob` scheduled at T-48h and T-2h. Normalize phone numbers to E.164. Gracefully skip if no phone. Hang job IDs on `Appointment` for cancellation.

---

## Dependent Tasks

- **TASK_001 (us_004)** — Hangfire infrastructure
- **TASK_001 (us_019)** — Appointment booking enqueues reminder jobs
- **TASK_001 (us_023)** — Cancel flow cancels job by stored ID

---

## Impacted Components

- `src/ClinicalHealthcare.Infrastructure/Sms/ISmsGateway.cs`
- `src/ClinicalHealthcare.Infrastructure/Sms/TwilioSandboxSmsGateway.cs`
- `src/ClinicalHealthcare.Infrastructure/Jobs/SendSmsReminderJob.cs`
- `src/ClinicalHealthcare.Infrastructure/Entities/Appointment.cs` — add `SmsReminderJobId48h`, `SmsReminderJobId2h`

---

## Implementation Plan

1. Create `ISmsGateway` interface: `SendAsync(string toE164, string body)`.
2. Create `TwilioSandboxSmsGateway` implementing `ISmsGateway`; reads `TWILIO_ACCOUNT_SID`, `TWILIO_AUTH_TOKEN`, `TWILIO_FROM_NUMBER` from env vars; sends via Twilio REST API.
3. Create `PhoneNormalizer` utility: parse and normalize to E.164 using `libphonenumber-csharp`; return null if invalid.
4. Implement `SendSmsReminderJob.Execute(appointmentId, reminderLabel)`: load Appointment + patient; normalize phone; if null/empty → log WARNING, return; send SMS via `ISmsGateway`.
5. In `BookAppointmentEndpoint` (us_019): schedule two jobs and store IDs on `Appointment`:
   - T-48h: `BackgroundJob.Schedule<SendSmsReminderJob>(j => j.Execute(id, "T-48h"), slotTime.AddHours(-48))`
   - T-2h: `BackgroundJob.Schedule<SendSmsReminderJob>(j => j.Execute(id, "T-2h"), slotTime.AddHours(-2))`
6. Add `SmsReminderJobId48h` + `SmsReminderJobId2h` fields to `Appointment`; create migration.
7. Register `ISmsGateway` → `TwilioSandboxSmsGateway` in DI.

---

## Current Project State

```
src/ClinicalHealthcare.Infrastructure/Jobs/
├── SendConfirmationEmailJob.cs
└── SendReminderJob.cs  (stub)
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Infrastructure/Sms/ISmsGateway.cs` | SMS abstraction |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Sms/TwilioSandboxSmsGateway.cs` | Twilio sandbox implementation |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Sms/PhoneNormalizer.cs` | E.164 normalization utility |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Jobs/SendSmsReminderJob.cs` | Hangfire SMS reminder job |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Entities/Appointment.cs` | Add SMS job ID fields |
| CREATE | `src/ClinicalHealthcare.Infrastructure.SqlMigrations/Migrations/*_AppointmentSmsJobIds.cs` | Migration |
| MODIFY | `src/ClinicalHealthcare.Api/Program.cs` | Register ISmsGateway |

---

## External References

- [libphonenumber-csharp](https://github.com/twcclegg/libphonenumber-csharp)
- [Twilio SMS API](https://www.twilio.com/docs/sms)

---

## Build Commands

```bash
dotnet add src/ClinicalHealthcare.Infrastructure package libphonenumber-csharp
dotnet ef migrations add AppointmentSmsJobIds --project src/ClinicalHealthcare.Infrastructure.SqlMigrations --startup-project src/ClinicalHealthcare.Api --context ApplicationDbContext
dotnet build
```

---

## Implementation Validation Strategy

- Patient with valid phone → SMS sent at scheduled time via Twilio sandbox.
- Patient with no phone → job completes silently (no exception in Hangfire).
- Invalid phone format → WARNING logged; job completes.
- Cancel appointment → `BackgroundJob.Delete(SmsReminderJobId48h)` and `Delete(SmsReminderJobId2h)`.
- `dotnet build` → 0 errors.

---

## Implementation Checklist

- [x] **[AC-001]** SMS scheduled at T-48h and T-2h via Hangfire
- [x] **[AC-002]** Phone number normalized to E.164 before sending
- [x] **[AC-003]** No phone number → graceful skip (WARNING logged, no exception)
- [x] **[AC-004]** `ISmsGateway` abstraction; `TwilioSandboxSmsGateway` implementation
- [x] **[AC-005]** Job IDs stored on `Appointment`; cancelled when appointment cancelled
- [x] `TWILIO_*` env vars used (no hardcoded credentials)
- [x] Migration for `SmsReminderJobId48h` + `SmsReminderJobId2h` fields
- [x] `dotnet build` passes with 0 errors
