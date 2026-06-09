# Unit Test Plan - TASK_029

## Requirement Reference
- **User Story**: us_029
- **Story Location**: `.propel/context/tasks/EP-004/us_029/us_029.md`
- **Layer**: BE
- **Related Test Plans**: None (single-layer BE story)
- **Acceptance Criteria Covered**:
  - AC-001: `POST /intake/ai/start` creates Redis session (TTL=900s) and returns greeting from Rasa
  - AC-002: Dialogue turns update Redis session; TTL resets on every message
  - AC-003: `POST /intake/ai/complete` creates `IntakeRecord` with `Source=AI`; Redis session deleted
  - AC-004: Confidence < 0.70 → clarification returned; field not committed to session
  - AC-005: `POST /intake/ai/switch-to-manual` returns confirmedFields; deletes Redis session
  - AC-006: Rasa unavailable → HTTP 503; no data loss; orphaned Redis key cleaned up

## Test Plan Overview

Tests the four AI intake endpoints (`StartAiIntakeEndpoint`, `SendAiMessageEndpoint`, `CompleteAiIntakeEndpoint`, `SwitchToManualEndpoint`) and the `IRasaIntakeService.IsSufficientConfidence` static helper. All Rasa HTTP calls are mocked via `IRasaIntakeService`; Redis is mocked via `ICacheService`. EF Core In-Memory database is used for `CompleteAiIntakeEndpoint` DB writes. Tests cover: session creation, confidence-gated field commitment, TTL enforcement, idempotent completion, manual-fallback handoff, cross-patient ownership guards, and Rasa failure handling. AIR-001 coverage includes confidence threshold logic tests.

## Dependent Tasks

