# Unit Test Plan - TASK_041

## Requirement Reference
- **User Story**: us_041
- **Story Location**: `.propel/context/tasks/EP-007/us_041/us_041.md`
- **Layer**: BE
- **Related Test Plans**:
  - `EP-007/us_040/unittest/test_plan_be_ocr-tesseract-hangfire-job.md` (OcrDocumentJob populates `RawOcrText`)
  - `EP-007/us_042/unittest/test_plan_be_deduplication-hangfire-aggregation-job.md` (DeduplicateClinicalFieldsJob enqueued after extraction)
- **Acceptance Criteria Covered**:
  - AC-001: NLP pipeline maps OCR text to `VitalSign`, `MedicalHistory`, `Medication`, `Allergy`, `Diagnosis` field types; `ExtractedClinicalField` rows inserted in PostgreSQL
  - AC-002: Confidence score propagated from `OcrStatus` tier to `ConfidenceScore` on each row (Extracted → 0.90; LowConfidence → 0.60)
  - AC-003: Each row has correct `PatientId` + `DocumentId`; no orphaned null-patient rows
  - AC-004: Unrecognised text → DEBUG log; no Unknown-type row inserted

## Test Plan Overview

Tests `ExtractClinicalFieldsJob.ExecuteAsync(documentId, cancellationToken)` and
`ClinicalFieldExtractor.Extract(rawOcrText)` (real instance — pure in-memory, no external deps).

`ApplicationDbContext` (SQL Server side) and `ClinicalDbContext` (PostgreSQL side) both use InMemory EF Core
with per-test `Guid.NewGuid().ToString()` database names. `IBackgroundJobClient` is mocked for job-chain
verification. `ILogger<ExtractClinicalFieldsJob>` uses `NullLogger`. `ILogger<ClinicalFieldExtractor>` uses
`NullLogger`.

**Gap noted:**
- AC-002 specifies the exact OCR float confidence is propagated to each field; the source uses a heuristic
  (Extracted → 0.90, LowConfidence → 0.60) rather than the precise per-page float.
  Tests `TC-003` and `TC-004` verify the heuristic as `[SOURCE:INPUT]`.

## Dependent Tasks

