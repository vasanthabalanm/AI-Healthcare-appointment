# Task - TASK_025

## Requirement Reference

- **User Story**: US_025 — Microsoft Outlook Calendar OAuth2 PKCE sync
- **Story Location**: `.propel/context/tasks/EP-003/us_025/us_025.md`
- **Parent Epic**: EP-003

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `POST /appointments/{id}/calendar-sync {provider:"microsoft"}` initiates Microsoft OAuth2 PKCE flow |
| AC-002 | Microsoft Graph scope `Calendars.ReadWrite` requested |
| AC-003 | Calendar event created via `POST /me/events` on Microsoft Graph API v1.0 |
| AC-004 | Idempotency check before event creation: skip if `CalendarEventId` already stored |

### Edge Cases

- Microsoft Graph returns 409 (event already exists with same ID) → treat as success; update stored `CalendarEventId`
- Token refresh fails → clear stored tokens; return 401 to prompt re-auth

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
| Backend | Microsoft Graph API | v1.0 | Outlook calendar sync per design.md |
| Backend | AES-256-CBC | .NET 8 BCL | Token encryption at rest per design.md |
| Database | SQL Server | 2022 / Express | Encrypted token storage |

---

## Task Overview

Implement Microsoft Outlook Calendar OAuth2 PKCE flow: initiate endpoint, callback endpoint, Microsoft Graph event creation. Re-uses `CalendarToken` entity from us_024. Idempotency check before event creation.

---

## Dependent Tasks

- **TASK_001 (us_024)** — `CalendarToken` entity, AES-256-CBC encryption pattern, PKCE helpers

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Appointments/MicrosoftCalendarCallbackEndpoint.cs`
- `src/ClinicalHealthcare.Infrastructure/Calendar/MicrosoftCalendarService.cs`

---

## Implementation Plan

1. Implement `POST /appointments/{id}/calendar-sync {provider:"microsoft"}` (`[Authorize(Roles="Patient")]`): generate PKCE; sign state with HMAC; redirect to Microsoft identity platform OAuth2 URL with `scope=Calendars.ReadWrite offline_access`.
2. Implement `GET /auth/microsoft/callback`: verify HMAC state; exchange code+verifier for tokens; encrypt with AES-256-CBC; store in `CalendarToken` table; check idempotency via `CalendarEventId`.
3. Create `MicrosoftCalendarService`: HTTP client calling `https://graph.microsoft.com/v1.0/me/events` with Bearer token; create event from `Appointment`; store returned `Id` as `CalendarEventId`.
4. Token refresh: if access token expired, use refresh token to get new access token; re-encrypt and store; if refresh fails → clear tokens.
5. Register `MicrosoftCalendarService` in DI.

---

## Current Project State

```
src/ClinicalHealthcare.Infrastructure/Calendar/
└── GoogleCalendarService.cs
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/Appointments/MicrosoftCalendarCallbackEndpoint.cs` | GET /auth/microsoft/callback |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Calendar/MicrosoftCalendarService.cs` | Microsoft Graph API integration |
| MODIFY | `src/ClinicalHealthcare.Api/Features/Appointments/CalendarSyncEndpoint.cs` | Branch on provider=microsoft |

---

## External References

- [Microsoft Graph Calendar API](https://learn.microsoft.com/en-us/graph/api/user-post-events)
- [Microsoft Identity Platform OAuth2 PKCE](https://learn.microsoft.com/en-us/azure/active-directory/develop/v2-oauth2-auth-code-flow)

---

## Build Commands

```bash
dotnet build
```

---

## Implementation Validation Strategy

- OAuth2 redirect URL targets `login.microsoftonline.com` with `scope=Calendars.ReadWrite`.
- Callback stores encrypted tokens; event created in Outlook.
- Second sync → idempotency: no duplicate event.
- Expired token → refresh used; re-encrypted and stored.
- `dotnet build` → 0 errors.

---

## Implementation Checklist

- [x] **[AC-001]** `POST /calendar-sync {provider:"microsoft"}` initiates Microsoft PKCE flow
- [x] **[AC-002]** `Calendars.ReadWrite offline_access` scope requested
- [x] **[AC-003]** Calendar event created via `POST /me/events` on Graph API v1.0
- [x] **[AC-004]** Idempotency check: skip event creation if `CalendarEventId` already stored
- [x] Token refresh implemented; failure clears tokens and returns 401
- [x] Tokens encrypted AES-256-CBC before DB storage (same key as us_024)
- [x] `CalendarSyncEndpoint` branches on `provider` field
- [x] `dotnet build` passes with 0 errors
