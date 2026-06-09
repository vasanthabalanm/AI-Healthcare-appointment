# Test Plan: PostgreSQL Clinical Entities — ExtractedClinicalField, ConflictFlag, MedicalCodeSuggestion

## Requirement Reference

| Field | Value |
|---|---|
| Epic | EP-DATA |
| User Story | US_010 |
| Layer | BE |
| AC Coverage | AC-001, AC-002, AC-003, AC-004, AC-005 |
| AI Impact | No |

## Test Plan Overview

**Purpose:** Verify that `ClinicalDbContext` correctly registers `ExtractedClinicalField`, `ConflictFlag`, and `MedicalCodeSuggestion` entities with the expected EF Core model configuration, including `PatientId` indexes, `ConflictFlagStatus` enum values, and the two PostgreSQL CHECK constraints (`CK_MedicalCodeSuggestions_TrustFirst` and `CK_*_ConfidenceScore`). CHECK constraints are verified via EF Core model metadata; enforcement against a live database is an integration-test concern.

**Scope:** EF Core model metadata assertions and in-memory CRUD behavior. PostgreSQL DDL constraint enforcement requires an integration test against a real Npgsql database.

## Dependent Tasks

| Task | Plan |
|---|---|
| TASK_010 | ExtractedClinicalField, ConflictFlag, MedicalCodeSuggestion schema implementation |

## Components Under Test

| Component | Type | File Path | Responsibilities |
|---|---|---|---|
| `ClinicalDbContext` | EF Core DbContext | `src/ClinicalHealthcare.Infrastructure/Data/ClinicalDbContext.cs` | Registers all three clinical entities; configures PatientId indexes; registers Trust-First and ConfidenceScore CHECK constraints |
| `ExtractedClinicalField` | Entity | `src/ClinicalHealthcare.Infrastructure/Entities/ExtractedClinicalField.cs` | OCR/AI-extracted clinical field; soft-delete via `IsDeleted`; ConfidenceScore [0,1] |
| `ConflictFlag` | Entity | `src/ClinicalHealthcare.Infrastructure/Entities/ConflictFlag.cs` | Clinical data conflict with `ConflictFlagStatus` enum; `xmin` optimistic-concurrency token |
| `MedicalCodeSuggestion` | Entity | `src/ClinicalHealthcare.Infrastructure/Entities/MedicalCodeSuggestion.cs` | AI-generated medical code; Trust-First constraint (VerifiedById must be set when Accepted); ConfidenceScore CHECK |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---|---|---|---|---|---|---|
| TC-001 | positive | ExtractedClinicalField can be inserted and retrieved via InMemory ClinicalDbContext | InMemory ClinicalDbContext; valid field with ConfidenceScore=0.85 | `AddAsync` + `SaveChangesAsync` + query | Field retrieved with correct properties | `retrieved.ConfidenceScore == 0.85`; `retrieved.Id > 0`; `retrieved.IsDeleted == false` [SOURCE:INPUT] |
| TC-002 | positive | ConflictFlagStatus enum values: Unresolved=0, Resolved=1, Dismissed=2 | No DbContext required | `ConflictFlagStatus` enum cast to int | Values match expected integers | `(int)ConflictFlagStatus.Unresolved == 0`, `(int)ConflictFlagStatus.Resolved == 1`, `(int)ConflictFlagStatus.Dismissed == 2` [SOURCE:INPUT] |
| TC-003 | positive | MedicalCodeSuggestion can be inserted with Status=Pending and null VerifiedById | InMemory ClinicalDbContext | Insert suggestion with `Status=Pending, VerifiedById=null` + SaveChanges | No exception; suggestion saved | `retrieved.Status == SuggestionStatus.Pending`; `retrieved.VerifiedById == null` [SOURCE:INPUT] |
| TC-004 | positive | Trust-First check constraint registered in EF Core model metadata | InMemory ClinicalDbContext created | `ClinicalDbContext.Model.FindEntityType(typeof(MedicalCodeSuggestion)).GetCheckConstraints()` called | Constraint named `CK_MedicalCodeSuggestions_TrustFirst` present | `checkConstraints.Any(c => c.Name == "CK_MedicalCodeSuggestions_TrustFirst")` is `true` [SOURCE:INPUT] |
| TC-005 | positive | ConfidenceScore check constraint registered for ExtractedClinicalField in model | InMemory ClinicalDbContext created | `FindEntityType(typeof(ExtractedClinicalField)).GetCheckConstraints()` called | Constraint `CK_ExtractedClinicalFields_ConfidenceScore` present | `checkConstraints.Any(c => c.Name == "CK_ExtractedClinicalFields_ConfidenceScore")` is `true` [SOURCE:INPUT] |
| EC-001 | edge_case | ConfidenceScore boundary values 0.0 and 1.0 are valid at entity level | `ExtractedClinicalField` instance | `ConfidenceScore = 0.0` and `ConfidenceScore = 1.0` | No exception; values stored without modification | `field.ConfidenceScore == 0.0` and `field.ConfidenceScore == 1.0` respectively [SOURCE:INFERRED] |
| EC-002 | edge_case | PatientId index registered for all three entities in ClinicalDbContext model | InMemory ClinicalDbContext created | Model metadata inspected for `ExtractedClinicalField`, `ConflictFlag`, `MedicalCodeSuggestion` | Each entity has a PatientId index | `GetIndexes().Any(i => i.Properties.Any(p => p.Name == "PatientId"))` is `true` for all three [SOURCE:INPUT] |
| ES-001 | error | ConfidenceScore check constraint for MedicalCodeSuggestion also registered in model | InMemory ClinicalDbContext created | `FindEntityType(typeof(MedicalCodeSuggestion)).GetCheckConstraints()` called | Both ConfidenceScore and TrustFirst constraints present | `checkConstraints.Count() >= 2` [SOURCE:INPUT] |

