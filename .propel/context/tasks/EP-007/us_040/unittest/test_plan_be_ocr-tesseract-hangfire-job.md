# Unit Test Plan - TASK_040

## Requirement Reference
- **User Story**: us_040
- **Story Location**: `.propel/context/tasks/EP-007/us_040/us_040.md`
- **Layer**: BE
- **Related Test Plans**: `EP-007/us_041/unittest/test_plan_be_clinical-field-extraction-nlp.md` (ExtractClinicalFieldsJob is enqueued from OcrDocumentJob)
- **Acceptance Criteria Covered**:
  - AC-001: Hangfire `OcrDocumentJob` decrypts blob via `IAesEncryptionService`, calls `ITesseractOcrService.OcrAsync`, stores `RawOcrText` + `OcrStatus` on `ClinicalDocument`
  - AC-002: Confidence ≥ 0.75 → `OcrStatus=Extracted`; `ExtractClinicalFieldsJob` enqueued
  - AC-003: Confidence < 0.75 → `OcrStatus=LowConfidence`; `ExtractClinicalFieldsJob` still enqueued (source behaviour; US says "not enqueued" — gap documented below)
  - AC-004: Empty OCR text → `OcrStatus=NoData`; no field-extraction job enqueued
  - AC-005: Job exception → `OcrStatus=NoData` saved; exception rethrown so Hangfire retries; decorated `[AutomaticRetry(Attempts=3)]`

## Test Plan Overview

Tests `OcrDocumentJob.ExecuteAsync(documentId, cancellationToken)` (static-equivalent Hangfire job method).
`ITesseractOcrService` is mocked via Moq (`OcrAsync` returns `(rawText, confidence)` tuple).
`IAesEncryptionService` is mocked via Moq (`Decrypt` returns a `MemoryStream`).
`IBackgroundJobClient` is a Moq mock for job-chain verification.
`ApplicationDbContext` uses InMemory EF Core with per-test `Guid.NewGuid().ToString()` database name plus
`ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))`.
`ILogger<OcrDocumentJob>` uses `NullLogger<OcrDocumentJob>.Instance`.

**Gaps noted:**
- AC-003 specifies that LowConfidence does **not** enqueue the field-extraction job; the source (`OcrDocumentJob.cs`)
  enqueues `ExtractClinicalFieldsJob` for **any** `OcrStatus != NoData`, including `LowConfidence`.
  Test `TC-002` verifies source behaviour (job **is** enqueued for LowConfidence) and documents the divergence
  as `[SOURCE:INPUT]`.
- AC-005 specifies `OcrStatus` remains `Pending` during retries; the source sets `OcrStatus=NoData` on the first
  exception and saves before rethrowing. Test `TC-005` verifies source behaviour as `[SOURCE:INPUT]`.

## Dependent Tasks

