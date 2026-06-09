# Unit Test Plan - US_024

## Requirement Reference
- **User Story**: us_024
- **Story Location**: `.propel/context/tasks/EP-003/us_024/us_024.md`
- **Layer**: BE
- **Related Test Plans**: [US_025 test plan](../../../EP-003/us_025/unittest/test_plan_be_microsoft-calendar-oauth2-pkce.md) — shares `CalendarSyncEndpoint` helpers
- **Acceptance Criteria Covered**:
  - AC-001: OAuth2 PKCE flow initiates and returns redirect URL with `code_challenge`, `code_challenge_method=S256`, and HMAC-signed `state`
  - AC-002: Callback exchanges code for tokens; tokens stored AES-256 encrypted; Google Calendar event created
  - AC-003: Access denied / missing code at callback — implementation returns 400 (spec says 200; discrepancy documented)
  - AC-004: Google Calendar API error returns 502/503

## Test Plan Overview

Covers backend unit tests for two tightly coupled handlers and their PKCE/HMAC/AES helpers in US_024:

1. **`CalendarSyncEndpoint.HandleCalendarSync`** (`POST /appointments/{id}/calendar-sync`) — generates PKCE session, HMAC-signs state, caches PKCE verifier, returns OAuth2 authorization URL.
2. **`GoogleCalendarCallbackEndpoint.HandleGoogleCallback`** (`GET /auth/google/callback`) — validates HMAC state, retrieves PKCE verifier from Redis, exchanges code for tokens, encrypts with AES-256-CBC, persists `CalendarToken`, creates Google Calendar event.
3. **`CalendarSyncEndpoint` PKCE/HMAC helpers** — `GenerateCodeVerifier`, `GenerateCodeChallenge`, `ComputeHmac`, `Base64UrlEncode`.

> **AC-003 Implementation Discrepancy:** US_024 specifies that access denial (`error=access_denied`) at the Google consent screen returns HTTP 200 with a cancellation message. The actual implementation returns HTTP 400 (`"Missing code or state parameter."`) because `HandleGoogleCallback` requires both `code` and `state` query parameters — when Google redirects with `?error=access_denied`, the `code` parameter is absent. EC-001 documents and tests the actual implemented behaviour (400). Product review required.

> **Note:** `CalendarSyncEndpoint` returns HTTP 200 with `{"authorizationUrl":"..."}` (not 302) — the redirect is performed client-side by the SPA after receiving the URL. Tests assert 200 + URL content.

AI Impact: **No** (no AIR-XXX requirements referenced in US_024).