- TASK_001 (Entities) — `ExtractedClinicalField`, `ClinicalFieldType` enum, `ClinicalDocument`, `OcrStatus`
- TASK_001 (Data) — `ApplicationDbContext.ClinicalDocuments`, `ClinicalDbContext.ExtractedClinicalFields`
- TASK_041 — `ExtractClinicalFieldsJob`, `ClinicalFieldExtractor`, `ExtractedFieldDto`
- TASK_042 — `DeduplicateClinicalFieldsJob` (enqueued from this job)

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `ExtractClinicalFieldsJob` | class | `src/ClinicalHealthcare.Infrastructure/Jobs/ExtractClinicalFieldsJob.cs` | Load doc; guard NoData/empty; idempotency; call extractor; insert fields; enqueue dedup job |
| `ClinicalFieldExtractor` | class | `src/ClinicalHealthcare.Infrastructure/NLP/ClinicalFieldExtractor.cs` | Rule-based regex NLP; 5 field types; first-rule-wins per line; no Unknown rows |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | OcrStatus=Extracted + text → fields inserted; dedup job enqueued `[SOURCE:INPUT]` | Seed `ClinicalDocument(Id=1, PatientId=2, OcrStatus=Extracted, RawOcrText="BP: 120/80\nAspirin 100mg")` | `job.ExecuteAsync(1, ...)` | `pgDb.ExtractedClinicalFields.Count() >= 1`; `DeduplicateClinicalFieldsJob` enqueued | `Assert.True(count >= 1)`; `jobsMock.Verify(Times.Once)` |
| TC-002 | positive | OcrStatus=LowConfidence + text → fields extracted; ConfidenceScore=0.60 `[SOURCE:INPUT]` | `OcrStatus=LowConfidence, RawOcrText="Diagnosis: Hypertension"` | `job.ExecuteAsync(1, ...)` | Fields inserted with `ConfidenceScore==0.60` | `Assert.Equal(0.60, field.ConfidenceScore)` |
| TC-003 | positive | OcrStatus=Extracted → ConfidenceScore=0.90 for each row **[SOURCE:INPUT] Gap: AC-002 expects precise OCR float; source uses heuristic 0.90** | `OcrStatus=Extracted, RawOcrText="Allergy to penicillin"` | `job.ExecuteAsync(1, ...)` | `ConfidenceScore==0.90` on inserted row | `Assert.Equal(0.90, field.ConfidenceScore)` |
| TC-004 | positive | Extracted fields have correct PatientId and DocumentId `[SOURCE:INPUT]` | `ClinicalDocument(Id=5, PatientId=7, OcrStatus=Extracted, RawOcrText="Medication: Aspirin 100mg")` | `job.ExecuteAsync(5, ...)` | All rows: `PatientId==7`, `DocumentId==5` | `Assert.All(fields, f => { Assert.Equal(7, f.PatientId); Assert.Equal(5, f.DocumentId); })` |
| TC-005 | negative | OcrStatus=NoData → no fields inserted; dedup job NOT enqueued `[SOURCE:INPUT]` | `OcrStatus=NoData, RawOcrText=null` | `job.ExecuteAsync(1, ...)` | `pgDb.ExtractedClinicalFields.Count()==0`; no dedup job enqueued | `Assert.Equal(0, count)`; `jobsMock.Verify(Times.Never)` |
| TC-006 | negative | Empty RawOcrText (Extracted status but whitespace) → no fields; dedup NOT enqueued `[SOURCE:INPUT]` | `OcrStatus=Extracted, RawOcrText="   "` | `job.ExecuteAsync(1, ...)` | `pgDb.ExtractedClinicalFields.Count()==0`; no dedup enqueued | `Assert.Equal(0, count)`; `jobsMock.Verify(Times.Never)` |
| TC-007 | negative | Document not found → early return; no exception `[SOURCE:INFERRED]` Basis: job loads doc by `SingleOrDefaultAsync`; if null returns immediately. | Empty `ClinicalDocuments` table | `job.ExecuteAsync(9999, ...)` | No exception; `pgDb.ExtractedClinicalFields.Count()==0` | Completes without exception |
| TC-008 | positive | Idempotency: document already has extracted rows → skip re-insertion; re-enqueue dedup `[SOURCE:INFERRED]` Basis: `AnyAsync(f => f.DocumentId == documentId)` guard in source. | Pre-seed one `ExtractedClinicalField` for `DocumentId=1`; `ClinicalDocument(Id=1, OcrStatus=Extracted, RawOcrText="BP: 120/80")` | `job.ExecuteAsync(1, ...)` | `pgDb.ExtractedClinicalFields.Count()==1` (unchanged); dedup job enqueued | `Assert.Equal(1, count)` (no duplicates); `jobsMock.Verify(Times.Once)` |
| TC-009 | positive | ClinicalFieldExtractor: VitalSign text extracted `[SOURCE:INPUT]` | `new ClinicalFieldExtractor(NullLogger<ClinicalFieldExtractor>.Instance)` | `extractor.Extract("BP: 120/80\nHR: 72 bpm")` | Returns ≥1 `VitalSign` field | `Assert.Contains(fields, f => f.FieldType == ClinicalFieldType.VitalSign)` |
| TC-010 | positive | ClinicalFieldExtractor: Medication text extracted `[SOURCE:INPUT]` | Real extractor | `extractor.Extract("Aspirin 100mg daily")` | Returns `Medication` field | `Assert.Contains(fields, f => f.FieldType == ClinicalFieldType.Medication)` |
| TC-011 | positive | ClinicalFieldExtractor: Allergy text extracted `[SOURCE:INPUT]` | Real extractor | `extractor.Extract("Allergy to penicillin")` | Returns `Allergy` field | `Assert.Contains(fields, f => f.FieldType == ClinicalFieldType.Allergy)` |
| TC-012 | positive | ClinicalFieldExtractor: Diagnosis text extracted `[SOURCE:INPUT]` | Real extractor | `extractor.Extract("Diagnosis: Hypertension")` | Returns `Diagnosis` field | `Assert.Contains(fields, f => f.FieldType == ClinicalFieldType.Diagnosis)` |
| TC-013 | negative | ClinicalFieldExtractor: unrecognised text → empty list returned; no Unknown rows `[SOURCE:INPUT]` | Real extractor | `extractor.Extract("Lorem ipsum dolor sit amet")` | Empty `IList<ExtractedFieldDto>` | `Assert.Empty(result)` |
| EC-001 | edge_case | ClinicalFieldExtractor: null/whitespace input → empty list returned immediately `[SOURCE:INPUT]` | Real extractor | `extractor.Extract(null!)` or `extractor.Extract("  ")` | Empty list, no exception | `Assert.Empty(extractor.Extract(null!))`; `Assert.Empty(extractor.Extract("  "))` |
| EC-002 | edge_case | Zero extracted fields from non-empty text → dedup job still enqueued `[SOURCE:INFERRED]` Basis: source enqueues `DeduplicateClinicalFieldsJob` regardless of field count (`// AC-004: enqueue deduplication regardless`). | `ClinicalDocument(OcrStatus=Extracted, RawOcrText="Lorem ipsum")` (unrecognised) | `job.ExecuteAsync(1, ...)` | `pgDb.ExtractedClinicalFields.Count()==0`; dedup job enqueued | `Assert.Equal(0, count)`; `jobsMock.Verify(Times.Once)` |

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/ExtractClinicalFieldsJobTests.cs` | TC-001 through EC-002 (15 test methods) |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ApplicationDbContext` | In-Memory EF Core | `UseInMemoryDatabase(Guid.NewGuid().ToString())`; `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` | Per-test SQL Server side |
| `ClinicalDbContext` | In-Memory EF Core | `UseInMemoryDatabase(Guid.NewGuid().ToString())` (no warnings config needed — no transactions) | Per-test PostgreSQL side |
| `IBackgroundJobClient` | `Mock<IBackgroundJobClient>` | No-op default; verify `Create(job => job.Type==typeof(DeduplicateClinicalFieldsJob), ...)` | Mock object |
| `ClinicalFieldExtractor` | Real instance | `new ClinicalFieldExtractor(NullLogger<ClinicalFieldExtractor>.Instance)` — pure in-memory, no external deps | Real NLP logic |
| `ILogger<ExtractClinicalFieldsJob>` | `NullLogger<ExtractClinicalFieldsJob>.Instance` | No-op | — |

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

