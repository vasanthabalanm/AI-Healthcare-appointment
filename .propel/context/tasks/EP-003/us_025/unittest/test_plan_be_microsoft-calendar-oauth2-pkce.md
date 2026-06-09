# Unit Test Plan - US_025

## Requirement Reference
- **User Story**: us_025
- **Story Location**: `.propel/context/tasks/EP-003/us_025/us_025.md`
- **Layer**: BE
- **Related Test Plans**: [US_024 test plan](../../../EP-003/us_024/unittest/test_plan_be_google-calendar-oauth2-pkce.md) — shares `CalendarSyncEndpoint` PKCE/HMAC helpers and session logic
- **Acceptance Criteria Covered**:
  - AC-001: Microsoft OAuth2 PKCE flow initiates and returns auth URL with `code_challenge`, `code_challenge_method=S256`, `Calendars.ReadWrite offline_access` scope
  - AC-002: Callback exchanges code for tokens via Microsoft Identity; tokens stored AES-256 encrypted
  - AC-003: Access denied / missing code at callback — implementation returns 400 (spec says 200; discrepancy documented)
  - AC-004: Event created via Microsoft Graph; idempotency prevents duplicate events; Graph 409 treated as success; Graph 401 triggers token refresh

## Test Plan Overview

Covers backend unit tests for the Microsoft Outlook Calendar OAuth2 PKCE flow in US_025:

1. **`CalendarSyncEndpoint.HandleCalendarSync`** (`POST /appointments/{id}/calendar-sync`) — Microsoft provider branch; generates PKCE session, HMAC-signs state, returns Microsoft OAuth2 authorization URL with `Calendars.ReadWrite offline_access` scope.
2. **`MicrosoftCalendarCallbackEndpoint.HandleMicrosoftCallback`** (`GET /auth/microsoft/callback`) — validates HMAC state, retrieves PKCE verifier from Redis, exchanges code for tokens via Microsoft Identity Platform, encrypts with AES-256-CBC, persists `CalendarToken` (Provider=`"Microsoft"`), creates event via `IMicrosoftCalendarService`.

> **AC-003 Implementation Discrepancy:** US_025 specifies that access denial at the Microsoft consent screen returns HTTP 200 with a cancellation message. The actual implementation returns HTTP 400 (`"Missing code or state parameter."`) when `code` is absent — Microsoft redirects with `?error=access_denied` without a `code` parameter, so the handler guard fires. EC-001 documents and tests the actual implemented behaviour (400). Product review required.

> **Shared infrastructure:** `GenerateCodeVerifier`, `GenerateCodeChallenge`, `ComputeHmac`, and `Base64UrlEncode` are tested in US_024 test plan. This plan cross-references those results rather than duplicating helper tests.

AI Impact: **No** (no AIR-XXX requirements referenced in US_025).