## Dependent Tasks
- EP-TECH US_001–006 (infrastructure foundation — JWT auth, DB context) must be passing.

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `CalendarSyncEndpoint.HandleCalendarSync` | static method | `src/ClinicalHealthcare.Api/Features/Appointments/CalendarSyncEndpoint.cs` | Validate patient; generate PKCE; sign state; cache session; return auth URL |
| `CalendarSyncEndpoint.GenerateCodeChallenge` | static method | same | BASE64URL(SHA256(ASCII(verifier))) — RFC 7636 S256 |
| `CalendarSyncEndpoint.GenerateCodeVerifier` | static method | same | 64-char URL-safe random string — no modulo bias |
| `CalendarSyncEndpoint.ComputeHmac` | static method | same | HMAC-SHA256 of state data using `StateSecret` |
| `GoogleCalendarCallbackEndpoint.HandleGoogleCallback` | static method | `src/ClinicalHealthcare.Api/Features/Appointments/GoogleCalendarCallbackEndpoint.cs` | Validate state HMAC; retrieve PKCE; exchange code; AES-encrypt tokens; create event; idempotency |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | Valid patient + google provider → 200 + authUrl with S256 params | Patient owns appointment; `GoogleClientId` + `StateSecret` configured | `HandleCalendarSync("google")` called | 200 OK; `authorizationUrl` contains `code_challenge_method=S256` and `code_challenge` | `StatusCode == 200`; URL contains `code_challenge_method%3DS256` [SOURCE:INPUT] Basis: AC-001 |
| TC-002 | positive | S256 code_challenge matches RFC 7636 Appendix B known test vector | Verifier = `dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk` | `GenerateCodeChallenge(verifier)` called | Returns `E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM` | `result == "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"` [SOURCE:INPUT] Basis: AC-001 PKCE S256 |
| TC-003 | positive | PKCE code_verifier is 64 chars from `[A-Za-z0-9\-_]` alphabet (no modulo bias) | Random bytes generated | `GenerateCodeVerifier()` called | Length = 64; matches `^[A-Za-z0-9\-_]+$` | `verifier.Length == 64`; `Regex.IsMatch` [SOURCE:INPUT] Basis: AC-001 PKCE RFC 7636 |
| TC-004 | positive | HMAC state is deterministic and sensitive to input changes | `data = "1\|42\|nonce"`; `secret = "test"` | `ComputeHmac(data, secret)` called twice; once with modified data | Same input → same output; modified input → different output | `sig1 == sig2`; `sig1 != sig3` [SOURCE:INPUT] Basis: AC-001 signed state |
| TC-005 | positive | Valid callback flow stores AES-256 encrypted tokens; creates calendar event | DB seeded with appointment; valid HMAC state; PKCE session in cache; services return tokens + eventId | `HandleGoogleCallback(code, state)` called | 200 OK; `CalendarToken` in DB with `EncryptedAccessToken != "access-token"`; `CalendarEventId` set | Token assertions; AES decrypt round-trip [SOURCE:INPUT] Basis: AC-002 |
| TC-006 | positive | Idempotent re-sync skips `CreateEventAsync`; returns existing `CalendarEventId` | `CalendarToken` with `CalendarEventId = "existing-id"` already in DB | `HandleGoogleCallback(code, state)` called | 200 OK; `calendarSvc.CreateEventAsync` called `Times.Never` | `StatusCode == 200`; verify mock `Times.Never` [SOURCE:INPUT] Basis: AC-002 idempotency |
| TC-007 | negative | Tampered HMAC state → 400; token exchange never attempted | State signature modified | `HandleGoogleCallback` called | 400 Bad Request | `StatusCode == 400`; `ExchangeCodeAsync` `Times.Never` [SOURCE:INPUT] Basis: AC-001 CSRF guard |
| TC-008 | negative | Expired/missing PKCE session in Redis → 400 | Cache returns `null` for PKCE key | `HandleGoogleCallback` called | 400 Bad Request | `StatusCode == 400` [SOURCE:INPUT] Basis: AC-001 PKCE TTL |
| EC-001 | edge_case | `code` missing (access_denied redirect) → 400 (implementation); spec says 200 — discrepancy | `code = ""` or missing | `HandleGoogleCallback("", state)` called | 400 Bad Request (actual); Note: AC-003 spec says 200 | `StatusCode == 400` [SOURCE:INFERRED] Basis: handler guard `if (IsNullOrWhiteSpace(code)) → BadRequest` — access_denied not handled separately |
| EC-002 | edge_case | `ExchangeCodeAsync` throws `HttpRequestException` → 502 | Token exchange fails with network error | `HandleGoogleCallback` called | 502 Bad Gateway | `StatusCode == 502` [SOURCE:INFERRED] Basis: catch block `HttpRequestException → Problem(statusCode: 502)` |
| ES-001 | error | Unsupported provider → 400 | `provider = "yahoo"` | `HandleCalendarSync` called | 400 Bad Request | `StatusCode == 400` [SOURCE:INFERRED] Basis: `!isGoogle && !isMicrosoft → BadRequest` |

## AI Component Test Cases