private static ClinicalDbContext BuildPgDb()
{
    var opts = new DbContextOptionsBuilder<ClinicalDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options;
    return new ClinicalDbContext(opts);
}

private static ClinicalDocument SeedDocument(
    ApplicationDbContext sqlDb,
    int id = 1, int patientId = 2,
    OcrStatus status = OcrStatus.Extracted,
    string rawText = "BP: 120/80\nAspirin 100mg")
{
    var doc = new ClinicalDocument
    {
        Id                = id,
        PatientId         = patientId,
        OcrStatus         = status,
        RawOcrText        = rawText,
        EncryptedBlobPath = "enc/a.bin",
        UploadedByStaffId = 1,
        OriginalFileName  = "scan.pdf"
    };
    sqlDb.ClinicalDocuments.Add(doc);
    sqlDb.SaveChanges();
    return doc;
}

// Dedup job enqueue verify pattern
jobsMock.Verify(j => j.Create(
    It.Is<Job>(job => job.Type == typeof(DeduplicateClinicalFieldsJob)),
    It.IsAny<IState>()), Times.Once);
```

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Extracted + valid text | `OcrStatus=Extracted, RawOcrText="BP: 120/80\nAspirin 100mg"` | ≥1 ExtractedClinicalField row; dedup job enqueued |
| LowConfidence + valid text | `OcrStatus=LowConfidence, RawOcrText="Diagnosis: Hypertension"` | Fields inserted with `ConfidenceScore=0.60` |
| Extracted confidence heuristic | `OcrStatus=Extracted, RawOcrText="Allergy to penicillin"` | `ConfidenceScore=0.90` [gap] |
| Correct patient + document refs | `ClinicalDocument(Id=5, PatientId=7)` | All rows: `PatientId=7, DocumentId=5` |
| NoData status | `OcrStatus=NoData` | 0 rows; dedup NOT enqueued |
| Whitespace RawOcrText | `OcrStatus=Extracted, RawOcrText="   "` | 0 rows; dedup NOT enqueued |
| Document not found | No ClinicalDocument row for id=9999 | Early return; no exception |
| Idempotency | Pre-existing `ExtractedClinicalField` for DocumentId=1 | No re-insertion; dedup enqueued |
| Extractor — VitalSign | `"BP: 120/80\nHR: 72 bpm"` | VitalSign field returned |
| Extractor — Medication | `"Aspirin 100mg daily"` | Medication field returned |
| Extractor — Allergy | `"Allergy to penicillin"` | Allergy field returned |
| Extractor — Diagnosis | `"Diagnosis: Hypertension"` | Diagnosis field returned |
| Extractor — unrecognised | `"Lorem ipsum dolor sit amet"` | Empty list |
| Extractor — null input | `null!` | Empty list; no exception |
| Zero extracted fields | `ClinicalDocument(OcrStatus=Extracted, RawOcrText="Lorem ipsum")` | 0 rows; dedup still enqueued |

## Test Commands

- **Run Tests**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~ExtractClinicalFieldsJobTests"`
- **Run with Coverage**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~ExtractClinicalFieldsJobTests"`
- **Run Single Test**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~ExtractClinicalFieldsJobTests.ExtractJob_ExtractedStatus_InsertsFieldsAndEnqueuesDedup"`

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**: Document not found return; OcrStatus=NoData return; empty RawOcrText return; idempotency guard; `fields.Count==0` log branch; entity insert + SaveChanges; dedup enqueue; extractor regex branches (VitalSign/Allergy/Medication/Diagnosis/MedicalHistory); unrecognised DEBUG log