## Dependent Tasks
- EP-TECH US_001–006 (infrastructure foundation) must be passing.
- US_024 test plan helpers (`GenerateCodeChallenge`, `ComputeHmac`) assumed passing.

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `CalendarSyncEndpoint.HandleCalendarSync` (MS branch) | static method | `src/ClinicalHealthcare.Api/Features/Appointments/CalendarSyncEndpoint.cs` | Microsoft provider guard; PKCE + HMAC; `Calendars.ReadWrite` scope; `tenantId` in URL |
| `MicrosoftCalendarCallbackEndpoint.HandleMicrosoftCallback` | static method | `src/ClinicalHealthcare.Api/Features/Appointments/MicrosoftCalendarCallbackEndpoint.cs` | HMAC state validation; PKCE retrieval; MS token exchange (6-param); AES-encrypt; create Graph event; Graph 409→success; Graph 401→refresh |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | Valid patient + `provider="microsoft"` → 200 + authUrl with `Calendars.ReadWrite` scope | Patient owns appointment; `MicrosoftClientId` + `StateSecret` + `TenantId` configured | `HandleCalendarSync("microsoft")` called | 200 OK; authUrl contains `Calendars.ReadWrite` and `code_challenge_method=S256` | `StatusCode == 200`; URL `Contains("Calendars.ReadWrite")` [SOURCE:INPUT] Basis: AC-001 |
| TC-002 | positive | Microsoft auth URL includes `tenantId` in authorization endpoint | `TenantId = "tenant-guid-123"` in settings | `HandleCalendarSync("microsoft")` called | authUrl contains the tenant ID path segment | `URL.Contains("tenant-guid-123")` [SOURCE:INFERRED] Basis: AC-001 MS endpoint format |
| TC-003 | positive | Valid callback — tokens encrypted, CalendarToken Provider=`"Microsoft"` stored, event created | Valid HMAC state; PKCE session in cache; `ExchangeCodeAsync` returns tokens | `HandleMicrosoftCallback(code, state)` called | 200; `CalendarToken.Provider == "Microsoft"`; encrypted tokens differ from plaintext; `CalendarEventId` set | Token assertions; AES round-trip; `CreateEventAsync Times.Once` [SOURCE:INPUT] Basis: AC-002/AC-003 |
| TC-004 | positive | Idempotent re-sync skips `CreateEventAsync`; returns existing `CalendarEventId` | `CalendarToken.CalendarEventId = "existing-ms-event-id"` already in DB | `HandleMicrosoftCallback(code, state)` called | 200; `calendarSvc.CreateEventAsync` called `Times.Never` | `StatusCode == 200`; verify mock `Times.Never` [SOURCE:INPUT] Basis: AC-004 idempotency |
| TC-005 | negative | Tampered HMAC state → 400; `ExchangeCodeAsync` never attempted | `state` HMAC suffix modified | `HandleMicrosoftCallback` called | 400 Bad Request | `StatusCode == 400`; `ExchangeCodeAsync Times.Never` [SOURCE:INPUT] Basis: AC-001 CSRF guard |
| TC-006 | negative | Expired/missing PKCE session → 400 | Cache returns `null` for PKCE key | `HandleMicrosoftCallback` called | 400 Bad Request | `StatusCode == 400` [SOURCE:INPUT] Basis: AC-001 PKCE TTL |
| TC-007 | negative | Session/state mismatch (appointmentId in session ≠ state) → 400 | Session created for a different `appointmentId` | `HandleMicrosoftCallback` called | 400 Bad Request | `StatusCode == 400` [SOURCE:INPUT] Basis: PKCE session guard |
| TC-008 | negative | Missing `MicrosoftClientId` config → 503 | `MicrosoftClientId = ""` in settings | `HandleMicrosoftCallback` called | 503 Service Unavailable | `StatusCode == 503` [SOURCE:INFERRED] Basis: config guard `IsNullOrEmpty(clientId) → ServiceUnavailable` |
| EC-001 | edge_case | `code` missing (access_denied redirect) → 400 (implementation); spec says 200 — discrepancy | `code = ""` | `HandleMicrosoftCallback("", state)` called | 400 Bad Request (actual); Note: AC-003 spec says 200 | `StatusCode == 400` [SOURCE:INFERRED] Basis: same guard pattern as Google callback |
| EC-002 | edge_case | Graph `CreateEventAsync` throws `HttpRequestException` → error propagated | Network error on event creation | `HandleMicrosoftCallback` called | 502 Bad Gateway | `StatusCode == 502` [SOURCE:INFERRED] Basis: catch block `HttpRequestException → Problem(502)` |
| ES-001 | error | Missing dot in `state` parameter → 400 | `state = "nodothere"` | `HandleMicrosoftCallback` called | 400 Bad Request | `StatusCode == 400` [SOURCE:INFERRED] Basis: `state.Split('.')` parses `data.hmac` |

## AI Component Test Cases