- TASK_001 (us_004) — `ICacheService` Redis abstraction (shared mock contract)
- TASK_001 (us_008) — `IntakeRecord` entity

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `StartAiIntakeEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Intake/StartAiIntakeEndpoint.cs` | Generates session ID, stores AiIntakeSession in Redis TTL=900s, calls Rasa greeting |
| `SendAiMessageEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Intake/SendAiMessageEndpoint.cs` | Proxies message to Rasa, enforces confidence threshold, resets TTL |
| `CompleteAiIntakeEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Intake/CompleteAiIntakeEndpoint.cs` | Converts confirmedFields into IntakeRecord(Source=AI), deletes session |
| `SwitchToManualEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Intake/SwitchToManualEndpoint.cs` | Returns confirmedFields as pre-fill data, deletes AI session |
| `IRasaIntakeService` | interface | `src/ClinicalHealthcare.Infrastructure/AI/RasaIntakeService.cs` | Confidence threshold logic via `IsSufficientConfidence` static method |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | Valid start returns 200 + sessionId; Redis stored with TTL=900s | Patient JWT (userId=42); Rasa mock returns greeting "Welcome!" | `HandleStartAiIntake` called | HTTP 200; Redis `SetAsync` called with key `ai-intake:{guid}`, TTL=900s | `StatusCode==200`; `cache.Verify SetAsync(key, session, TimeSpan.FromSeconds(900), ct) Times.Once` |
| TC-002 | positive | High-confidence message (≥0.70) commits field to session | Session exists (PatientId=9); Rasa mock confidence=0.92; fieldName="chiefComplaint" | `HandleSendAiMessage` called | HTTP 200; session updated; `chiefComplaint` key in `ConfirmedFields` | `StatusCode==200`; `cache.Verify SetAsync with session.ConfirmedFields.ContainsKey("chiefComplaint") Times.Once` |
| TC-003 | positive | Session TTL reset on every message regardless of confidence | Session exists; Rasa mock confidence=0.30 (below threshold) | `HandleSendAiMessage` called | TTL reset to 900s even on low-confidence response | `cache.Verify SetAsync("ai-intake:sess", _, TimeSpan.FromSeconds(900), ct) Times.Once` |
| TC-004 | positive | Low-confidence (<0.70) → field NOT committed; clarification flag set | Session exists; Rasa mock confidence=0.50; fieldName="chiefComplaint" | `HandleSendAiMessage` called | HTTP 200; `chiefComplaint` NOT in `ConfirmedFields`; `requiresClarification=true` | `StatusCode==200`; `cache.Verify SetAsync with !session.ConfirmedFields.ContainsKey("chiefComplaint") Times.Once` |
| TC-005 | positive | Switch to manual returns confirmedFields and deletes session | Session exists with `ConfirmedFields={"chiefComplaint":"headache"}`; PatientId=20 | `HandleSwitchToManual` called | HTTP 200; `confirmedFields` in response body; Redis session deleted | `StatusCode==200`; `cache.Verify DeleteAsync("ai-intake:sess-switch") Times.Once` |
| TC-006 | positive | Complete creates IntakeRecord(Source=AI) and returns 201 | Session exists with `ConfirmedFields={"chiefComplaint":"headache","allergies":"penicillin"}`; PatientId=15 | `HandleCompleteAiIntake` called | HTTP 201; `IntakeRecord` in DB with `Source=AI`, correct fields; session deleted | `StatusCode==201`; `db.IntakeRecords.Single().Source==IntakeSource.AI`; `record.ChiefComplaint=="headache"` |
| TC-007 | positive | Idempotent retry returns existing record without duplicate DB row | Session has `CompletedIntakeRecordId=99`; PatientId=17 | `HandleCompleteAiIntake` called again | HTTP 201 with existing recordId; no new IntakeRecord inserted | `StatusCode==201`; `db.IntakeRecords.Count()==0` |
| EC-001 | edge_case | Empty sessionId guard → 400 Bad Request | Valid patient JWT; request with `SessionId=""` | `HandleSendAiMessage` called | HTTP 400; no Rasa call made | `StatusCode==400`; `rasa.Verify SendMessageAsync Times.Never` |
| EC-002 | edge_case | Empty message text → 400 Bad Request | Valid session; request with `Message=""` | `HandleSendAiMessage` called | HTTP 400 | `StatusCode==400` |
| EC-003 | edge_case | Confidence exactly at 0.70 → field committed (inclusive boundary) | Rasa mock confidence=0.70; fieldName="allergies" | `IsSufficientConfidence(0.70)` called | Returns `true`; field committed | `Assert.True(IRasaIntakeService.IsSufficientConfidence(0.70))` |
| ES-001 | error | Rasa unavailable on start → 503; orphaned Redis key cleaned up | Rasa mock throws `RasaUnavailableException` on `SendMessageAsync` | `HandleStartAiIntake` called | HTTP 503; `DeleteAsync` called to remove orphaned session key | `StatusCode==503`; `cache.Verify DeleteAsync Times.Once` |
| ES-002 | error | Session expired (GetAsync returns null) → 404 | Cache returns null for session key | `HandleSendAiMessage` called | HTTP 404 | `StatusCode==404` |
| ES-003 | error | Rasa unavailable during message → 503 | Session exists; Rasa throws `RasaUnavailableException` | `HandleSendAiMessage` called | HTTP 503 | `StatusCode==503` |
| ES-004 | error | Switch-to-manual session expired → 404 | Cache returns null | `HandleSwitchToManual` called | HTTP 404 | `StatusCode==404` |
| ES-005 | error | Complete session expired → 404 | Cache returns null | `HandleCompleteAiIntake` called | HTTP 404 | `StatusCode==404` |
| ES-006 | error | Wrong patient sends message → 403 Forbidden | Session PatientId=99; JWT PatientId=1 | `HandleSendAiMessage` called | ForbidHttpResult | `result.GetType().Name=="ForbidHttpResult"` |
| ES-007 | error | Wrong patient completes session → 403 Forbidden | Session PatientId=99; JWT PatientId=1 | `HandleCompleteAiIntake` called | ForbidHttpResult | `result.GetType().Name=="ForbidHttpResult"` |
| ES-008 | error | Wrong patient switches to manual → 403 Forbidden | Session PatientId=99; JWT PatientId=1 | `HandleSwitchToManual` called | ForbidHttpResult | `result.GetType().Name=="ForbidHttpResult"` |

