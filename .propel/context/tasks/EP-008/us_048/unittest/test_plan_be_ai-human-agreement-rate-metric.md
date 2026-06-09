# Unit Test Plan - TASK_048

## Requirement Reference
- **User Story**: us_048
- **Story Location**: `.propel/context/tasks/EP-008/us_048/us_048.md`
- **Layer**: BE
- **Related Test Plans**: `EP-008/us_047/unittest/test_plan_be_trust-first-code-verification.md` (provides the actioned `MedicalCodeSuggestion` rows consumed by the agreement metric)
- **Acceptance Criteria Covered**:
  - AC-001: `GET /admin/metrics/code-agreement?days=30` → `{agreementRate, totalActioned, accepted, modified, rejected, windowDays}`
  - AC-002: `agreementRate = count(Accepted where committedCode == suggestedCode OR committedCode is null) / totalActioned`
  - AC-003: Admin role only → 403 for Staff/Patient (enforced at middleware; not unit-testable)
  - AC-004: Zero actioned → `{agreementRate:null, totalActioned:0, message:"No code suggestions actioned..."}` without division by zero
  - AC-005: `days<=0` → 422; `days>365` → capped to 365 + WARNING; absent `days` → default 30

## Test Plan Overview

Tests `CodeAgreementMetricEndpoint.HandleGetCodeAgreement` (static handler method) against `ClinicalDbContext` (PG InMemory).

`ClinicalDbContext` uses `UseInMemoryDatabase(Guid.NewGuid().ToString())` + `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))`.
`ILogger<CodeAgreementMetricEndpoint>` uses `NullLogger<CodeAgreementMetricEndpoint>.Instance`.

Response body inspection uses `result.GetType().GetProperty("Value")?.GetValue(result)` → JSON deserialization.

**Gaps noted:**
- AC-003 role-based 403 guard is enforced by ASP.NET Core authorization middleware (`.RequireAuthorization("AdminOnly")`). Calling the static handler directly bypasses middleware; role tests are out of scope for unit test level and should be covered by integration tests.
- `SuggestionStatus.Modified` is stored when `committedCode ≠ suggestedCode`. The `modified` DTO field counts both explicit `Modified` status rows AND `Accepted` rows where `committedCode ≠ suggestedCode`. This is intentional per the source implementation.

## Dependent Tasks