## Documentation References

- **Framework Docs**: [xUnit.net](https://xunit.net/docs/getting-started/netcore/cmdline)
- **Mocking Guide**: [Moq Quickstart](https://github.com/moq/moq4/wiki/Quickstart)
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/GenerateIcd10CodesJobTests.cs`
- **Hangfire IBackgroundJobClient**: [Hangfire Client](https://docs.hangfire.io/en/latest/background-methods/calling-methods-in-background.html)
- **EF Core In-Memory**: [InMemory Provider](https://learn.microsoft.com/en-us/ef/core/providers/in-memory/)

## Implementation Checklist

- [x] Create test file `tests/ClinicalHealthcare.Infrastructure.Tests/Features/ExtractClinicalFieldsJobTests.cs`
- [x] Set up `BuildSqlDb()`, `BuildPgDb()`, and `SeedDocument()` helpers
- [x] Set up `Mock<IBackgroundJobClient>` and real `ClinicalFieldExtractor` helpers
- [x] Implement job-level positive test cases (TC-001, TC-002, TC-003, TC-004)
- [x] Implement job-level negative and idempotency test cases (TC-005, TC-006, TC-007, TC-008)
- [x] Implement extractor unit tests (TC-009 through TC-013)
- [x] Implement edge case tests (EC-001, EC-002)
- [x] Run test suite and validate all 15 tests pass
