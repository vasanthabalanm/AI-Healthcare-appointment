---
task_id: task_029
us_id: us_029
epic: EP-004
review_date: 2026-05-18
reviewer: GitHub Copilot
verdict: Conditional Pass
---

# Implementation Analysis — `.propel/context/tasks/EP-004/us_029/task_029_ai-conversational-intake-rasa.md`

## Verdict

**Status:** Conditional Pass
**Summary:** TASK_029 delivers all four AI intake endpoints (`/intake/ai/start`, `/intake/ai/message`, `/intake/ai/complete`, `/intake/ai/switch-to-manual`) with correct Redis session management, ownership guards, and 503 error handling. AC-001, AC-002, AC-004, and AC-005 are fully implemented and tested. However, two significant gaps prevent a full Pass: (1) AC-006 (`POST /intake/ai/complete`) has zero unit-test coverage, and (2) the Rasa REST webhook response format does not include a `confidence` field — meaning `RasaMessage.Confidence` will always deserialize as `0.0`, making the AC-003 confidence threshold non-functional in a live deployment.

---

## Traceability Matrix

| Requirement / Acceptance Criterion | Evidence (file : fn / line) | Result |
|---|---|---|
| AC-001: `POST /intake/ai/start` proxies to Rasa; returns session ID | `StartAiIntakeEndpoint.cs` : `HandleStartAiIntake()` L64–82 | Pass |
| AC-002: Redis TTL=900s stored on start | `StartAiIntakeEndpoint.cs` : L55–58, `SessionTtl = 900s` | Pass |
| AC-002: TTL reset on each message | `SendAiMessageEndpoint.cs` : `HandleSendAiMessage()` L79–81 | Pass |
| AC-003: confidence < 0.70 → field not committed | `SendAiMessageEndpoint.cs` : L75–78, `IsSufficientConfidence()` | Pass (logic) / **Gap** (Rasa webhook lacks confidence field) |
| AC-003: confidence ≥ 0.70 → field committed | `SendAiMessageEndpoint.cs` : L75–78 | Pass (logic) / **Gap** (see F2) |
| AC-004: `POST /intake/ai/switch-to-manual` preserves confirmed fields | `SwitchToManualEndpoint.cs` : `HandleSwitchToManual()` L58–70 | Pass |
| AC-004: AI Redis session deleted after switch | `SwitchToManualEndpoint.cs` : L66 | Pass |
| AC-005: Rasa unavailable → 503 on start | `StartAiIntakeEndpoint.cs` : L75–80 catch `RasaUnavailableException` | Pass |
| AC-005: Rasa unavailable → 503 on message | `SendAiMessageEndpoint.cs` : L65–70 catch `RasaUnavailableException` | Pass |
| AC-006: `POST /intake/ai/complete` creates `IntakeRecord` with `Source=AI` | `CompleteAiIntakeEndpoint.cs` : L60–80 | Pass |
| AC-006: Maps confirmedFields → IntakeRecord properties | `CompleteAiIntakeEndpoint.cs` : L68–74 | Pass |
| `RASA_URL` from env var; default `http://localhost:5005` | `RasaIntakeService.cs` : `SendMessageAsync()` L77 | Pass |
| `dotnet build` passes with 0 errors | Terminal output: `Build succeeded. 0 Warning(s). 0 Error(s).` | Pass |
| All tests pass | Terminal: 348 passed, 0 failed | Pass |
| OWASP A01: Ownership guard (cross-session access) | `SendAiMessageEndpoint.cs` L60, `CompleteAiIntakeEndpoint.cs` L57, `SwitchToManualEndpoint.cs` L57 | Pass |
| OWASP A01: `RequireAuthorization("PatientOnly")` on all 4 routes | All 4 endpoint `MapEndpoints()` methods | Pass |

---

## Logical & Design Findings

### Business Logic

- **F1 — MEDIUM:** `RasaIntakeService.IsSufficientConfidence()` is a `public static` method on the **concrete class**, not on the `IRasaIntakeService` interface. `SendAiMessageEndpoint` calls it as `RasaIntakeService.IsSufficientConfidence(...)`, creating a direct compile-time dependency on the concrete type from the API layer. This violates the dependency inversion intent of the interface extraction.