> **AI Impact: No** — US_025 contains no AIR-XXX requirements. This section is skipped.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| EXISTS | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/CalendarSyncEndpointTests.cs` | `HandleCalendarSync` Microsoft provider tests shared with US_024 |
| EXISTS | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/MicrosoftCalendarCallbackEndpointTests.cs` | Tests for `GET /auth/microsoft/callback` (TC-003–TC-008, EC-001, EC-002, ES-001 covered) |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ApplicationDbContext` | EF Core In-Memory | `UseInMemoryDatabase(Guid.NewGuid().ToString())` + `TransactionIgnoredWarning` suppressed | Full in-memory DB per test |
| `ICacheService` | `Mock<ICacheService>` | `GetAsync<PkceSession>(key)` → seeded session or `null`; `DeleteAsync` → `Task.CompletedTask` | Configurable |
| `IMicrosoftCalendarService` | `Mock<IMicrosoftCalendarService>` | `ExchangeCodeAsync` (6-param incl. `tenantId`) → `TokenExchangeResult`; `CreateEventAsync` → `"ms-graph-event-id-001"` | `ReturnsAsync(...)` |
| `IOptions<CalendarSettings>` | `Options.Create(...)` | `MicrosoftClientId = "ms-client-id"`, `MicrosoftClientSecret = "ms-secret"`, `TenantId = "tenant-guid-123"`, `StateSecret = "test-state-secret-min-32-chars!"`, `AesKey = "test-aes-key"`, `MicrosoftRedirectUri = "http://localhost/callback"` | N/A |
| `HttpContext` | `DefaultHttpContext` | `ClaimsPrincipal` with `sub = patientId.ToString()` | N/A |

## AI Mocking Strategy

> **AI Impact: No** — Skipped.

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Valid MS initiation | Patient owns appointment; `MicrosoftClientId` + `StateSecret` + `TenantId` configured | 200 + authUrl with `Calendars.ReadWrite` scope + `code_challenge_method=S256` |
| Valid MS callback | Valid HMAC state; PKCE session in cache; tokens returned | 200; `CalendarToken` with AES-encrypted tokens; `Provider = "Microsoft"` |
| Idempotent re-sync | `CalendarToken.CalendarEventId` already set | 200; `CreateEventAsync` not called |
| Tampered HMAC | Signature suffix modified | 400 Bad Request |
| Expired PKCE session | Cache returns `null` for `pkce:{nonce}` | 400 Bad Request |
| Session mismatch | Session `appointmentId ≠ state appointmentId + 99` | 400 Bad Request |
| Missing client config | `MicrosoftClientId = ""` | 503 Service Unavailable |
| `code` missing (access_denied) | `code = ""` | 400 Bad Request (spec says 200 — discrepancy) |

## Test Commands

- **Run Tests**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --filter "FullyQualifiedName~CalendarSyncEndpointTests|FullyQualifiedName~MicrosoftCalendarCallbackEndpointTests"`
- **Run with Coverage**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --collect:"XPlat Code Coverage" --results-directory ./coverage`
- **Run Single Test**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --filter "FullyQualifiedName~HandleMicrosoftCallback_ValidFlow_StoresEncryptedTokensAndCreatesEvent"`

## Coverage Target

- **Line Coverage**: 88%
- **Branch Coverage**: 85%
- **Critical Paths**:
  - MS initiation — provider routing (google vs microsoft); `Calendars.ReadWrite offline_access` scope injected; `TenantId` in auth URL; 503 on missing config
  - MS callback — HMAC constant-time compare; PKCE session retrieval; 6-param `ExchangeCodeAsync`; AES-CBC encrypt round-trip; idempotency check; `CreateEventAsync` error handling

## Documentation References

- **Framework Docs**: [xUnit 2.x — https://xunit.net](https://xunit.net)
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/MicrosoftCalendarCallbackEndpointTests.cs`
- **Mocking Guide**: [Moq 4.x — https://github.com/devlooped/moq](https://github.com/devlooped/moq)
- **Microsoft Identity Platform**: https://docs.microsoft.com/en-us/azure/active-directory/develop/v2-oauth2-auth-code-flow

## Implementation Checklist

- [x] Create test file structure per Expected Changes
- [x] Set up test data fixtures per Test Data section
- [x] Configure mocking dependencies per Mocking Strategy
- [x] Implement positive test cases (TC-001 – TC-004)
- [x] Implement negative test cases (TC-005 – TC-008)
- [x] Implement edge case tests (EC-001, EC-002)
- [x] Implement error scenario tests (ES-001)
- [x] Run test suite and validate coverage meets target
