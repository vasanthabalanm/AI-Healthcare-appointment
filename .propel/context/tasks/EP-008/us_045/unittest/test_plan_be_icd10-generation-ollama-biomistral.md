# Unit Test Plan - TASK_045

## Requirement Reference
- **User Story**: us_045
- **Story Location**: `.propel/context/tasks/EP-008/us_045/us_045.md`
- **Layer**: BE
- **Related Test Plans**: `EP-008/us_046/unittest/test_plan_be_cpt-generation-ollama-biomistral.md` (CPT path shares `GenerateCodesEndpoint` and `OllamaCodeGenerationService`)
- **Acceptance Criteria Covered**:
  - AC-001: `POST /patients/{id}/generate-codes?type=ICD10` → 409 if patient not Verified; 202 + `GenerateIcd10CodesJob` enqueued if Verified
  - AC-002: Hangfire job calls `IOllamaCodeGenerationService.GenerateIcd10Async`; inserts `MedicalCodeSuggestion` rows with `codeType=ICD10, status=Pending`
  - AC-003: `confidence_score < 0.60` → `lowConfidenceFlag=true`; `>= 0.60` → `false`
  - AC-004: Ollama timeout/unavailability → exception rethrown so Hangfire retries; dead-letter after 3 failures
  - AC-005: Dead-letter path: `SaveChanges` failure → rollback; no partial Pending rows committed
  - AI-ACR: ICD-10 code format validation (`[A-Z][0-9]{2}(\.[0-9]{1,4})?`) inside `OllamaCodeGenerationService`; max 20 suggestions

## Test Plan Overview

Tests three components across two test files:

1. **`GenerateCodesEndpoint.HandleGenerateCodes`** (endpoint guard logic — AC-001) via `GenerateCodesEndpointTests.cs`.
2. **`GenerateIcd10CodesJob.ExecuteAsync`** (job insertion logic — AC-002, AC-003, AC-005) via `GenerateIcd10CodesJobTests.cs`.
3. **`OllamaCodeGenerationService.GenerateIcd10Async`** (AI parsing/validation — AI-ACR) via `OllamaCodeGenerationServiceTests.cs`.

`IOllamaCodeGenerationService` is mocked via Moq for job-level tests; `OllamaCodeGenerationService` is tested directly with a mocked `HttpMessageHandler` for AI parsing tests.
`ClinicalDbContext` uses `UseInMemoryDatabase(Guid.NewGuid().ToString())` + `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))`.
`ApplicationDbContext` uses the same InMemory pattern.
`IBackgroundJobClient` is mocked via `Mock<IBackgroundJobClient>(MockBehavior.Loose)` with `Create(Job, IState)` returning a Guid string.
`ILogger<T>` uses `NullLogger<T>.Instance`.

**Gaps noted:**
- AC-004 retry delays (30 s / 60 s / 120 s) are enforced by the `[AutomaticRetry]` attribute at the Hangfire server level and cannot be tested in unit tests. The test verifies only that exceptions propagate (so Hangfire can handle them).

## Dependent Tasks