- **F2 — HIGH:** The Rasa REST webhook (`POST /webhooks/rest/webhook`) returns responses in the format `[{"recipient_id": "...", "text": "..."}]`. There is **no `confidence` field** in the standard webhook response. `RasaMessage.Confidence` will always deserialize as `0.0` (default `double`), which means `IsSufficientConfidence()` will always return `false`. AC-003's field-commitment logic is therefore **functionally broken** in any real Rasa deployment. Confidence is available via `POST /model/parse` (NLU-only endpoint), not via the webhook.

- **F3 — LOW:** The confidence threshold (`0.70`) is a `private const` inside `RasaIntakeService`. The `.env` file defines `AI_EXTRACTION_CONFIDENCE_THRESHOLD=0.70`, signalling that thresholds are configuration-driven. The threshold cannot be overridden without a code change.

### Security

- **OWASP A01 ✅:** All four endpoints enforce `PatientOnly` authorization. Ownership guards (`session.PatientId != patientId.Value → Forbid()`) are applied in `SendAiMessage`, `CompleteAiIntake`, and `SwitchToManual`. `StartAiIntake` binds `PatientId` directly from the verified JWT `sub` claim.
- **OWASP A03 ✅:** Session IDs are `Guid.NewGuid().ToString("N")` — not user-derived. Redis keys are prefixed `"ai-intake:{guid}"`. No SQL interaction with user message text.
- **OWASP A04 — LOW:** No rate limiting on `POST /intake/ai/message`. A patient could spam Rasa with unbounded requests within the 900s TTL window, causing downstream load.
- **OWASP A04 — LOW:** `SendAiMessageRequest.Message` has no maximum length constraint. A very large payload would be forwarded verbatim to Rasa.

### Error Handling

- **Start → 503:** Orphaned Redis session is cleaned up before returning 503. ✅
- **RasaUnavailableException** wraps both HTTP non-2xx and `HttpRequestException`/`TaskCanceledException`. ✅
- **Empty Rasa response** returns a hardcoded fallback string with `Confidence = 0.0`. ✅
- **CompleteAiIntakeEndpoint with zero confirmedFields:** Creates a valid `IntakeRecord` with all intake fields as `null`. No guard prevents empty completion — this is an edge case that warrants a business-logic review (should the API allow completing with no data?).

### Data Access

- `CompleteAiIntakeEndpoint`: `db.SaveChangesAsync(ct)` then `cache.DeleteAsync(...)`. If `SaveChangesAsync` succeeds but `DeleteAsync` fails, the DB record is persisted but the Redis session remains. The next call to `CompleteAiIntakeEndpoint` with the same session ID would create a **duplicate `IntakeRecord`**. There is no idempotency guard (e.g., checking for an existing record tied to the session before inserting). **MEDIUM risk.**

### Performance

- Each message turn performs 2 cache operations (GetAsync + SetAsync) plus 1 Rasa HTTP call. Acceptable for interactive chat.
- No connection pooling concern — `IHttpClientFactory` manages `HttpClient` lifetime correctly via named client `"Rasa"`. ✅

### Patterns & Standards

- Vertical-slice `IEndpointDefinition` pattern followed correctly for all four endpoints. ✅
- `ExtractPatientId()` and `CacheKey()` are `internal static` helpers on `StartAiIntakeEndpoint`, reused by other endpoint classes. Acceptable for intra-assembly sharing; slightly unusual coupling between sibling endpoint classes.
- `AddServices()` is no-op in all four endpoints — DI registration done in `Program.cs`. Consistent with project pattern. ✅

---

## Test Review

### Existing Tests

| Test | AC Covered | Result |
|---|---|---|
| `StartAiIntake_Valid_ReturnsSessionId` | AC-001, AC-002 | ✅ Pass |
| `StartAiIntake_RasaUnavailable_Returns503` | AC-005 | ✅ Pass |
| `SendAiMessage_LowConfidence_FieldNotCommitted` | AC-003 (low) | ✅ Pass |
| `SendAiMessage_HighConfidence_FieldCommitted` | AC-003 (high) | ✅ Pass |
| `SendAiMessage_AlwaysResetsTtl` | AC-002 reset | ✅ Pass |
| `SendAiMessage_SessionExpired_Returns404` | Guard | ✅ Pass |
| `SendAiMessage_RasaUnavailable_Returns503` | AC-005 message | ✅ Pass |
| `SwitchToManual_ReturnsConfirmedFieldsAndDeletesSession` | AC-004 | ✅ Pass |
| `SwitchToManual_SessionExpired_Returns404` | Guard | ✅ Pass |

