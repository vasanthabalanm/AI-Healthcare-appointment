# Test Plan: ClinicalDocument Schema & Encrypted Blob Path

## Requirement Reference

| Field | Value |
|---|---|
| Epic | EP-DATA |
| User Story | US_009 |
| Layer | BE |
| AC Coverage | AC-001, AC-002, AC-003, AC-004, AC-005 |
| AI Impact | No |

## Test Plan Overview

**Purpose:** Verify that the `ClinicalDocument` entity correctly enforces the `EncryptedBlobPath` property guard (throws `ArgumentException` on null/whitespace), that `VirusScanResult` defaults to `Pending`, and that `ApplicationDbContext` registers the `PatientId` index and `EncryptedBlobPath` column constraints in the EF Core model. Confirms that no binary document content is stored in the entity.

**Scope:** Entity property-level validation and EF Core model metadata assertions. SQL Server–level FK enforcement is an integration-test concern and is out of scope here.

## Dependent Tasks

| Task | Plan |
|---|---|
| TASK_009 | ClinicalDocument schema implementation |

## Components Under Test

| Component | Type | File Path | Responsibilities |
|---|---|---|---|
| `ClinicalDocument` | Entity | `src/ClinicalHealthcare.Infrastructure/Entities/ClinicalDocument.cs` | Property guard on `EncryptedBlobPath`; `VirusScanResult` default; no binary DB storage |
| `ApplicationDbContext` | EF Core DbContext | `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs` | Configures `EncryptedBlobPath` maxlength 500; `PatientId` index; `VirusScanResult` default column value |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---|---|---|---|---|---|---|
| TC-001 | positive | EncryptedBlobPath with valid string accepted and returned | New `ClinicalDocument` instance | `EncryptedBlobPath = "/encrypted/path/doc.enc"` | Property getter returns same value | `doc.EncryptedBlobPath == "/encrypted/path/doc.enc"` [SOURCE:INPUT] |
| TC-002 | positive | VirusScanResult defaults to Pending on new ClinicalDocument instance | New `ClinicalDocument()` default-constructed | `VirusScanResult` property read | Value is `VirusScanResult.Pending` | `doc.VirusScanResult == VirusScanResult.Pending` [SOURCE:INPUT] |
| TC-003 | positive | ClinicalDocument can be inserted and retrieved via InMemory ApplicationDbContext | InMemory DB; valid ClinicalDocument with EncryptedBlobPath set | `AddAsync` + `SaveChangesAsync` + query | Document retrieved with same EncryptedBlobPath | `retrieved.EncryptedBlobPath == inserted path`; `retrieved.Id > 0` [SOURCE:INPUT] |
| TC-004 | positive | PatientId non-clustered index registered in EF Core model metadata | InMemory ApplicationDbContext created | `ClinicalDocument` model metadata inspected | PatientId index present in entity indexes | `entityType.GetIndexes().Any(i => i.Properties.Any(p => p.Name == "PatientId"))` is `true` [SOURCE:INPUT] |
| TC-005 | positive | EncryptedBlobPath column configured with maxlength 500 in EF Core model | InMemory ApplicationDbContext created | `ClinicalDocument` model metadata for `EncryptedBlobPath` property | MaxLength is 500 | `entityType.FindProperty("EncryptedBlobPath")!.GetMaxLength() == 500` [SOURCE:INPUT] |
| EC-001 | edge_case | EncryptedBlobPath setter throws ArgumentException on null | New `ClinicalDocument` instance | `EncryptedBlobPath = null` | `ArgumentException` thrown with param name `encryptedBlobPath` | `Assert.Throws<ArgumentException>` catches exception; `ex.ParamName == "value"` [SOURCE:INPUT] |
| EC-002 | edge_case | EncryptedBlobPath setter throws ArgumentException on whitespace | New `ClinicalDocument` instance | `EncryptedBlobPath = "   "` | `ArgumentException` thrown | Exception message contains "null or whitespace" [SOURCE:INPUT] |
| ES-001 | error | EncryptedBlobPath setter throws ArgumentException on empty string | New `ClinicalDocument` instance | `EncryptedBlobPath = ""` | `ArgumentException` thrown | `Assert.Throws<ArgumentException>` catches exception [SOURCE:INFERRED] |