## AI Component Test Cases

> **Note:** This section applies because the user story maps to AIR-001 (Rasa NLU confidence-gated field extraction).

**AI Requirements Covered:**
- AIR-001: Confidence threshold (default 0.70) gates field commitment; configurable via `AI_EXTRACTION_CONFIDENCE_THRESHOLD` env var

| Test-ID | Type | Description | Input | Expected | Assertions |
|---------|------|-------------|-------|----------|------------|
| AI-001 | prompt_mock | `IsSufficientConfidence` default threshold — confidence ≥ 0.70 → true | `confidence=0.70`, no env var set | Returns `true` | `Assert.True(IRasaIntakeService.IsSufficientConfidence(0.70))` |
| AI-002 | prompt_mock | `IsSufficientConfidence` env var override — custom threshold respected | `AI_EXTRACTION_CONFIDENCE_THRESHOLD=0.80`; `confidence=0.75` | Returns `false` (below custom threshold) | After setting env var to "0.80": `Assert.False(IsSufficientConfidence(0.75))` |
| AI-003 | fallback | Confidence 0.69 (below default threshold) → false; field not committed | `confidence=0.69` | Returns `false` | `Assert.False(IRasaIntakeService.IsSufficientConfidence(0.69))` |

### AI Test Case Design Patterns

**Confidence Threshold Test (AI-001, AI-003):**
```text
Given: No AI_EXTRACTION_CONFIDENCE_THRESHOLD env var; confidence value provided
When:  IRasaIntakeService.IsSufficientConfidence(confidence) is called (static method)
Then:
  - Returns true when confidence >= 0.70 (default)
  - Returns false when confidence < 0.70
  - No external calls made (pure computation)
```

**Env Var Override Test (AI-002):**
```text
Given: AI_EXTRACTION_CONFIDENCE_THRESHOLD="0.80" is set before call; confidence=0.75
When:  IRasaIntakeService.IsSufficientConfidence(0.75) is called
Then:
  - Returns false (0.75 < 0.80 custom threshold)
  - After test: env var restored to null (test isolation)
```

**Fallback Verification (TC-004 + AI-003):**
```text
Given: Rasa returns mocked RasaMessage(Text="Can you rephrase?", Confidence=0.50)
When:  SendAiMessageEndpoint processes the reply
Then:
  - requiresClarification = true in response body
  - fieldCommitted = false
  - ConfirmedFields unchanged in session
  - TTL still reset to 900s
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/AiIntakeEndpointTests.cs` | Add EC-001, EC-002, EC-003, AI-001, AI-002, AI-003 tests |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `IRasaIntakeService` | Mock (Moq) | `SendMessageAsync` returns configurable `RasaMessage(Text, Confidence)` | `RasaMessage("reply", 0.95)` default; `RasaUnavailableException` for error cases |
| `ICacheService` | Mock (Moq) | `SetAsync` returns `Task.CompletedTask`; `GetAsync<AiIntakeSession>` returns seeded session or null; `DeleteAsync` returns `Task.CompletedTask` | Session object or null |
| `ApplicationDbContext` | In-Memory EF Core | `UseInMemoryDatabase(Guid.NewGuid().ToString())`; `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` | Real in-memory store per test |
| `HttpContext` | `DefaultHttpContext` | JWT sub claim set via `ClaimsPrincipal` with `JwtRegisteredClaimNames.Sub` | PatientId as string |

## AI Mocking Strategy

