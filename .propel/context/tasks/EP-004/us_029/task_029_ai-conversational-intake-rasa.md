# Task - TASK_029

## Requirement Reference

- **User Story**: US_029 — AI conversational intake via Rasa
- **Story Location**: `.propel/context/tasks/EP-004/us_029/us_029.md`
- **Parent Epic**: EP-004

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `POST /intake/ai/start` proxies to Rasa `localhost:5005`; returns conversation session ID |
| AC-002 | Redis session TTL=900s maintained per conversation; extends on each message |
| AC-003 | Low-confidence response (<0.70) triggers clarification question, not a field commitment |
| AC-004 | Patient can switch to manual intake; confirmed fields are preserved in the `IntakeRecord` |
| AC-005 | Rasa unavailable → 503 `{"error":"AI intake service unavailable"}` |
| AC-006 | Completed AI intake creates `IntakeRecord` with `Source=AI` |

### Edge Cases

- Rasa returns HTTP 5xx → catch, return 503 (no bypass)
- Partial session data preserved in Redis even if patient disconnects; resumes on next request within TTL

---

## Design References

N/A — UI Impact: No

---

## AI References

- **AI Platform**: Rasa Open Source 3.x
- **Endpoint**: `POST http://localhost:5005/webhooks/rest/webhook`
- **Confidence Threshold**: 0.70

---

## Mobile References

N/A — Mobile Impact: No

---

## Applicable Technology Stack

| Layer | Technology | Version | Justification |
|-------|-----------|---------|---------------|
| Backend | ASP.NET Core Web API | 8 LTS | Endpoint hosting |
| Backend | Rasa Open Source | 3.x | NLU conversational intake per design.md |
| Infrastructure | Upstash Redis | N/A | Session state TTL=900s per design.md |
| Backend | HttpClient | 8.x (built-in) | HTTP proxy to Rasa |

---

## Task Overview

Implement AI intake endpoints: `POST /intake/ai/start`, `POST /intake/ai/message`, `POST /intake/ai/complete`, and `POST /intake/ai/switch-to-manual`. Proxy messages to Rasa, maintain Redis session state, enforce confidence threshold, preserve confirmed fields on switch-to-manual.

---

## Dependent Tasks

- **TASK_001 (us_004)** — Redis `ICacheService`
- **TASK_001 (us_008)** — `IntakeRecord` entity
- **TASK_001 (us_030)** — Manual intake (switch-to-manual target)

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Intake/StartAiIntakeEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Intake/SendAiMessageEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Intake/CompleteAiIntakeEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Intake/SwitchToManualEndpoint.cs`
- `src/ClinicalHealthcare.Infrastructure/AI/RasaIntakeService.cs`

---

## Implementation Plan

1. Create `RasaIntakeService`: wraps `HttpClient` to `RASA_URL` (env var, default `http://localhost:5005`); `SendMessageAsync(sessionId, message)` → returns `{text, confidence}`; on HTTP error → throw `RasaUnavailableException`.
2. Implement `POST /intake/ai/start` (`[Authorize(Roles="Patient")]`): generate session ID; store empty `IntakeSession` in Redis with TTL=900s; call Rasa greeting; return `{sessionId}`.
3. Implement `POST /intake/ai/message` (`[Authorize(Roles="Patient")]`): load session from Redis; send message to Rasa; if `confidence < 0.70` → return clarification text (don't commit field); else add field to `confirmedFields` in Redis; reset TTL; return Rasa response.
4. Implement `POST /intake/ai/complete` (`[Authorize(Roles="Patient")]`): load `confirmedFields` from Redis; create `IntakeRecord` with `Source=AI`; delete Redis session; return 201.
5. Implement `POST /intake/ai/switch-to-manual` (`[Authorize(Roles="Patient")]`): load `confirmedFields`; return them as pre-filled form fields; delete AI session. (Patient completes via `POST /intake/manual`.)
6. Catch `RasaUnavailableException` → return 503.
7. Register `RasaIntakeService` + `IHttpClientFactory` in DI.

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Intake/
└── README.md
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/Intake/StartAiIntakeEndpoint.cs` | POST /intake/ai/start |
| CREATE | `src/ClinicalHealthcare.Api/Features/Intake/SendAiMessageEndpoint.cs` | POST /intake/ai/message |
| CREATE | `src/ClinicalHealthcare.Api/Features/Intake/CompleteAiIntakeEndpoint.cs` | POST /intake/ai/complete |
| CREATE | `src/ClinicalHealthcare.Api/Features/Intake/SwitchToManualEndpoint.cs` | POST /intake/ai/switch-to-manual |
| CREATE | `src/ClinicalHealthcare.Infrastructure/AI/RasaIntakeService.cs` | Rasa HTTP proxy |
| MODIFY | `src/ClinicalHealthcare.Api/Program.cs` | Register RasaIntakeService + named HttpClient |

---

## External References

- [Rasa REST API](https://rasa.com/docs/rasa/http-api)
- [StackExchange.Redis](https://stackexchange.github.io/StackExchange.Redis/)

---

## Build Commands

```bash
dotnet build
```

---

## Implementation Validation Strategy

- `POST /intake/ai/start` → session ID returned; Redis key exists with TTL ~900s.
- Low-confidence response → clarification returned; no field committed.
- Rasa down → 503 returned.
- `POST /intake/ai/switch-to-manual` → confirmed fields returned; Redis session deleted.
- `POST /intake/ai/complete` → `IntakeRecord` in DB with `Source=AI`.

---

## Implementation Checklist

- [x] **[AC-001]** `POST /intake/ai/start` proxies to Rasa; returns session ID
- [x] **[AC-002]** Redis session TTL=900s; reset on each message
- [x] **[AC-003]** Confidence < 0.70 → clarification; field not committed
- [x] **[AC-004]** `POST /intake/ai/switch-to-manual` preserves confirmed fields
- [x] **[AC-005]** Rasa unavailable → 503 (no bypass)
- [x] **[AC-006]** `POST /intake/ai/complete` creates `IntakeRecord` with `Source=AI`
- [x] `RASA_URL` from env var; default `http://localhost:5005`
- [x] `dotnet build` passes with 0 errors
