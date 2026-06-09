# Implementation Analysis — TASK_025: Microsoft Outlook Calendar OAuth2 PKCE Sync

## Verdict

**Status:** Conditional Pass
**Score:** 83 / 100

All four acceptance criteria are implemented and green. Two edge cases from the task specification have incomplete handling — one (F1) breaks idempotency on the 409 conflict path and is classified HIGH; the other (F2) returns the wrong status code on a no-refresh-token 401. Three LOW findings cover a documentation staleness, a redundant DB call, and an `HttpClient` dispose pattern deviation. No security vulnerabilities found.

---

## Traceability Matrix

| Requirement / Acceptance Criterion | Evidence | Result |
|---|---|---|
| AC-001: `POST /calendar-sync {provider:"microsoft"}` initiates Microsoft PKCE flow | `CalendarSyncEndpoint.HandleCalendarSync` L87-L120; `isMicrosoft` branch builds `login.microsoftonline.com/{tenantId}/oauth2/v2.0/authorize` URL | ✅ Pass |
| AC-002: `Calendars.ReadWrite offline_access` scope requested | Auth URL query: `scope=Calendars.ReadWrite+offline_access` L110; token exchange form: `scope=Calendars.ReadWrite offline_access` `MicrosoftCalendarService.cs` L37 & L52 | ✅ Pass |
| AC-003: Calendar event created via `POST /me/events` on Graph v1.0 | `MicrosoftCalendarService.CreateEventAsync` calls `https://graph.microsoft.com/v1.0/me/events` L89-L98 | ✅ Pass |
| AC-004: Idempotency — skip event creation if `CalendarEventId` already stored | `existingEventId` captured before token upsert; early return at L148-L149 if non-empty | ✅ Pass |
| Edge: Graph 409 → treat as success | `MicrosoftGraphConflictException` caught in callback L162-L167; returns 200 | ⚠ Conditional (see F1) |
| Edge: Token refresh fails → clear tokens, return 401 | `catch (HttpRequestException) when (Unauthorized && RefreshToken not null)` L170-L199; failure clears tokens, returns 401 | ⚠ Conditional (see F2) |
| Tokens encrypted AES-256-CBC before DB storage | `AesCbc.Encrypt()` called at L128-L132; round-trip verified in test `HandleMicrosoftCallback_ValidFlow_StoresEncryptedTokensAndCreatesEvent` | ✅ Pass |
| `CalendarSyncEndpoint` branches on `provider` field | `isMicrosoft`/`isGoogle` flags at L86-L87 | ✅ Pass |
| `IMicrosoftCalendarService` registered in DI | `services.AddScoped<IMicrosoftCalendarService, MicrosoftCalendarService>()` in `CalendarSyncEndpoint.AddServices` | ✅ Pass |
| `dotnet build` passes with 0 errors | Build output: 0 Warnings, 0 Errors | ✅ Pass |
| All tests pass | 307 total (13 Api.Tests + 294 Infrastructure.Tests), 0 failures | ✅ Pass |

---

## Logical & Design Findings

### F1 — HIGH: 409 Conflict Path Breaks Idempotency (Persistent `CalendarEventId` Never Set)

**Location:** `MicrosoftCalendarCallbackEndpoint.cs` L162-L167

**Code:**
```csharp
catch (MicrosoftGraphConflictException)
{
    await db.SaveChangesAsync(ct);
    return Results.Ok(new MicrosoftCallbackResponse(string.Empty, Idempotent: true));
}
```

**Problem:** When Graph returns 409, `existing.CalendarEventId` is never written. On the *next* re-sync call, `existingEventId` will be `null` (since the token row exists but has no `CalendarEventId`), so the idempotency guard at step 6 is skipped and `CreateEventAsync` is called again → another 409 → another conflict exception → indefinite repetition. The task edge case explicitly states: *"treat as success; **update stored CalendarEventId**"*.

The underlying constraint is that Graph does not return the event ID in the 409 response body (it returns an error object). The correct approach is to perform a follow-up `GET /me/events?$filter=subject eq 'Medical Appointment'` to retrieve the existing event ID, or to accept a known sentinel value (e.g., `"CONFLICT_RESOLVED"`) that signals the event exists but the ID is unknown, and treat that non-null value as idempotent.

**Impact:** Every re-sync of a patient whose first sync hit a 409 will repeatedly call Graph, returning 200 to the caller but never achieving true idempotency.

---

### F2 — MEDIUM: 401 from Graph Without Refresh Token Returns 502 Instead of 401

**Location:** `MicrosoftCalendarCallbackEndpoint.cs` L169-L200

**Code:**
```csharp
catch (HttpRequestException ex) when (
    ex.StatusCode == System.Net.HttpStatusCode.Unauthorized
    && existing.EncryptedRefreshToken is not null)   // ← when-guard
{
    // ... refresh path
}
catch (HttpRequestException ex)        // ← generic handler
{
    return Results.Problem(
        $"Failed to create Microsoft Calendar event: {ex.Message}",
        statusCode: StatusCodes.Status502BadGateway);
}
```

