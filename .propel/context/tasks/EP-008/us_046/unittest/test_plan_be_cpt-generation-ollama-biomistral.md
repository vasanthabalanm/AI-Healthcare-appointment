# Unit Test Plan - TASK_046

## Requirement Reference
- **User Story**: us_046
- **Story Location**: `.propel/context/tasks/EP-008/us_046/us_046.md`
- **Layer**: BE
- **Related Test Plans**: `EP-008/us_045/unittest/test_plan_be_icd10-generation-ollama-biomistral.md` (CPT path shares `GenerateCodesEndpoint` and `OllamaCodeGenerationService`)
- **Acceptance Criteria Covered**:
  - AC-001: `POST /patients/{id}/generate-codes?type=CPT` → uses distinct CPT prompt; enqueues `GenerateCptCodesJob` (separate from ICD-10 job)
  - AC-002: `MedicalCodeSuggestion` rows stored with `codeType=CPT`; no ICD-10 rows created by CPT job
  - AC-003: Ollama "No procedures identified" → 0 rows inserted; job completes cleanly
  - AC-004: CPT format validation: regex `^\d{5}$`; non-matching → rejected + WARNING logged
  - AI-ACR: ICD-10 format codes in CPT response → discarded; both ICD-10 and CPT jobs produce independent rows

## Test Plan Overview

Tests three components:

1. **`GenerateCodesEndpoint.HandleGenerateCodes`** (CPT dispatch path — AC-001) via `GenerateCodesEndpointTests.cs` (shared with US-045).
2. **`GenerateCptCodesJob.ExecuteAsync`** (job insertion logic — AC-002, AC-003) via `GenerateCptCodesJobTests.cs`.
3. **`OllamaCodeGenerationService.GenerateCptAsync`** (CPT parsing/validation — AC-004, AI-ACR) via `OllamaCodeGenerationServiceTests.cs` (shared with US-045).

`IOllamaCodeGenerationService` is mocked via Moq for job-level tests.
`ClinicalDbContext` uses `UseInMemoryDatabase(Guid.NewGuid().ToString())` + `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))`.
`ApplicationDbContext` uses the same InMemory pattern.
`ILogger<T>` uses `NullLogger<T>.Instance`.

**Gaps noted:**
- `GenerateCptCodesJob` uses `ClinicalDbContext.ExtractedClinicalFields` to build the patient summary (same pattern as `GenerateIcd10CodesJob`). The summary content is not user-visible in test assertions; tests pass empty fields and rely on the fallback `"Patient ID {id} — no extracted clinical fields available."` string.
- The CPT System Prompt (`CptSystemPrompt`) is a `private const` inside `OllamaCodeGenerationService`; tests verify only the parsed output, not the prompt content.

## Dependent Tasks