- TASK_001 (Entities) — `ClinicalDocument`, `OcrStatus` enum
- TASK_001 (Data) — `ApplicationDbContext.ClinicalDocuments`
- TASK_040 — `OcrDocumentJob`, `ITesseractOcrService`
- TASK_038 — `IAesEncryptionService` interface (shared)
- TASK_041 — `ExtractClinicalFieldsJob` (enqueued from this job)

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `OcrDocumentJob` | class | `src/ClinicalHealthcare.Infrastructure/Jobs/OcrDocumentJob.cs` | Decrypt blob; run OCR; map confidence to OcrStatus; store RawOcrText; enqueue ExtractClinicalFieldsJob |
| `ITesseractOcrService` | interface | `src/ClinicalHealthcare.Infrastructure/OCR/ITesseractOcrService.cs` | `OcrAsync(Stream, CancellationToken) → (string RawText, float AverageConfidence)` — mocked |
| `IAesEncryptionService` | interface | `src/ClinicalHealthcare.Infrastructure/Security/IAesEncryptionService.cs` | `Decrypt(string) → Stream` — mocked |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | High-confidence OCR → OcrStatus=Extracted; RawOcrText stored; extraction job enqueued `[SOURCE:INPUT]` | Seed `ClinicalDocument(Id=1, PatientId=2, EncryptedBlobPath="enc/a.bin")`; `aes.Decrypt(...)` → `new MemoryStream(new byte[64])`; `ocr.OcrAsync(...)` → `("BP: 120/80\nAllergy to penicillin", 0.90f)` | `job.ExecuteAsync(1, CancellationToken.None)` | `doc.OcrStatus==Extracted`; `doc.RawOcrText=="BP: 120/80\nAllergy to penicillin"`; `jobs.Create(ExtractClinicalFieldsJob, ...)` called once | `Assert.Equal(OcrStatus.Extracted, doc.OcrStatus)`; `Assert.Equal(rawText, doc.RawOcrText)`; `jobsMock.Verify(Times.Once)` |
| TC-002 | positive | Low-confidence OCR → OcrStatus=LowConfidence; RawOcrText stored; extraction job enqueued **[SOURCE:INPUT] Gap: US AC-003 says no extraction job for LowConfidence; source enqueues regardless** | `ocr.OcrAsync(...)` → `("partial text", 0.50f)` | `job.ExecuteAsync(1, ...)` | `doc.OcrStatus==LowConfidence`; `doc.RawOcrText=="partial text"`; extraction job enqueued once | `Assert.Equal(OcrStatus.LowConfidence, doc.OcrStatus)`; `jobsMock.Verify(Times.Once)` |
| TC-003 | negative | Empty OCR text → OcrStatus=NoData; extraction job NOT enqueued `[SOURCE:INPUT]` | `ocr.OcrAsync(...)` → `("", 0.0f)` | `job.ExecuteAsync(1, ...)` | `doc.OcrStatus==NoData`; `doc.RawOcrText` null/empty; no extraction job enqueued | `Assert.Equal(OcrStatus.NoData, doc.OcrStatus)`; `jobsMock.Verify(j => j.Create(...), Times.Never)` |
| TC-004 | negative | Document not found → early return; no exception; no DB mutation `[SOURCE:INFERRED]` Basis: `OcrDocumentJob.ExecuteAsync` loads `doc` by `SingleOrDefaultAsync`; if null → logs warning + returns. | Empty `ClinicalDocuments` table | `job.ExecuteAsync(9999, ...)` | No exception thrown; DB unchanged | `await job.ExecuteAsync(9999, ...)` completes without exception; `db.ClinicalDocuments.Count()==0` |
| TC-005 | negative | ITesseractOcrService throws → OcrStatus=NoData saved; exception rethrown **[SOURCE:INPUT] Gap: US says status remains Pending; source sets NoData before rethrow** | `ocr.OcrAsync(...)` → `ThrowsAsync(new InvalidOperationException("OCR failure"))` | `job.ExecuteAsync(1, ...)` | `InvalidOperationException` rethrown; `doc.OcrStatus==NoData` in DB | `await Assert.ThrowsAsync<InvalidOperationException>(...)`; `doc.OcrStatus==OcrStatus.NoData` |
| TC-006 | negative | IAesEncryptionService throws → OcrStatus=NoData saved; exception rethrown `[SOURCE:INFERRED]` Basis: AES decrypt failure is caught by the same try/catch in `ExecuteAsync` that handles OCR failures. | `aes.Decrypt(...)` → `Throws(new InvalidOperationException("decrypt fail"))` | `job.ExecuteAsync(1, ...)` | `InvalidOperationException` rethrown; `doc.OcrStatus==NoData` | `Assert.ThrowsAsync<InvalidOperationException>`; `doc.OcrStatus==OcrStatus.NoData` |
| EC-001 | edge_case | Confidence exactly 0.75 → OcrStatus=Extracted (inclusive lower bound) `[SOURCE:INPUT]` | `ocr.OcrAsync(...)` → `("text", 0.75f)` | `job.ExecuteAsync(1, ...)` | `doc.OcrStatus==Extracted` | `Assert.Equal(OcrStatus.Extracted, doc.OcrStatus)` |
| EC-002 | edge_case | Confidence just below 0.75 (0.749f) → OcrStatus=LowConfidence `[SOURCE:INPUT]` | `ocr.OcrAsync(...)` → `("text", 0.749f)` | `job.ExecuteAsync(1, ...)` | `doc.OcrStatus==LowConfidence` | `Assert.Equal(OcrStatus.LowConfidence, doc.OcrStatus)` |

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/OcrDocumentJobTests.cs` | TC-001 through EC-002 (8 test methods) |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ApplicationDbContext` | In-Memory EF Core | `UseInMemoryDatabase(Guid.NewGuid().ToString())`; `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` | Per-test isolated store |
| `ITesseractOcrService` | `Mock<ITesseractOcrService>` | `.Setup(o => o.OcrAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ReturnsAsync(("text", 0.90f))` | Configurable `(string, float)` tuple |
| `IAesEncryptionService` | `Mock<IAesEncryptionService>` | `.Setup(a => a.Decrypt(It.IsAny<string>())).Returns(new MemoryStream(new byte[64]))` | In-memory stream |
| `IBackgroundJobClient` | `Mock<IBackgroundJobClient>` | No-op default; verify `Create(job => job.Type==typeof(ExtractClinicalFieldsJob), ...)` for TC-001/TC-002 and `Times.Never` for TC-003 | Mock object |
| `ILogger<OcrDocumentJob>` | `NullLogger<OcrDocumentJob>.Instance` | No-op | — |