**Problem:** When Graph returns 401 *and* no refresh token is stored (`EncryptedRefreshToken == null`), the `when`-guard fails and the generic `HttpRequestException` catch fires, returning **502 Bad Gateway**. The task specification says: *"Token refresh fails → clear stored tokens; return 401 to prompt re-auth."* A missing refresh token is a degenerate form of refresh failure. Returning 502 will mislead the SPA into thinking Graph itself is down, not that re-authorization is needed.

**Fix:** Add a separate `catch (HttpRequestException ex) when (ex.StatusCode == Unauthorized)` clause *above* the generic one that clears tokens and returns 401 regardless of refresh token presence.

---

### F3 — LOW: `CalendarSyncEndpoint` Class Doc Comment Not Updated for Microsoft

**Location:** `CalendarSyncEndpoint.cs` L16

**Code:**
```csharp
/// Initiates the Google Calendar OAuth2 PKCE flow for an appointment (AC-001/AC-002/AC-003).
```

The handler now supports both Google and Microsoft but the summary still says "Google Calendar OAuth2 PKCE flow only". Misleading for future maintainers.

---

### F4 — LOW: Redundant `SaveChangesAsync` on 409 Conflict Path

**Location:** `MicrosoftCalendarCallbackEndpoint.cs` L163

Step 5 already committed the upserted `CalendarToken` to the database (`await db.SaveChangesAsync(ct)` at L143). Inside the `MicrosoftGraphConflictException` catch, a second `SaveChangesAsync` is called with no pending entity changes — a no-op that adds an unnecessary DB round-trip.

---

### F5 — LOW: `HttpClient` Dispose Pattern with `IHttpClientFactory`

**Location:** `MicrosoftCalendarService.cs` L60 and L81

```csharp
using var client = _httpFactory.CreateClient("MicrosoftOAuth");
using var client = _httpFactory.CreateClient("MicrosoftGraph");
```

Microsoft recommends **not** wrapping factory-managed `HttpClient` instances in `using` — the factory manages handler lifetimes; disposing the wrapper only disposes the thin `HttpClient` wrapper (no-op for the handler). Not a functional bug but deviates from the recommended pattern. The same minor pattern exists in `GoogleCalendarService.cs` (via `HttpClientFactory` for OAuth) — consistent but still worth noting.

---

### Security Assessment

| Area | Finding |
|---|---|
| CSRF protection | ✅ HMAC-SHA256 state with constant-time comparison (`CryptographicOperations.FixedTimeEquals`) |
| Token storage | ✅ AES-256-CBC encryption with per-ciphertext random IV before DB write |
| PKCE | ✅ S256 challenge; 64-char alphabet eliminates modulo bias; 10-minute Redis TTL; one-time use (deleted on retrieval) |
| Secret handling | ✅ All keys from environment variables; `IOptions<CalendarSettings>` bound at startup |
| State enumeration | ✅ 400 returned for all HMAC/session failures; no information leakage |
| Input validation | ✅ Null/whitespace guards on `code` and `state` at entry |
| TenantId injection | ✅ `Uri.EscapeDataString(tenantId)` applied before URL interpolation |

No OWASP Top-10 vulnerabilities detected.

---

### Performance Assessment

| Area | Finding |
|---|---|
| DB queries | Two `FirstOrDefaultAsync` calls (CalendarToken lookup + Appointment include). `UIX_CalendarTokens_AppointmentId_Provider` unique index covers the first query. |
| Multiple `SaveChangesAsync` on refresh path | Three calls possible in the 401+refresh path: (1) initial token save, (2) implicit tracking after refresh, (3) final eventId persist. The refresh + eventId persist could be merged into one call. |
| HttpClient creation | `CreateClient` returns a new `HttpClient` per call (handler pooled). One client created for token exchange, another for Graph — acceptable. |

---

## Test Review

### Existing Tests (12 tests in `MicrosoftCalendarCallbackEndpointTests.cs`)

| Test | Covers |
|---|---|
| `HandleMicrosoftCallback_InvalidHmacState_Returns400` | Tampered state → 400; `ExchangeCodeAsync` not called |
| `HandleMicrosoftCallback_MissingDotInState_Returns400` | Malformed state (no dot separator) → 400 |
| `HandleMicrosoftCallback_ExpiredPkceSession_Returns400` | Redis cache miss → 400 |
| `HandleMicrosoftCallback_SessionMismatch_Returns400` | State appointmentId ≠ session appointmentId → 400 |
| `HandleMicrosoftCallback_MissingConfig_Returns503` | No `MicrosoftClientId` → 503 |
| `HandleMicrosoftCallback_ValidFlow_StoresEncryptedTokensAndCreatesEvent` | Happy path: AES encryption, event creation, CalendarEventId stored |
| `HandleMicrosoftCallback_IdempotentResync_SkipsEventCreation` | Pre-seeded `CalendarEventId` → `CreateEventAsync` `Times.Never` |
| `HandleMicrosoftCallback_GraphConflict_Returns200` | 409 from Graph → returns 200 |
| `HandleMicrosoftCallback_GraphUnauthorized_RefreshSucceeds_EventCreated` | 401 → `RefreshTokenAsync` → retry `CreateEventAsync` → event stored |
| `HandleMicrosoftCallback_GraphUnauthorized_RefreshFails_Returns401AndClearsTokens` | 401 → refresh throws → tokens cleared, 401 returned |
| `HandleCalendarSync_MicrosoftProvider_Returns200WithMicrosoftAuthUrl` | AC-001/AC-002: URL contains `login.microsoftonline.com` + `Calendars.ReadWrite` |
| `HandleCalendarSync_MicrosoftProvider_MissingConfig_Returns503` | Microsoft config absent → 503 |