| Dependency | Mock Type | Mock Approach | Example Return |
|------------|-----------|---------------|----------------|
| `IRasaIntakeService` | `Mock<IRasaIntakeService>` (Moq) | `SendMessageAsync` returns predetermined `RasaMessage` | `new RasaMessage("Hello!", 0.95)` |
| Rasa NLU confidence | Inline constant | Pass exact confidence value to mock; bypass HTTP | `confidence: 0.50` (low), `0.92` (high), `0.70` (boundary) |
| Rasa failure | Exception injection | `ThrowsAsync(new RasaUnavailableException("down"))` | N/A |

**Note:** `IRasaIntakeService.IsSufficientConfidence` is a static method — tested directly without mocking. Env var `AI_EXTRACTION_CONFIDENCE_THRESHOLD` is set/cleared with `try/finally` for test isolation.

### AI Mock Response Factory

```csharp
// Rasa mock factory
private static Mock<IRasaIntakeService> RasaMock(
    string replyText  = "Hello! How can I help?",
    double confidence = 0.95)
{
    var mock = new Mock<IRasaIntakeService>();
    mock.Setup(r => r.SendMessageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new RasaMessage(replyText, confidence));
    return mock;
}

// Unavailable Rasa
private static Mock<IRasaIntakeService> RasaUnavailableMock()
{
    var mock = new Mock<IRasaIntakeService>();
    mock.Setup(r => r.SendMessageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new RasaUnavailableException("service down"));
    return mock;
}
```

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Valid start | `PatientId=42`; Rasa returns `"Welcome!"` confidence=1.0 | `StatusCode=200`; Redis key `ai-intake:{guid}` set with TTL=900s |
| High-confidence message | `SessionId="sess-002"`, `Message="headache"`, `FieldName="chiefComplaint"`, confidence=0.92 | `fieldCommitted=true`; `ConfirmedFields["chiefComplaint"]="headache"` |
| Low-confidence message | `SessionId="sess-001"`, `Message="maybe something"`, confidence=0.50 | `requiresClarification=true`; `fieldCommitted=false` |
| Boundary confidence | `confidence=0.70` | `IsSufficientConfidence==true` |
| Complete session | `ConfirmedFields={"chiefComplaint":"headache","allergies":"penicillin"}` | `IntakeRecord.ChiefComplaint="headache"`, `Source=AI` |
| Rasa down | `RasaUnavailableException` thrown | `StatusCode=503`; orphaned key deleted |

## Test Commands

- **Run Tests**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~AiIntakeEndpointTests"`
- **Run with Coverage**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~AiIntakeEndpointTests"`
- **Run Single Test**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~AiIntakeEndpointTests.TC_001"`

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**: `IsSufficientConfidence` (both branches); `HandleStartAiIntake` Rasa-unavailable path; ownership guard in all three session-mutating endpoints

## Documentation References

- **Framework Docs**: [xUnit.net](https://xunit.net/docs/getting-started/netcore/cmdline)
- **Mocking Guide**: [Moq Quickstart](https://github.com/moq/moq4/wiki/Quickstart)
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/AiIntakeEndpointTests.cs`
- **Rasa REST API**: [Rasa HTTP API](https://rasa.com/docs/rasa/http-api)

## Implementation Checklist

- [x] Modify test file `tests/.../Features/AiIntakeEndpointTests.cs`
- [x] Set up `RasaMock()` and `NoOpCache()` helpers
- [x] Implement positive test cases (TC-001 to TC-007)
- [x] Implement error scenario tests (ES-001 to ES-008)
- [x] Implement edge case tests (EC-001, EC-002, EC-003)
- [x] Run test suite and validate coverage meets target

### AI Test Implementation Checklist

- [x] Implement `IsSufficientConfidence` default threshold test (AI-001)
- [x] Implement env var override test (AI-002) with `try/finally` for isolation
- [x] Implement below-threshold boundary test (AI-003)
- [x] Verify no actual Rasa HTTP calls are made in unit tests (all via `IRasaIntakeService` mock)