- TASK_001 (Entities) — `MedicalCodeSuggestion`, `CodeType`, `SuggestionStatus`, `UserAccount`, `VerificationStatus`
- TASK_001 (Data) — `ApplicationDbContext.UserAccounts`, `ClinicalDbContext.MedicalCodeSuggestions`, `ClinicalDbContext.ExtractedClinicalFields`
- TASK_045 — `GenerateCodesEndpoint` (shared); `IOllamaCodeGenerationService`, `CodeSuggestionDto`, `OllamaCodeGenerationService`
- TASK_046 — `GenerateCptCodesJob`

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `GenerateCodesEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Coding/GenerateCodesEndpoint.cs` | Dispatch: enqueues `GenerateCptCodesJob` when `type=CPT` |
| `GenerateCptCodesJob` | class | `src/ClinicalHealthcare.Infrastructure/Jobs/GenerateCptCodesJob.cs` | Build patient summary; call `GenerateCptAsync`; insert `MedicalCodeSuggestion` rows with `codeType=CPT` |
| `OllamaCodeGenerationService` | class | `src/ClinicalHealthcare.Infrastructure/AI/OllamaCodeGenerationService.cs` | `GenerateCptAsync`: call Ollama with `CptSystemPrompt`; validate `^\d{5}$`; cap at 20 |
| `IOllamaCodeGenerationService` | interface | `src/ClinicalHealthcare.Infrastructure/AI/IOllamaCodeGenerationService.cs` | Mocked in job tests |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | Verified patient + type=CPT → 202 + `GenerateCptCodesJob` enqueued (distinct from ICD-10) `[SOURCE:INPUT]` | Seed `UserAccount(Id=2, VerificationStatus=Verified)`; `Mock<IBackgroundJobClient>` | `HandleGenerateCodes(2, "CPT", db, jobs, ct)` | Status 202; `jobs.Create` called with `GenerateCptCodesJob` | `Assert.Equal(202, status)`; `jobs.Verify(j => j.Create(It.Is<Job>(j => j.Type == typeof(GenerateCptCodesJob)), ...))` |
| TC-002 | positive | CPT job inserts rows with codeType=CPT, status=Pending; no ICD-10 rows created `[SOURCE:INPUT]` | `IOllamaCodeGenerationService.GenerateCptAsync` returns 2 CPT `CodeSuggestionDto`s | `job.ExecuteAsync(1, ct)` | 2 rows in `MedicalCodeSuggestions`; all `CodeType=CPT`; `Status=Pending` | `Assert.Equal(2, rows.Count)`; `Assert.All(rows, r => Assert.Equal(CodeType.CPT, r.CodeType))` |
| TC-003 | negative | Ollama returns "No procedures identified" (empty list) → 0 rows; job completes cleanly `[SOURCE:INPUT]` | `IOllamaCodeGenerationService.GenerateCptAsync` returns `[]` | `job.ExecuteAsync(1, ct)` | `MedicalCodeSuggestions` table empty; no exception | `Assert.Empty(await pgDb.MedicalCodeSuggestions.ToListAsync())` |
| TC-004 | negative | CPT format validation — non-`^\d{5}$` code rejected + WARNING `[SOURCE:INPUT]` | HTTP mock returns `[{"code":"9999","description":"short","confidence":0.85}]` (4-digit) | `svc.GenerateCptAsync("summary", ct)` | Returns `Count=0` | `Assert.Empty(results)` |
| TC-005 | negative | ICD-10 format code ("J18.9") in CPT response → discarded `[SOURCE:INFERRED]` Basis: `GenerateCptAsync` uses `CptCodeRegex (^\d{5}$)`; "J18.9" fails that regex | HTTP mock returns `[{"code":"J18.9","description":"ICD code","confidence":0.80}]` | `svc.GenerateCptAsync("summary", ct)` | Returns `Count=0` | `Assert.Empty(results)` |
| TC-006 | positive | Valid 5-digit CPT codes accepted `[SOURCE:INPUT]` | HTTP mock returns `[{"code":"99213","description":"Office Visit","confidence":0.91},{"code":"85025","description":"CBC","confidence":0.87}]` | `svc.GenerateCptAsync("summary", ct)` | Returns `Count=2`; codes preserved | `Assert.Equal(2, results.Count)`; `Assert.Equal("99213", results[0].SuggestedCode)` |
| TC-007 | positive | CPT confidence < 0.60 → LowConfidenceFlag=true `[SOURCE:INPUT]` | `IOllamaCodeGenerationService.GenerateCptAsync` returns `ConfidenceScore=0.42` | `job.ExecuteAsync(1, ct)` | Row has `LowConfidenceFlag=true` | `Assert.True(row.LowConfidenceFlag)` |
| TC-008 | positive | CPT confidence >= 0.60 → LowConfidenceFlag=false `[SOURCE:INPUT]` | `IOllamaCodeGenerationService.GenerateCptAsync` returns `ConfidenceScore=0.60` and `0.80` | `job.ExecuteAsync(1, ct)` | All rows have `LowConfidenceFlag=false` | `Assert.All(rows, r => Assert.False(r.LowConfidenceFlag))` |
| TC-009 | negative | SaveChanges failure → exception rethrown; no partial rows `[SOURCE:INPUT]` | `Mock<ClinicalDbContext>` with `SaveChangesAsync` → `ThrowsAsync`; CPT Ollama returns 1 suggestion | `job.ExecuteAsync(1, ct)` | Exception propagates | `await Assert.ThrowsAsync<InvalidOperationException>(...)` |
| EC-001 | edge_case | Narrative response with extractable 5-digit codes → valid ones inserted `[SOURCE:INPUT]` | HTTP mock returns JSON array containing mixed valid/invalid codes | `svc.GenerateCptAsync("summary", ct)` | Only valid `^\d{5}$` codes in result | `Assert.All(results, r => Assert.Matches(@"^\d{5}$", r.SuggestedCode))` |
| EC-002 | edge_case | ICD-10 and CPT jobs produce independent rows (different `CodeType`) `[SOURCE:INPUT]` | 1 ICD-10 suggestion row + 1 CPT suggestion row seeded directly in `ClinicalDbContext` | Query all `MedicalCodeSuggestions` | 2 rows: 1 ICD-10, 1 CPT | `Assert.Single(rows, r => r.CodeType == CodeType.ICD10)`; `Assert.Single(rows, r => r.CodeType == CodeType.CPT)` |

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| EXISTS | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/GenerateCodesEndpointTests.cs` | TC-001 CPT dispatch test (shared with US-045) |
| EXISTS | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/GenerateCptCodesJobTests.cs` | TC-002 through TC-009 (CPT job insertion + rollback tests) |
| EXISTS | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/OllamaCodeGenerationServiceTests.cs` | TC-004 through EC-001 (CPT format validation tests, shared with US-045) |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ClinicalDbContext` | In-Memory EF Core | `UseInMemoryDatabase(Guid.NewGuid().ToString())`; `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` | Per-test isolated store |
| `IOllamaCodeGenerationService` | `Mock<IOllamaCodeGenerationService>` | `.Setup(s => s.GenerateCptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(list)` | Configurable `IReadOnlyList<CodeSuggestionDto>` |
| `IBackgroundJobClient` | `Mock<IBackgroundJobClient>(MockBehavior.Loose)` | `.Setup(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>())).Returns(Guid.NewGuid().ToString())` | No-op |
| `HttpMessageHandler` (Ollama HTTP) | `Mock<HttpMessageHandler>` | `Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ...)` | Configurable `HttpResponseMessage` |
| `ILogger<T>` | `NullLogger<T>.Instance` | No-op | — |

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