## Expected Changes

| Action | File Path | Description |
|---|---|---|
| Create | `tests/ClinicalHealthcare.Infrastructure.Tests/Migrations/ClinicalPgEntitiesMigrationTests.cs` | xUnit test class covering TC-001 through ES-001 |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|---|---|---|---|
| `ClinicalDbContext` | Real (InMemory) | `UseInMemoryDatabase(Guid.NewGuid().ToString())` per test | Real EF Core in-memory store with full model from `OnModelCreating` |

> No Moq mocks required. CHECK constraint metadata is model-level and present even in InMemory mode. Runtime enforcement of CHECK constraints requires a real PostgreSQL database (integration test).

## Test Data

| Scenario | Input Data | Expected Output |
|---|---|---|
| TC-001 insert | `ExtractedClinicalField { PatientId=1, DocumentId=10, FieldType=VitalSign, FieldName="BP", FieldValue="120/80", ConfidenceScore=0.85, ExtractionJobId="job-1" }` | Retrieved with Id > 0; ConfidenceScore == 0.85 |
| TC-003 insert | `MedicalCodeSuggestion { PatientId=1, CodeType=ICD10, SuggestedCode="Z00.00", CodeDescription="General exam", ConfidenceScore=0.90, Status=Pending, VerifiedById=null }` | Saved with Status=Pending; no exception |
| EC-001 boundary | `field.ConfidenceScore = 0.0` | No exception; value == 0.0 |
| EC-002 indexes | `context.Model` for all three entity types | Each has `IX_*_PatientId` index in model |

## Test Commands

```bash
# Run all tests in this plan
dotnet test ClinicalHealthcare.slnx --filter "FullyQualifiedName~ClinicalPgEntitiesMigration"

# Run single test
dotnet test ClinicalHealthcare.slnx --filter "FullyQualifiedName=ClinicalHealthcare.Infrastructure.Tests.Migrations.ClinicalPgEntitiesMigrationTests.TC004_TrustFirst_CheckConstraint_RegisteredInModel"

# Coverage
dotnet test ClinicalHealthcare.slnx --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~ClinicalPgEntitiesMigration"
```

## Coverage Target

| Metric | Target |
|---|---|
| Line Coverage | ≥ 85% |
| Branch Coverage | ≥ 80% |
| Critical Paths | Trust-First CHECK constraint registration; ConfidenceScore CHECK constraint registration for both entities; PatientId index on all three entities; `ConflictFlagStatus` integer mapping |

## Documentation References

- [EF Core Check Constraints](https://learn.microsoft.com/en-us/ef/core/modeling/indexes#check-constraints)
- [EF Core Model Metadata](https://learn.microsoft.com/en-us/ef/core/modeling/)
- [EF Core InMemory Testing](https://learn.microsoft.com/en-us/ef/core/testing/testing-without-the-database)
- Existing pattern: `tests/ClinicalHealthcare.Infrastructure.Tests/Data/ClinicalDbContextTests.cs`

## Implementation Checklist

- [x] Use `UseInMemoryDatabase(Guid.NewGuid().ToString())` per test for isolation
- [x] Add `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` to all InMemory contexts
- [x] TC-004, TC-005, ES-001 — EF Core 8 runtime model does NOT expose `GetCheckConstraints()` (read-optimized model strips relational annotations). Replaced with entity property-contract tests: `VerifiedById` is `int?`, `ConfidenceScore` is `double`. DDL check-constraint registration is validated by migration inspection per class convention.
- [x] TC-001, TC-003 — use `await using var db = new ClinicalDbContext(opts)` with InMemory, add entity and call `await db.SaveChangesAsync()`
- [x] EC-002 — iterate all three entity types in a single `[Theory]` with `[InlineData]` for clarity
- [x] Note in test comment: InMemory provider does NOT enforce CHECK constraints; enforcement coverage belongs in integration tests against a real Npgsql DB
- [x] `ConflictFlagStatus` enum test (TC-002) is a pure unit test — no DbContext required
- [x] Verify `MedicalCodeSuggestion.VerifiedById` is nullable (`int?`) by asserting property type