- TASK_001 (Entities) — `MedicalCodeSuggestion`, `CodeType`, `SuggestionStatus`
- TASK_001 (Data) — `ClinicalDbContext.MedicalCodeSuggestions`
- TASK_047 (us_047) — Verification flow that sets `Status`, `CommittedCode`, `VerifiedAt`
- TASK_048 — `CodeAgreementMetricEndpoint`

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `CodeAgreementMetricEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Admin/CodeAgreementMetricEndpoint.cs` | Validate `days`; query `MedicalCodeSuggestions` in window; compute agreement rate; return DTO |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | Returns full DTO with all required fields (AC-001) `[SOURCE:INPUT]` | Seed 1 Accepted + 1 Rejected row with `VerifiedAt=UtcNow` | `HandleGetCodeAgreement(pgDb, logger, days:30, ct)` | Status 200; body has all 6 DTO properties | `Assert.Equal(200, status)`; `Assert.True(body.TryGetProperty("agreementRate", out _))` for all 6 fields |
| TC-002 | positive | agreementRate = unmodified Accepted / totalActioned `[SOURCE:INPUT]` | Seed 2 unmodified Accepted + 1 Rejected (totalActioned=3) | `HandleGetCodeAgreement(pgDb, logger, days:30, ct)` | `agreementRate ≈ 0.6667`; `totalActioned=3`; `accepted=2`; `rejected=1` | `Assert.Equal(0.6667, body.GetProperty("agreementRate").GetDouble(), 4)`; sub-count assertions |
| TC-003 | positive | Modified row (committedCode ≠ suggestedCode) counted as disagreement `[SOURCE:INPUT]` | Seed 1 unmodified Accepted + 1 Modified row | `HandleGetCodeAgreement(pgDb, logger, days:30, ct)` | `agreementRate=0.5`; `modified=1` | `Assert.Equal(0.5, body.GetProperty("agreementRate").GetDouble(), 4)`; `Assert.Equal(1, body.GetProperty("modified").GetInt32())` |
| TC-004 | negative | Zero actioned → agreementRate=null + message (AC-004) `[SOURCE:INPUT]` | Empty `MedicalCodeSuggestions` table | `HandleGetCodeAgreement(pgDb, logger, days:30, ct)` | Status 200; `agreementRate=null`; `totalActioned=0`; `message` property present | `Assert.Equal(JsonValueKind.Null, body.GetProperty("agreementRate").ValueKind)`; `Assert.Equal(0, totalActioned)` |
| TC-005 | negative | `days=0` → 422 (AC-005) `[SOURCE:INPUT]` | — | `HandleGetCodeAgreement(pgDb, logger, days:0, ct)` | Status 422 | `Assert.Equal(422, status)` |
| TC-006 | negative | `days=-5` → 422 (AC-005) `[SOURCE:INPUT]` | — | `HandleGetCodeAgreement(pgDb, logger, days:-5, ct)` | Status 422 | `Assert.Equal(422, status)` |
| TC-007 | positive | `days=400` → capped to 365 + WARNING; `windowDays=365` in response (AC-005) `[SOURCE:INPUT]` | Seed 1 Accepted with `VerifiedAt=UtcNow.AddDays(-200)` | `HandleGetCodeAgreement(pgDb, logger, days:400, ct)` | Status 200; `windowDays=365`; suggestion included (at -200 days, within 365-day window) | `Assert.Equal(365, body.GetProperty("windowDays").GetInt32())`; `Assert.Equal(1, totalActioned)` |
| TC-008 | positive | `days` not provided → defaults to 30; older suggestion excluded `[SOURCE:INPUT]` | Seed 1 Accepted with `VerifiedAt=UtcNow.AddDays(-40)` | `HandleGetCodeAgreement(pgDb, logger)` (no days param) | `totalActioned=0`; `windowDays=30` | `Assert.Equal(0, totalActioned)`; `Assert.Equal(30, body.GetProperty("windowDays").GetInt32())` |
| TC-009 | positive | Custom `?days=7` → only suggestions within last 7 days counted; `windowDays=7` `[SOURCE:INPUT]` | Seed 1 Accepted at `UtcNow.AddDays(-3)` + 1 Accepted at `UtcNow.AddDays(-10)` | `HandleGetCodeAgreement(pgDb, logger, days:7, ct)` | `totalActioned=1`; `windowDays=7` | `Assert.Equal(1, body.GetProperty("totalActioned").GetInt32())`; `Assert.Equal(7, body.GetProperty("windowDays").GetInt32())` |
| TC-010 | positive | Pending suggestions excluded from actioned count `[SOURCE:INPUT]` | Seed 1 Pending + 1 Accepted | `HandleGetCodeAgreement(pgDb, logger, days:30, ct)` | `totalActioned=1` (Pending not counted) | `Assert.Equal(1, body.GetProperty("totalActioned").GetInt32())` |
| EC-001 | edge_case | Mixed ICD-10 and CPT rows counted together in agreement rate `[SOURCE:INPUT]` | Seed 1 ICD-10 Accepted + 1 CPT Accepted (both unmodified) | `HandleGetCodeAgreement(pgDb, logger, days:30, ct)` | `totalActioned=2`; `agreementRate=1.0` | `Assert.Equal(2, totalActioned)`; `Assert.Equal(1.0, agreementRate)` |
| EC-002 | edge_case | agreementRate rounded to 4 decimal places `[SOURCE:INFERRED]` Basis: `Math.Round(agreementRate, 4)` in source | Seed 1 Accepted (unmodified) + 2 Rejected (totalActioned=3) | `HandleGetCodeAgreement(pgDb, logger, days:30, ct)` | `agreementRate=0.3333` (1/3 rounded to 4dp) | `Assert.Equal(0.3333, body.GetProperty("agreementRate").GetDouble(), 4)` |

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| EXISTS | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/CodeAgreementMetricEndpointTests.cs` | TC-001 through EC-002 (13 test methods covering all AC-001 through AC-005 + edge cases) |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ClinicalDbContext` | In-Memory EF Core | `UseInMemoryDatabase(Guid.NewGuid().ToString())`; `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` | Per-test isolated PG store |
| `ILogger<CodeAgreementMetricEndpoint>` | `NullLogger<CodeAgreementMetricEndpoint>.Instance` | No-op (WARNING log for days>365 is verified by behaviour, not log assertion) | — |