> **AI Impact: No** — US_024 contains no AIR-XXX requirements. This section is skipped.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| EXISTS | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/CalendarSyncEndpointTests.cs` | Tests for `POST /appointments/{id}/calendar-sync` Google provider (TC-001–004, ES-001 covered) |
| EXISTS | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/GoogleCalendarCallbackEndpointTests.cs` | Tests for `GET /auth/google/callback` (TC-005–008, EC-001, EC-002 covered) |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ApplicationDbContext` | EF Core In-Memory | `UseInMemoryDatabase(Guid.NewGuid().ToString())` + `TransactionIgnoredWarning` suppressed | Full in-memory DB per test |
| `ICacheService` | `Mock<ICacheService>` | `SetAsync` → `Task.CompletedTask`; `GetAsync<PkceSession>(key)` → seeded `PkceSession` or `null`; `DeleteAsync` → `Task.CompletedTask` | Configurable per test |
| `IGoogleCalendarService` | `Mock<IGoogleCalendarService>` | `ExchangeCodeAsync(...)` returns `TokenExchangeResult("access-token-123", "refresh-token-456", UtcNow+1h)`; `CreateEventAsync(...)` returns `"google-event-id-001"` | `ReturnsAsync(...)` |
| `IOptions<CalendarSettings>` | `Options.Create(...)` | `GoogleClientId = "test-client-id"`, `StateSecret = "test-state-secret-min-32-chars-ok!"`, `AesKey = "test-aes-key"`, `GoogleRedirectUri = "http://localhost/callback"` | N/A |
| `HttpContext` | `DefaultHttpContext` | Injects `ClaimsPrincipal` with `sub = patientId.ToString()` | N/A |

## AI Mocking Strategy

> **AI Impact: No** — Skipped.

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Valid Google initiation | Patient owns appointment; `GoogleClientId` + `StateSecret` set | 200 + authUrl with `code_challenge_method=S256` |
| S256 RFC 7636 known vector | verifier = `dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk` | challenge = `E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM` |
| Valid callback | Valid HMAC state; PKCE session in cache; `ExchangeCodeAsync` returns tokens | 200; `CalendarToken` with AES-encrypted tokens |
| Idempotent resync | `CalendarToken.CalendarEventId` already set | 200; `CreateEventAsync` not called |
| Tampered HMAC | `state` signature suffix modified | 400 Bad Request |
| Expired PKCE session | Cache returns `null` for `pkce:{nonce}` key | 400 Bad Request |
| Token exchange error | `ExchangeCodeAsync` throws `HttpRequestException` | 502 Bad Gateway |
| Unsupported provider | `provider = "yahoo"` | 400 Bad Request |

## Test Commands

- **Run Tests**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --filter "FullyQualifiedName~CalendarSyncEndpointTests|FullyQualifiedName~GoogleCalendarCallbackEndpointTests"`
- **Run with Coverage**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --collect:"XPlat Code Coverage" --results-directory ./coverage`
- **Run Single Test**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --filter "FullyQualifiedName~HandleGoogleCallback_ValidFlow_StoresEncryptedTokens"`

## Coverage Target

- **Line Coverage**: 88%
- **Branch Coverage**: 85%
- **Critical Paths**:
  - `HandleCalendarSync` — provider validation guard; missing config guard; wrong patient guard; PKCE + HMAC generation
  - `HandleGoogleCallback` — HMAC validation (constant-time compare); PKCE session retrieval (hit/miss); token exchange exception; idempotency check; AES-CBC encryption round-trip

## Documentation References

- **Framework Docs**: [xUnit 2.x — https://xunit.net](https://xunit.net)
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/CalendarSyncEndpointTests.cs`
- **Mocking Guide**: [Moq 4.x — https://github.com/devlooped/moq](https://github.com/devlooped/moq)
- **RFC 7636 PKCE**: https://datatracker.ietf.org/doc/html/rfc7636

## Implementation Checklist

- [x] Create test file structure per Expected Changes
- [x] Set up test data fixtures per Test Data section
- [x] Configure mocking dependencies per Mocking Strategy
- [x] Implement positive test cases (TC-001 – TC-006)
- [x] Implement negative test cases (TC-007, TC-008)
- [x] Implement edge case tests (EC-001, EC-002)
- [x] Implement error scenario tests (ES-001)
- [x] Run test suite and validate coverage meets target
