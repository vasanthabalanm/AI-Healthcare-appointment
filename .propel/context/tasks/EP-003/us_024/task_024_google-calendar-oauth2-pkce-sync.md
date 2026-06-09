# Task - TASK_024

## Requirement Reference

- **User Story**: US_024 — Google Calendar OAuth2 PKCE sync
- **Story Location**: `.propel/context/tasks/EP-003/us_024/us_024.md`
- **Parent Epic**: EP-003

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `POST /appointments/{id}/calendar-sync {provider:"google"}` initiates OAuth2 PKCE flow |
| AC-002 | PKCE code challenge uses S256 method |
| AC-003 | OAuth2 state parameter is signed (HMAC) to prevent CSRF |
| AC-004 | OAuth2 tokens stored AES-256-CBC encrypted in SQL Server |

### Edge Cases

- Duplicate calendar event creation (re-sync) → idempotency check: if event already exists (stored `CalendarEventId`), skip creation
- OAuth callback with invalid/expired state → 400

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
| Backend | Google Calendar API | v3 | Google calendar sync per design.md |
| Backend | AES-256-CBC | .NET 8 BCL | Token encryption at rest per design.md |
| Database | SQL Server | 2022 / Express | Encrypted token storage |

---

## Task Overview

Implement Google Calendar OAuth2 PKCE flow: initiate endpoint, callback endpoint, token storage (AES-256-CBC encrypted), and Google Calendar event creation for the appointment.

---

## Dependent Tasks

- **TASK_001 (us_007)** — `Appointment` entity
- **TASK_001 (us_039)** — AES-256-CBC key management pattern

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Appointments/CalendarSyncEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Appointments/GoogleCalendarCallbackEndpoint.cs`
- `src/ClinicalHealthcare.Infrastructure/Calendar/GoogleCalendarService.cs`
- `src/ClinicalHealthcare.Infrastructure/Entities/CalendarToken.cs`

---

## Implementation Plan

1. Create `CalendarToken` entity: `Id`, `PatientId`, `Provider` (Google/Microsoft), `EncryptedAccessToken`, `EncryptedRefreshToken`, `CalendarEventId`, `ExpiresAt`; add to `ApplicationDbContext`; create migration.
2. Implement `POST /appointments/{id}/calendar-sync` (`[Authorize(Roles="Patient")]`): generate PKCE `code_verifier` (random 43–128 chars) and `code_challenge = BASE64URL(SHA256(code_verifier))`; sign state param with HMAC-SHA256 using `CALENDAR_STATE_SECRET` env var; redirect to Google OAuth2 URL.
3. Implement `GET /auth/google/callback`: verify HMAC state; exchange code for tokens using `code_verifier`; encrypt tokens with AES-256-CBC (`CLINICAL_AES_KEY` env var); store in `CalendarToken` table; check `CalendarEventId` for idempotency; call Google Calendar API to create event; store `CalendarEventId`; return 200.
4. Create `GoogleCalendarService` using `Google.Apis.Calendar.v3` NuGet; create event DTO from `Appointment` details.
5. All calendar operations are `[Authorize(Roles="Patient")]`.

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Appointments/
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/Appointments/CalendarSyncEndpoint.cs` | POST /appointments/{id}/calendar-sync |
| CREATE | `src/ClinicalHealthcare.Api/Features/Appointments/GoogleCalendarCallbackEndpoint.cs` | GET /auth/google/callback |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Calendar/GoogleCalendarService.cs` | Google Calendar API integration |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Entities/CalendarToken.cs` | Encrypted token storage entity |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs` | Register CalendarToken |
| CREATE | `src/ClinicalHealthcare.Infrastructure.SqlMigrations/Migrations/*_CalendarToken.cs` | Migration |

---

## External References

- [Google Calendar API v3](https://developers.google.com/calendar/api/v3/reference)
- [OAuth2 PKCE RFC 7636](https://datatracker.ietf.org/doc/html/rfc7636)
- [AES-256-CBC .NET](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aes)

---

## Build Commands

```bash
dotnet add src/ClinicalHealthcare.Infrastructure package Google.Apis.Calendar.v3
dotnet ef migrations add CalendarToken --project src/ClinicalHealthcare.Infrastructure.SqlMigrations --startup-project src/ClinicalHealthcare.Api --context ApplicationDbContext
dotnet build
```

---

## Implementation Validation Strategy

- OAuth2 redirect URL includes `code_challenge` and `code_challenge_method=S256`.
- Invalid state on callback → 400.
- Tokens stored encrypted in `CalendarToken` table (not plaintext).
- Second sync for same appointment → no duplicate Google event (idempotency via `CalendarEventId`).
- `CLINICAL_AES_KEY` env var used for encryption; not hardcoded.

---

## Implementation Checklist

- [ ] **[AC-001]** `POST /appointments/{id}/calendar-sync` initiates PKCE OAuth2 flow
- [ ] **[AC-002]** PKCE code challenge uses S256 (`BASE64URL(SHA256(code_verifier))`)
- [ ] **[AC-003]** State parameter signed with HMAC-SHA256; validated on callback
- [ ] **[AC-004]** OAuth2 tokens encrypted AES-256-CBC before DB storage
- [ ] Idempotency check: existing `CalendarEventId` → skip event creation
- [ ] `CLINICAL_AES_KEY` sourced from env var; never hardcoded
- [ ] Migration for `CalendarToken` entity created and applied
- [ ] `dotnet build` passes with 0 errors