### Missing Tests (must add)

- [ ] **Unit — AC-006:** `CompleteAiIntake_Valid_CreatesIntakeRecordWithSourceAI` — verify `IntakeRecord.Source == IntakeSource.AI`, fields mapped, 201 returned, Redis session deleted.
- [ ] **Unit — AC-006:** `CompleteAiIntake_SessionExpired_Returns404` — verify 404 when cache returns null.
- [ ] **Unit — Ownership guard:** `SendAiMessage_WrongPatient_Returns403` — verify `session.PatientId != requestPatientId → 403`.
- [ ] **Unit — Ownership guard:** `CompleteAiIntake_WrongPatient_Returns403`
- [ ] **Unit — Ownership guard:** `SwitchToManual_WrongPatient_Returns403`
- [ ] **Unit — Edge case:** `CompleteAiIntake_NoConfirmedFields_CreatesEmptyIntakeRecord` — define expected behaviour.
- [ ] **Unit — Input validation:** `SendAiMessage_EmptyMessage_Returns400`

---

## Validation Results

**Commands Executed:**

```bash
dotnet build --no-restore
dotnet test
```

**Outcomes:**

```text
Build succeeded. 0 Warning(s). 0 Error(s).
Passed! - Failed: 0, Passed: 348, Skipped: 0, Total: 348
```

---

## Fix Plan (Prioritized)

| # | Finding | Fix | Files | Risk |
|---|---|---|---|---|
| 1 | **F2 — Rasa webhook has no `confidence` field** | Call `POST {RASA_URL}/model/parse` for NLU confidence; call webhook for bot reply. Merge into `RasaMessage`. Or accept confidence from custom Rasa action data via `custom` field. Document assumption explicitly. | `RasaIntakeService.cs` | H |
| 2 | **Missing AC-006 test** | Add `CompleteAiIntake_Valid_CreatesIntakeRecordWithSourceAI` using InMemory DB and mock cache | `AiIntakeEndpointTests.cs` | H |
| 3 | **Duplicate IntakeRecord on retry** | Add idempotency check: query `db.IntakeRecords` for existing record with matching `PatientId` and session-derived field before inserting, or store `IntakeRecordId` in `AiIntakeSession` after first completion | `CompleteAiIntakeEndpoint.cs`, `AiIntakeSession.cs` | M |
| 4 | **F1 — Static coupling to concrete class** | Move `IsSufficientConfidence` to a `public static class AiConfidence` helper, or expose threshold as a constant on `IRasaIntakeService` | `RasaIntakeService.cs`, `SendAiMessageEndpoint.cs` | M |
| 5 | **Missing ownership guard tests** | Add 3 Forbid tests (send, complete, switch) | `AiIntakeEndpointTests.cs` | M |
| 6 | **F3 — Threshold not configurable** | Read `AI_EXTRACTION_CONFIDENCE_THRESHOLD` env var in `RasaIntakeService` constructor; fall back to `0.70` | `RasaIntakeService.cs` | L |
| 7 | **No rate limiting on message endpoint** | Apply `.RequireRateLimiting` policy or use existing project rate-limiter on `/intake/ai/message` | `SendAiMessageEndpoint.cs` | L |

---

## Appendix

### Rules Applied

- `rules/security-standards-owasp.md` — OWASP A01, A03, A04 review
- `rules/backend-development-standards.md` — service/endpoint patterns
- `rules/dotnet-architecture-standards.md` — vertical-slice IEndpointDefinition
- `rules/language-agnostic-standards.md` — KISS, naming, size
- `rules/code-anti-patterns.md` — static coupling, magic constants
- `rules/dry-principle-guidelines.md` — shared helpers assessment

### Search Evidence

```text
grep: RasaIntakeService — 17 matches across 5 files
grep: IRasaIntakeService — used in Program.cs, 4 endpoint files, test file, RasaIntakeService.cs
grep: IsSufficientConfidence — SendAiMessageEndpoint.cs L77, RasaIntakeService.cs L125
grep: IntakeSource.AI — CompleteAiIntakeEndpoint.cs L66
grep: RequireAuthorization("PatientOnly") — 4 endpoint MapEndpoints() calls
```

### Context7 References

- Rasa REST API: `POST /webhooks/rest/webhook` response schema — `[{recipient_id, text}]` (no confidence field)
- Rasa NLU parse: `POST /model/parse` — returns `{intent: {confidence}, entities, text}`