### Missing Tests (must add)

- [ ] **Negative/Edge:** `HandleMicrosoftCallback_GraphUnauthorized_NoRefreshToken_Returns401` — verifies F2 fix: when Graph returns 401 and `EncryptedRefreshToken` is null, expect 401 (not 502) and tokens cleared
- [ ] **Edge:** `HandleMicrosoftCallback_GraphConflict_EventIdPersisted` — once F1 is fixed (sentinel value or Graph lookup), verify `CalendarEventId` is non-empty in DB after a 409, so next call is truly idempotent
- [ ] **Unit:** `MicrosoftCalendarService_CreateEventAsync_Sets_Authorization_Header` — verify Bearer header is sent correctly (integration-level; acceptable to defer)

---

## Validation Results

**Commands executed:**
```bash
dotnet build
dotnet test --no-build
```

**Outcomes:**

| Command | Result |
|---|---|
| `dotnet build` | ✅ 0 Errors, 0 Warnings |
| `dotnet test --no-build` | ✅ 307 passed (13 Api.Tests + 294 Infrastructure.Tests), 0 failed |

---

## Fix Plan (Prioritized)

| # | Finding | Fix | File(s) | Effort | Risk |
|---|---|---|---|---|---|
| F1 | HIGH: 409 path — CalendarEventId never persisted | On `MicrosoftGraphConflictException`, attempt `GET /me/events?$filter=...` to retrieve existing event ID; on failure, persist a sentinel string (e.g., `"CONFLICT"`) and treat any non-null `CalendarEventId` as idempotent. Update idempotency guard to `CalendarEventId is not null`. Add corresponding test. | `MicrosoftCalendarCallbackEndpoint.cs`, `IMicrosoftCalendarService.cs`, `MicrosoftCalendarService.cs`, test file | 2–3 h | Medium |
| F2 | MEDIUM: 401 without refresh token → 502 | Add `catch (HttpRequestException ex) when (ex.StatusCode == Unauthorized)` before the generic handler. Clear tokens, return 401. Add test `HandleMicrosoftCallback_GraphUnauthorized_NoRefreshToken_Returns401`. | `MicrosoftCalendarCallbackEndpoint.cs`, test file | 30 min | Low |
| F3 | LOW: Stale doc comment on `CalendarSyncEndpoint` | Update XML summary to mention both Google and Microsoft. | `CalendarSyncEndpoint.cs` L16 | 5 min | None |
| F4 | LOW: Redundant `SaveChangesAsync` on 409 | Remove the `await db.SaveChangesAsync(ct)` inside the `MicrosoftGraphConflictException` catch; the prior call (step 5) already committed. | `MicrosoftCalendarCallbackEndpoint.cs` L163 | 5 min | None |
| F5 | LOW: `HttpClient` dispose pattern | Remove `using` from `_httpFactory.CreateClient(...)` calls. Not functionally required; aligns with Microsoft `IHttpClientFactory` guidance. | `MicrosoftCalendarService.cs` L60, L81 | 10 min | None |

---

## Appendix

### Rules Applied
- OWASP Top-10 secure coding guardrails (security-standards-owasp.md)
- Backend development standards (backend-development-standards.md)
- DRY principle guidelines (dry-principle-guidelines.md)
- Performance best practices (performance-best-practices.md)

### Search Evidence

| Pattern | File | Purpose |
|---|---|---|
| `CreateEventAsync` | `MicrosoftCalendarService.cs` L79 | AC-003 Graph endpoint |
| `Calendars.ReadWrite` | `CalendarSyncEndpoint.cs` L110 + `MicrosoftCalendarService.cs` L37 | AC-002 scope |
| `existingEventId` | `MicrosoftCalendarCallbackEndpoint.cs` L136 | AC-004 idempotency |
| `MicrosoftGraphConflictException` | `MicrosoftCalendarCallbackEndpoint.cs` L162 + `MicrosoftCalendarService.cs` L100 | 409 edge case |
| `FixedTimeEquals` | `MicrosoftCalendarCallbackEndpoint.cs` L85 | CSRF protection |
| `AesCbc.Encrypt` | `MicrosoftCalendarCallbackEndpoint.cs` L128 | Token encryption |