### Helper Pattern

```csharp
private static ApplicationDbContext BuildSqlDb()
{
    var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options;
    return new ApplicationDbContext(opts);
}

private static ClinicalDocument SeedDocument(ApplicationDbContext db, int id = 1, int patientId = 2)
{
    var doc = new ClinicalDocument
    {
        Id                = id,
        PatientId         = patientId,
        EncryptedBlobPath = "enc/a.bin",
        UploadedByStaffId = 1,
        OriginalFileName  = "scan.pdf",
        OcrStatus         = OcrStatus.Pending
    };
    db.ClinicalDocuments.Add(doc);
    db.SaveChanges();
    return doc;
}

// Hangfire enqueue verify pattern (v1.8 IBackgroundJobClient)
jobsMock.Verify(j => j.Create(
    It.Is<Job>(job => job.Type == typeof(ExtractClinicalFieldsJob)),
    It.IsAny<IState>()), Times.Once);
```

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| High-confidence OCR | `("BP: 120/80", 0.90f)`; `ClinicalDocument(Id=1)` | OcrStatus=Extracted; RawOcrText stored; ExtractClinicalFieldsJob enqueued |
| Low-confidence OCR | `("partial text", 0.50f)` | OcrStatus=LowConfidence; RawOcrText stored; ExtractClinicalFieldsJob enqueued [gap] |
| Empty OCR text | `("", 0.0f)` | OcrStatus=NoData; no enqueue |
| Document not found | No ClinicalDocument row for id=9999 | Early return; no exception |
| OCR throws | `InvalidOperationException` from OcrAsync | OcrStatus=NoData saved; exception rethrown |
| AES throws | `InvalidOperationException` from Decrypt | OcrStatus=NoData saved; exception rethrown |
| Boundary confidence 0.75 | `("text", 0.75f)` | OcrStatus=Extracted |
| Boundary confidence 0.749 | `("text", 0.749f)` | OcrStatus=LowConfidence |

## Test Commands

- **Run Tests**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~OcrDocumentJobTests"`
- **Run with Coverage**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~OcrDocumentJobTests"`
- **Run Single Test**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~OcrDocumentJobTests.OcrJob_HighConfidence_SetsExtractedAndEnqueuesExtraction"`

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**: Document not found early return; AES decrypt call; OCR async call; confidence threshold branches (≥0.75/< 0.75/empty); OcrStatus assignment; RawOcrText assignment; SaveChangesAsync; ExtractClinicalFieldsJob enqueue guard; exception catch + NoData set + rethrow

## Documentation References

- **Framework Docs**: [xUnit.net](https://xunit.net/docs/getting-started/netcore/cmdline)
- **Mocking Guide**: [Moq Quickstart](https://github.com/moq/moq4/wiki/Quickstart)
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/UploadDocumentEndpointTests.cs`
- **Hangfire IBackgroundJobClient**: [Hangfire Client](https://docs.hangfire.io/en/latest/background-methods/calling-methods-in-background.html)
- **Tesseract .NET**: [Tesseract NuGet](https://www.nuget.org/packages/Tesseract)

## Implementation Checklist

- [x] Create test file `tests/ClinicalHealthcare.Infrastructure.Tests/Features/OcrDocumentJobTests.cs`
- [x] Set up `BuildSqlDb()` and `SeedDocument()` helpers
- [x] Set up `Mock<ITesseractOcrService>`, `Mock<IAesEncryptionService>`, `Mock<IBackgroundJobClient>` helpers
- [x] Implement positive test cases (TC-001, TC-002)
- [x] Implement negative test cases (TC-003, TC-004, TC-005, TC-006)
- [x] Implement edge case tests (EC-001, EC-002)
- [x] Run test suite and validate all 8 tests pass