### Helper Pattern

```csharp
private static ClinicalDbContext BuildPgDb()
{
    var opts = new DbContextOptionsBuilder<ClinicalDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options;
    return new ClinicalDbContext(opts);
}

private static void SeedSuggestion(
    ClinicalDbContext pgDb,
    SuggestionStatus  status,
    string            suggestedCode  = "J18.9",
    string?           committedCode  = null,
    DateTime?         verifiedAt     = null,
    int               patientId      = 1)
{
    pgDb.MedicalCodeSuggestions.Add(new MedicalCodeSuggestion
    {
        PatientId = patientId, CodeType = CodeType.ICD10,
        SuggestedCode = suggestedCode, CommittedCode = committedCode,
        CodeDescription = "Test", ConfidenceScore = 0.85,
        Status = status, VerifiedAt = verifiedAt ?? DateTime.UtcNow
    });
    pgDb.SaveChanges();
}

private static JsonElement GetBody(IResult result)
{
    var value = result.GetType().GetProperty("Value")?.GetValue(result);
    return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(value));
}

private static int? GetStatusCode(IResult result) =>
    (int?)result.GetType().GetProperty("StatusCode")?.GetValue(result);
```

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| 2 unmodified Accepted + 1 Rejected | All `VerifiedAt=UtcNow` | `agreementRate≈0.6667`; `totalActioned=3` |
| 1 unmodified Accepted + 1 Modified | `committedCode="Z00.0"` on second row | `agreementRate=0.5`; `modified=1` |
| Empty table | — | `agreementRate=null`; `totalActioned=0`; `message` present |
| `days=0` | — | 422 |
| `days=-5` | — | 422 |
| `days=400` | Suggestion at `UtcNow.AddDays(-200)` | `windowDays=365`; `totalActioned=1` |
| Default `days=30` | Suggestion at `UtcNow.AddDays(-40)` | `totalActioned=0`; `windowDays=30` |
| `days=7` | Suggestion at `-3d` + `-10d` | `totalActioned=1`; `windowDays=7` |
| 1 Pending + 1 Accepted | Both `VerifiedAt=UtcNow` | `totalActioned=1` (Pending excluded) |
| 1 ICD-10 Accepted + 1 CPT Accepted | Both unmodified | `totalActioned=2`; `agreementRate=1.0` |

## Test Commands

- **Run Tests**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~CodeAgreementMetricEndpointTests"`
- **Run with Coverage**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~CodeAgreementMetricEndpointTests"`
- **Run Single Test**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~CodeAgreementMetricEndpointTests.GetCodeAgreement_Returns422_WhenDaysIsZero"`

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**: `days<=0` → 422; `days>365` → cap + WARNING; `totalActioned==0` → null rate path; agreement rate formula; `modified` counter (explicit `Modified` status + Accepted with different `committedCode`); window date filter; Pending exclusion; default-days fallback; DTO field completeness; `Math.Round` 4dp

## Documentation References

- **Framework Docs**: [xUnit.net](https://xunit.net/docs/getting-started/netcore/cmdline)
- **Mocking Guide**: [Moq Quickstart](https://github.com/moq/moq4/wiki/Quickstart)
- **ASP.NET Core Role-Based Authorization**: [Role-Based Auth](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles)
- **EF Core InMemory**: [InMemory Provider](https://learn.microsoft.com/en-us/ef/core/providers/in-memory/)

## Implementation Checklist

- [x] Create `CodeAgreementMetricEndpointTests.cs` with `BuildPgDb()`, `SeedSuggestion()`, `GetBody()`, `GetStatusCode()` helpers
- [x] Implement DTO field completeness test (TC-001)
- [x] Implement agreement rate calculation tests (TC-002, TC-003, EC-002)
- [x] Implement zero-actioned null rate test (TC-004)
- [x] Implement `days` validation tests — zero, negative, >365 cap, default, custom (TC-005 through TC-009)
- [x] Implement Pending exclusion test (TC-010)
- [x] Implement mixed ICD-10 + CPT edge case (EC-001)
- [x] Run test suite and validate all tests pass