private static IOllamaCodeGenerationService BuildOllamaService(
    IReadOnlyList<CodeSuggestionDto> returnValue)
{
    var mock = new Mock<IOllamaCodeGenerationService>();
    mock.Setup(s => s.GenerateCptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(returnValue);
    return mock.Object;
}

private static GenerateCptCodesJob BuildJob(
    IOllamaCodeGenerationService ollama,
    ClinicalDbContext pgDb) =>
    new(ollama, pgDb, NullLogger<GenerateCptCodesJob>.Instance);

// CPT dispatch verify pattern
jobs.Verify(j => j.Create(
    It.Is<Job>(job => job.Type == typeof(GenerateCptCodesJob)),
    It.IsAny<IState>()), Times.Once);
```

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Verified patient + CPT | `UserAccount(VerificationStatus=Verified)` | Status 202; `GenerateCptCodesJob` enqueued |
| 2 valid CPT suggestions | `[{code:"99213", confidence:0.91}, {code:"85025", confidence:0.87}]` | 2 rows; `CodeType=CPT`; `LowConfidenceFlag=false` |
| Empty CPT response | `[]` | 0 rows |
| 4-digit code "9999" | Fails `^\d{5}$` | Rejected; 0 rows |
| ICD-10 code "J18.9" | Fails `^\d{5}$` | Rejected; 0 rows |
| Low-confidence CPT | `confidence=0.42` | `LowConfidenceFlag=true` |
| Mixed ICD-10 + CPT rows in DB | Both code types seeded | 2 independent rows with distinct `CodeType` values |

## Test Commands

- **Run Tests (CPT job)**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~GenerateCptCodesJobTests"`
- **Run Tests (service — CPT)**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~OllamaCodeGenerationServiceTests"`
- **Run All EP-008 Tests**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~GenerateCodesEndpointTests|FullyQualifiedName~GenerateCptCodesJobTests|FullyQualifiedName~OllamaCodeGenerationServiceTests"`
- **Run with Coverage**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --collect:"XPlat Code Coverage"`

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**: CPT dispatch branch in endpoint; job transaction commit; job rollback; `LowConfidenceFlag` branches; 0-suggestion early return; CPT regex accept/reject; 20-code cap; empty JSON array; malformed JSON

## Documentation References

- **Framework Docs**: [xUnit.net](https://xunit.net/docs/getting-started/netcore/cmdline)
- **Mocking Guide**: [Moq Quickstart](https://github.com/moq/moq4/wiki/Quickstart)
- **Hangfire IBackgroundJobClient**: [Hangfire Client](https://docs.hangfire.io/en/latest/background-methods/calling-methods-in-background.html)
- **Ollama API**: [Ollama REST API](https://github.com/ollama/ollama/blob/main/docs/api.md)
- **AI Reference**: AIR-004 — BioMistral CPT generation

## Implementation Checklist

- [x] Create `GenerateCptCodesJobTests.cs` with AC-002/AC-003 (CPT rows, empty response)
- [x] Set up `BuildPgDb()` and `BuildOllamaService()` helpers (GenerateCptAsync path)
- [x] Implement `LowConfidenceFlag` threshold tests for CPT (TC-007, TC-008)
- [x] Implement rollback / no-partial-rows test for CPT job (TC-009)
- [x] Add CPT format validation tests to `OllamaCodeGenerationServiceTests.cs` (TC-004, TC-005, TC-006)
- [x] Add CPT dispatch test to `GenerateCodesEndpointTests.cs` (TC-001)
- [x] Run test suite and validate all tests pass