## Expected Changes

| Action | File Path | Description |
|---|---|---|
| Create | `tests/ClinicalHealthcare.Infrastructure.Tests/Migrations/ClinicalDocumentSchemaTests.cs` | xUnit test class covering TC-001 through ES-001 |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|---|---|---|---|
| `ApplicationDbContext` | Real (InMemory) | `UseInMemoryDatabase(Guid.NewGuid().ToString())` per test | Real EF Core in-memory store |
| `UserAccount` (FK PatientId) | Real entity | Inserted as prerequisite seed | Seeded with Id=1 |

> No Moq mocks required — property guard tests are pure unit tests; model metadata tests use real EF Core model.

## Test Data

| Scenario | Input Data | Expected Output |
|---|---|---|
| TC-001 valid path | `doc.EncryptedBlobPath = "/encrypted/a.enc"` | Getter returns `"/encrypted/a.enc"` |
| TC-003 insert | `ClinicalDocument { PatientId=1, OriginalFileName="report.pdf", EncryptedBlobPath="/enc/report.enc", VirusScanResult=Pending }` | Retrieved via `db.ClinicalDocuments.First()`; `Id > 0` |
| EC-001 null guard | `EncryptedBlobPath = null` | `ArgumentException("EncryptedBlobPath must not be null or whitespace.", "value")` |
| EC-002 whitespace | `EncryptedBlobPath = "   "` | `ArgumentException` |
| ES-001 empty string | `EncryptedBlobPath = ""` | `ArgumentException` |

## Test Commands

```bash
# Run all tests in this plan
dotnet test ClinicalHealthcare.slnx --filter "FullyQualifiedName~ClinicalDocumentSchema"

# Run single test
dotnet test ClinicalHealthcare.slnx --filter "FullyQualifiedName=ClinicalHealthcare.Infrastructure.Tests.Migrations.ClinicalDocumentSchemaTests.EC001_EncryptedBlobPath_Null_ThrowsArgumentException"

# Coverage
dotnet test ClinicalHealthcare.slnx --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~ClinicalDocumentSchema"
```

## Coverage Target

| Metric | Target |
|---|---|
| Line Coverage | ≥ 90% |
| Branch Coverage | ≥ 85% |
| Critical Paths | `EncryptedBlobPath` setter null/whitespace/empty guard; `VirusScanResult.Pending` default; `PatientId` index and `EncryptedBlobPath` maxlength 500 model config |

## Documentation References

- [EF Core Model Metadata API](https://learn.microsoft.com/en-us/ef/core/modeling/)
- [EF Core InMemory Testing](https://learn.microsoft.com/en-us/ef/core/testing/testing-without-the-database)
- [xUnit Assert.Throws](https://xunit.net/docs/getting-started/netcore/cmdline)
- Existing pattern: `tests/ClinicalHealthcare.Infrastructure.Tests/Data/ApplicationDbContextTests.cs`

## Implementation Checklist

- [x] Use `UseInMemoryDatabase(Guid.NewGuid().ToString())` per test for isolation
- [x] Add `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` to all InMemory contexts
- [x] TC-001, EC-001, EC-002, ES-001 — pure property tests; no DbContext needed; construct `new ClinicalDocument()` directly
- [x] TC-002 — verify `new ClinicalDocument().VirusScanResult == VirusScanResult.Pending` (enum default value)
- [x] TC-003 — seed a `UserAccount` with Id=1 before inserting `ClinicalDocument` to satisfy FK
- [x] TC-004, TC-005 — use `db.Model.FindEntityType(typeof(ClinicalDocument))` to access index/property metadata
- [x] Assert `EncryptedBlobPath` `ArgumentException` catches use `Assert.Throws<ArgumentException>` (synchronous setter)
- [x] Verify entity has no `BlobContent` or binary property (confirm HIPAA binary-not-in-DB requirement via reflection or entity inspection)