- TASK_001 (Entities) — `MedicalCodeSuggestion`, `CodeType`, `SuggestionStatus`, `UserAccount`, `VerificationStatus`
- TASK_001 (Data) — `ApplicationDbContext.UserAccounts`, `ClinicalDbContext.MedicalCodeSuggestions`
- TASK_045 — `GenerateCodesEndpoint`, `GenerateIcd10CodesJob`, `OllamaCodeGenerationService`, `IOllamaCodeGenerationService`, `CodeSuggestionDto`

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `GenerateCodesEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Coding/GenerateCodesEndpoint.cs` | Route guard: 400 unsupported type; 404 patient not found; 409 unverified; 202 + enqueue job |
| `GenerateIcd10CodesJob` | class | `src/ClinicalHealthcare.Infrastructure/Jobs/GenerateIcd10CodesJob.cs` | Build patient summary; call Ollama; insert `MedicalCodeSuggestion` rows in transaction |
| `OllamaCodeGenerationService` | class | `src/ClinicalHealthcare.Infrastructure/AI/OllamaCodeGenerationService.cs` | Call `localhost:11434/api/chat`; parse JSON array; validate ICD-10 format; cap at 20; set confidence |
| `IOllamaCodeGenerationService` | interface | `src/ClinicalHealthcare.Infrastructure/AI/IOllamaCodeGenerationService.cs` | Mocked in job tests |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | Verified patient + type=ICD10 → 202 + job enqueued `[SOURCE:INPUT]` | Seed `UserAccount(Id=1, VerificationStatus=Verified)`; `Mock<IBackgroundJobClient>` | `HandleGenerateCodes(1, "ICD10", db, jobs, ct)` | Status 202; `jobs.Create` called once with `GenerateIcd10CodesJob` | `Assert.Equal(202, status)`; `jobs.Verify(j => j.Create(It.Is<Job>(j => j.Type == typeof(GenerateIcd10CodesJob)), It.IsAny<IState>()), Times.Once)` |
| TC-002 | negative | Unverified patient + type=ICD10 → 409 `[SOURCE:INPUT]` | Seed `UserAccount(Id=1, VerificationStatus=Unverified)` | `HandleGenerateCodes(1, "ICD10", db, jobs, ct)` | Status 409; no job enqueued | `Assert.Equal(409, status)`; `jobs.Verify(... Times.Never)` |
| TC-003 | negative | Patient not found → 404 `[SOURCE:INPUT]` | Empty `UserAccounts` table | `HandleGenerateCodes(999, "ICD10", db, jobs, ct)` | Status 404 | `Assert.Equal(404, status)` |
| TC-004 | negative | Unsupported type (e.g. "SNOMED") → 400 `[SOURCE:INPUT]` | Seed any patient | `HandleGenerateCodes(1, "SNOMED", db, jobs, ct)` | Status 400 | `Assert.Equal(400, status)` |
| TC-005 | positive | Job inserts rows with codeType=ICD10, status=Pending `[SOURCE:INPUT]` | `IOllamaCodeGenerationService` returns 2 `CodeSuggestionDto`s; seed `ExtractedClinicalField` rows | `job.ExecuteAsync(1, ct)` | 2 rows in `MedicalCodeSuggestions`; all `CodeType=ICD10`, `Status=Pending` | `Assert.Equal(2, rows.Count)`; `Assert.All(rows, r => Assert.Equal(CodeType.ICD10, r.CodeType))`; `Assert.All(rows, r => Assert.Equal(SuggestionStatus.Pending, r.Status))` |
| TC-006 | positive | confidence < 0.60 → LowConfidenceFlag=true `[SOURCE:INPUT]` | Ollama returns `ConfidenceScore=0.45` | `job.ExecuteAsync(1, ct)` | Row has `LowConfidenceFlag=true` | `Assert.True(row.LowConfidenceFlag)` |
| TC-007 | positive | confidence >= 0.60 → LowConfidenceFlag=false `[SOURCE:INPUT]` | Ollama returns `ConfidenceScore=0.60` and `0.85` | `job.ExecuteAsync(1, ct)` | All rows have `LowConfidenceFlag=false` | `Assert.All(rows, r => Assert.False(r.LowConfidenceFlag))` |
| TC-008 | negative | Ollama returns 0 suggestions → 0 rows inserted; no exception `[SOURCE:INPUT]` | `IOllamaCodeGenerationService.GenerateIcd10Async` → `[]` | `job.ExecuteAsync(1, ct)` | `MedicalCodeSuggestions` table empty | `Assert.Empty(await pgDb.MedicalCodeSuggestions.ToListAsync())` |
| TC-009 | negative | SaveChanges failure → exception rethrown; no partial rows (AC-005) `[SOURCE:INPUT]` | `Mock<ClinicalDbContext>` with `SaveChangesAsync` → `ThrowsAsync`; Ollama returns 1 suggestion | `job.ExecuteAsync(1, ct)` | Exception propagates; 0 rows committed | `await Assert.ThrowsAsync<InvalidOperationException>(...)`; verify no rows in real InMemory DB |
| TC-010 | positive | ICD-10 code format valid — `OllamaCodeGenerationService` accepts `[A-Z][0-9]{2}(\.[0-9]{1,4})?` patterns `[SOURCE:INPUT]` | HTTP mock returns `[{"code":"J18.9","description":"Pneumonia","confidence":0.92}]` | `svc.GenerateIcd10Async("summary", ct)` | Returns `Count=1`; `SuggestedCode="J18.9"` | `Assert.Equal(1, results.Count)`; `Assert.Equal("J18.9", results[0].SuggestedCode)` |
| TC-011 | negative | Malformed ICD-10 code → rejected + WARNING logged `[SOURCE:INPUT]` | HTTP mock returns `[{"code":"INVALID","description":"bad","confidence":0.80}]` | `svc.GenerateIcd10Async("summary", ct)` | Returns `Count=0` | `Assert.Empty(results)` |
| TC-012 | negative | >20 valid codes returned → only first 20 inserted; extras dropped `[SOURCE:INPUT]` | HTTP mock returns JSON array with 25 valid codes | `svc.GenerateIcd10Async("summary", ct)` | `results.Count == 20` | `Assert.Equal(20, results.Count)` |
| EC-001 | edge_case | Empty Ollama response (empty JSON array `[]`) → 0 rows; job completes cleanly `[SOURCE:INFERRED]` | HTTP mock returns `{"message":{"content":"[]"}}` | `svc.GenerateIcd10Async("summary", ct)` | Empty list | `Assert.Empty(results)` |
| EC-002 | edge_case | Confidence exactly 0.60 → LowConfidenceFlag=false (threshold is exclusive `<`) `[SOURCE:INPUT]` | Ollama returns `ConfidenceScore=0.60` | `job.ExecuteAsync(1, ct)` | Row has `LowConfidenceFlag=false` | `Assert.False(row.LowConfidenceFlag)` |
| EC-003 | edge_case | Confidence 0.599 → LowConfidenceFlag=true `[SOURCE:INPUT]` | Ollama returns `ConfidenceScore=0.599` | `job.ExecuteAsync(1, ct)` | Row has `LowConfidenceFlag=true` | `Assert.True(row.LowConfidenceFlag)` |

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| EXISTS | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/GenerateCodesEndpointTests.cs` | TC-001 through TC-004 (endpoint guard tests, shared with US-046) |
| EXISTS | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/GenerateIcd10CodesJobTests.cs` | TC-005 through TC-009 (job insertion + rollback tests) |
| EXISTS | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/OllamaCodeGenerationServiceTests.cs` | TC-010 through EC-001 (AI parsing + validation tests, shared with US-046) |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ApplicationDbContext` | In-Memory EF Core | `UseInMemoryDatabase(Guid.NewGuid().ToString())`; `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` | Per-test isolated store |
| `ClinicalDbContext` | In-Memory EF Core | Same pattern as `ApplicationDbContext` | Per-test isolated store |
| `IOllamaCodeGenerationService` | `Mock<IOllamaCodeGenerationService>` | `.Setup(s => s.GenerateIcd10Async(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(list)` | Configurable `IReadOnlyList<CodeSuggestionDto>` |
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
    mock.Setup(s => s.GenerateIcd10Async(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(returnValue);
    return mock.Object;
}

// Hangfire enqueue verify pattern (v1.8 IBackgroundJobClient)
jobs.Verify(j => j.Create(
    It.Is<Job>(job => job.Type == typeof(GenerateIcd10CodesJob)),
    It.IsAny<IState>()), Times.Once);

// HTTP mock for OllamaCodeGenerationService
private static string WrapInOllamaChatResponse(string contentJson) =>
    $$"""{"model":"biomistral","message":{"role":"assistant","content":{{JsonSerializer.Serialize(contentJson)}}},"done":true}""";
```

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Verified patient + ICD10 | `UserAccount(VerificationStatus=Verified)` | Status 202; `GenerateIcd10CodesJob` enqueued |
| Unverified patient | `UserAccount(VerificationStatus=Unverified)` | Status 409; no job |
| Patient not found | No matching `UserAccount` | Status 404 |
| 2 valid Ollama suggestions | `[{code:"J18.9", confidence:0.92}, {code:"I10", confidence:0.88}]` | 2 rows; both `CodeType=ICD10`; `LowConfidenceFlag=false` |
| Low-confidence suggestion | `confidence=0.45` | `LowConfidenceFlag=true` |
| Threshold boundary 0.60 | `confidence=0.60` | `LowConfidenceFlag=false` |
| Threshold boundary 0.599 | `confidence=0.599` | `LowConfidenceFlag=true` |
| Malformed code "INVALID" | Code fails `[A-Z][0-9]{2}(\.[0-9]{1,4})?` | Rejected; 0 rows |
| 25 valid codes | 25-element array | 20 rows (5 dropped) |
| Empty Ollama response | `"[]"` JSON | 0 rows |

## Test Commands

- **Run Tests (endpoint)**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~GenerateCodesEndpointTests"`
- **Run Tests (job)**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~GenerateIcd10CodesJobTests"`
- **Run Tests (service)**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~OllamaCodeGenerationServiceTests"`
- **Run All EP-008 Tests**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~GenerateCodesEndpointTests|FullyQualifiedName~GenerateIcd10CodesJobTests|FullyQualifiedName~OllamaCodeGenerationServiceTests"`
- **Run with Coverage**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --collect:"XPlat Code Coverage"`

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**: Verified/Unverified guard; 404 patient path; unsupported type path; 202 enqueue; job transaction commit; job rollback on failure; `LowConfidenceFlag` branches (< 0.60 / >= 0.60); 0-suggestion early return; ICD-10 regex accept/reject; 20-code cap; empty JSON array; malformed JSON envelope

## Documentation References

- **Framework Docs**: [xUnit.net](https://xunit.net/docs/getting-started/netcore/cmdline)
- **Mocking Guide**: [Moq Quickstart](https://github.com/moq/moq4/wiki/Quickstart)
- **Hangfire IBackgroundJobClient**: [Hangfire Client](https://docs.hangfire.io/en/latest/background-methods/calling-methods-in-background.html)
- **Ollama API**: [Ollama REST API](https://github.com/ollama/ollama/blob/main/docs/api.md)
- **AI Reference**: AIR-003 — BioMistral ICD-10 generation

## Implementation Checklist

- [x] Create `GenerateCodesEndpointTests.cs` with Verified/Unverified/404/400 guards
- [x] Create `GenerateIcd10CodesJobTests.cs` with AC-002/AC-003/AC-005 tests
- [x] Set up `BuildPgDb()` and `BuildOllamaService()` helpers in `GenerateIcd10CodesJobTests.cs`
- [x] Implement `LowConfidenceFlag` boundary tests (TC-006, TC-007, EC-002, EC-003)
- [x] Implement rollback / no-partial-rows test (TC-009)
- [x] Create `OllamaCodeGenerationServiceTests.cs` with ICD-10 format validation and 20-code cap
- [x] Run test suite and validate all tests pass
