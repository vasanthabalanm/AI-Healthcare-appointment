# Unit Test Plan - TASK_047

## Requirement Reference
- **User Story**: us_047
- **Story Location**: `.propel/context/tasks/EP-008/us_047/us_047.md`
- **Layer**: BE
- **Related Test Plans**: `EP-008/us_048/unittest/test_plan_be_ai-human-agreement-rate-metric.md` (Modified rows produced here are counted in the agreement metric)
- **Acceptance Criteria Covered**:
  - AC-001: `GET /patients/{id}/code-suggestions` → 200 with suggestions grouped by `codeType` (ICD10/CPT)
  - AC-002: `PATCH /code-suggestions/{id} {status:"Accepted", verifiedById}` → 200; sets `verifiedById`, `verifiedAt=UtcNow`, `status=Accepted`
  - AC-003: `PATCH` without `verifiedById` (when status=Accepted) → 422 `{"error":"verifiedById is required for code verification"}`
  - AC-004: `POST /patients/{id}/code-suggestions/accept-all {verifiedById}` → all Pending → Accepted; single AuditLog entry
  - AC-005: `POST /patients/{id}/coding-complete` → 409 if any Pending remain; 200 + `CodingStatus=Complete` otherwise
  - AC-006: AuditLog entry written for every verification action (PATCH accept/reject, accept-all, coding-complete)

## Test Plan Overview

Tests four verification endpoints against `ClinicalDbContext` (PG InMemory) and `ApplicationDbContext` (SQL InMemory):

1. **`GetCodeSuggestionsEndpoint.HandleGetCodeSuggestions`** (AC-001) — grouped Pending suggestions.
2. **`PatchCodeSuggestionEndpoint.HandlePatchCodeSuggestion`** (AC-002, AC-003, AC-006) — single accept/reject.
3. **`AcceptAllCodeSuggestionsEndpoint.HandleAcceptAll`** (AC-004, AC-006) — bulk accept with AuditLog.
4. **`CodingCompleteEndpoint.HandleCodingComplete`** (AC-005, AC-006) — gate + `CodingStatus=Complete`.

All handlers are `public static async Task<IResult>` methods called directly in tests (no HTTP pipeline required).
`ClinicalDbContext` and `ApplicationDbContext` use `UseInMemoryDatabase(Guid.NewGuid().ToString())` + `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))`.
`ILogger<T>` uses `NullLogger<T>.Instance`.
`HttpContext` is `new DefaultHttpContext()` for `CodingCompleteEndpoint`; use a `ClaimsPrincipal` with `JwtRegisteredClaimNames.Sub` when testing actor extraction.

**Gaps noted:**
- Authorization checks (`RequireAuthorization("StaffOrAdmin")`) are enforced by the ASP.NET Core middleware and are not verifiable in unit tests calling handlers directly. Role-based 403 tests are out of scope for unit test level.
- `CodingCompleteEndpoint` performs `await sqlDb.SaveChangesAsync(ct)` once for both `UserAccount.CodingStatus` update and `AuditLog` add; both are written in the same `SaveChanges` call.

## Dependent Tasks

- TASK_001 (Entities) — `MedicalCodeSuggestion`, `CodeType`, `SuggestionStatus`, `UserAccount`, `CodingStatus`, `AuditLog`
- TASK_001 (Data) — `ClinicalDbContext.MedicalCodeSuggestions`, `ApplicationDbContext.UserAccounts`, `ApplicationDbContext.Set<AuditLog>()`
- TASK_045 (us_045) — `MedicalCodeSuggestion` rows with `codeType=ICD10` seeded for verification tests
- TASK_046 (us_046) — `MedicalCodeSuggestion` rows with `codeType=CPT` seeded for grouping tests
- TASK_047 — `GetCodeSuggestionsEndpoint`, `PatchCodeSuggestionEndpoint`, `AcceptAllCodeSuggestionsEndpoint`, `CodingCompleteEndpoint`

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `GetCodeSuggestionsEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Coding/GetCodeSuggestionsEndpoint.cs` | Query Pending rows for patient; group by `codeType`; return 200 DTO |
| `PatchCodeSuggestionEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Coding/PatchCodeSuggestionEndpoint.cs` | Validate status + verifiedById; load suggestion; set status/verifiedById/verifiedAt/committedCode; write AuditLog |
| `AcceptAllCodeSuggestionsEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Coding/AcceptAllCodeSuggestionsEndpoint.cs` | Validate verifiedById; bulk-update Pending → Accepted; write single AuditLog |
| `CodingCompleteEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Coding/CodingCompleteEndpoint.cs` | Count Pending; 409 if > 0; set `CodingStatus=Complete`; write AuditLog |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | GET returns 200 with grouped Pending suggestions (ICD10 + CPT) `[SOURCE:INPUT]` | Seed 1 ICD-10 Pending + 1 CPT Pending for patient 1 | `HandleGetCodeSuggestions(1, pgDb, ct)` | Status 200; body has `suggestions.ICD10` (1 item) + `suggestions.CPT` (1 item) | `Assert.Equal(200, status)`; verify grouping keys exist |
| TC-002 | negative | GET excludes non-Pending suggestions (Accepted rows not returned) `[SOURCE:INFERRED]` | Seed 1 ICD-10 Accepted + 1 ICD-10 Pending for patient 1 | `HandleGetCodeSuggestions(1, pgDb, ct)` | `suggestions.ICD10` has only 1 item (the Pending one) | `Assert.Equal(1, body.GetProperty("suggestions").GetProperty("ICD10").GetArrayLength())` |
| TC-003 | positive | PATCH accept → 200; `Status=Accepted`; `VerifiedById` set; `VerifiedAt` not null `[SOURCE:INPUT]` | Seed Pending ICD-10 suggestion | `HandlePatchCodeSuggestion(id, new("Accepted", 42, null), pgDb, sqlDb, logger, ct)` | Status 200; updated row has `Status=Accepted`, `VerifiedById=42`, `VerifiedAt≠null` | `Assert.Equal(SuggestionStatus.Accepted, updated.Status)`; `Assert.Equal(42, updated.VerifiedById)`; `Assert.NotNull(updated.VerifiedAt)` |
| TC-004 | positive | PATCH with `committedCode` ≠ `suggestedCode` → `Status=Modified` stored `[SOURCE:INPUT]` | Seed Pending suggestion with `SuggestedCode="J18.9"` | `HandlePatchCodeSuggestion(id, new("Accepted", 42, "Z00.0"), ...)` | Status 200; `Status=Modified`; `CommittedCode="Z00.0"` | `Assert.Equal(SuggestionStatus.Modified, updated.Status)`; `Assert.Equal("Z00.0", updated.CommittedCode)` |
| TC-005 | negative | PATCH without `verifiedById` when status=Accepted → 422 `[SOURCE:INPUT]` | Seed Pending suggestion | `HandlePatchCodeSuggestion(id, new("Accepted", null, null), ...)` | Status 422 | `Assert.Equal(422, status)` |
| TC-006 | negative | PATCH already-Accepted suggestion → 409 `[SOURCE:INPUT]` | Seed `Status=Accepted` suggestion | `HandlePatchCodeSuggestion(id, new("Accepted", 42, null), ...)` | Status 409 | `Assert.Equal(409, status)` |
| TC-007 | negative | PATCH with invalid status value → 400 `[SOURCE:INPUT]` | Seed Pending suggestion | `HandlePatchCodeSuggestion(id, new("UNKNOWN", 42, null), ...)` | Status 400 | `Assert.Equal(400, status)` |
| TC-008 | positive | PATCH reject → 200; `Status=Rejected`; no `verifiedById` required `[SOURCE:INPUT]` | Seed Pending suggestion | `HandlePatchCodeSuggestion(id, new("Rejected", null, null), ...)` | Status 200; `Status=Rejected` | `Assert.Equal(SuggestionStatus.Rejected, updated.Status)` |
| TC-009 | positive | POST accept-all → 200; all Pending → Accepted; `VerifiedById` set `[SOURCE:INPUT]` | Seed 2 Pending suggestions for patient 1 (ICD-10 + CPT) | `HandleAcceptAll(1, new(99), pgDb, sqlDb, logger, ct)` | Status 200; 0 Pending remain; both `VerifiedById=99` | `Assert.Equal(0, pendingCount)`; `Assert.All(rows, r => Assert.Equal(99, r.VerifiedById))` |
| TC-010 | negative | POST accept-all without `verifiedById` → 422 `[SOURCE:INPUT]` | No suggestions needed | `HandleAcceptAll(1, new(null), pgDb, sqlDb, logger, ct)` | Status 422 | `Assert.Equal(422, status)` |
| TC-011 | positive | POST coding-complete with Pending suggestions remaining → 409 `[SOURCE:INPUT]` | Seed 1 Pending suggestion for patient 1 | `HandleCodingComplete(1, httpCtx, pgDb, sqlDb, ct)` | Status 409; error includes `pendingCount` | `Assert.Equal(409, status)` |
| TC-012 | positive | POST coding-complete with no Pending → 200; `CodingStatus=Complete` `[SOURCE:INPUT]` | Seed patient 1 in `UserAccounts`; no Pending suggestions | `HandleCodingComplete(1, httpCtx, pgDb, sqlDb, ct)` | Status 200; `patient.CodingStatus==Complete` | `Assert.Equal(CodingStatus.Complete, patient.CodingStatus)` |
| TC-013 | negative | POST coding-complete for non-existent patient → 404 `[SOURCE:INFERRED]` | No patient in `UserAccounts`; no Pending suggestions | `HandleCodingComplete(999, httpCtx, pgDb, sqlDb, ct)` | Status 404 | `Assert.Equal(404, status)` |
| EC-001 | edge_case | PATCH AuditLog entry written with correct `EntityType`, `ActorId`, `Action` `[SOURCE:INPUT]` | Seed Pending suggestion | PATCH accept with `verifiedById=5` | `AuditLog.EntityType=nameof(MedicalCodeSuggestion)`; `ActorId=5`; `Action="ACCEPTED"` | `Assert.Equal("ACCEPTED", log.Action)`; `Assert.Equal(5, log.ActorId)` |
| EC-002 | edge_case | Accept-all AuditLog entry written with `Action="ACCEPT_ALL"` `[SOURCE:INPUT]` | Seed 1 Pending suggestion | Accept-all with `verifiedById=55` | Single AuditLog row; `Action="ACCEPT_ALL"`; `ActorId=55` | `Assert.Single(logs)`; `Assert.Equal("ACCEPT_ALL", log.Action)` |
| EC-003 | edge_case | Coding-complete AuditLog entry written; `ActorId` extracted from JWT `sub` claim `[SOURCE:INPUT]` | Seed patient 1; `HttpContext.User` has `sub=7` | `HandleCodingComplete(1, httpCtxWithSub7, pgDb, sqlDb, ct)` | AuditLog `Action="CODING_COMPLETE"`; `ActorId=7` | `Assert.Equal("CODING_COMPLETE", log.Action)`; `Assert.Equal(7, log.ActorId)` |

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| EXISTS | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/CodeVerificationEndpointTests.cs` | TC-001 through EC-003 (14 test methods covering all 4 verification endpoints) |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ClinicalDbContext` | In-Memory EF Core | `UseInMemoryDatabase(Guid.NewGuid().ToString())`; `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` | Per-test isolated PG store |
| `ApplicationDbContext` | In-Memory EF Core | Same InMemory pattern | Per-test isolated SQL store |
| `ILogger<PatchCodeSuggestionEndpoint>` | `NullLogger<PatchCodeSuggestionEndpoint>.Instance` | No-op | — |
| `ILogger<AcceptAllCodeSuggestionsEndpoint>` | `NullLogger<AcceptAllCodeSuggestionsEndpoint>.Instance` | No-op | — |
| `HttpContext` | `new DefaultHttpContext()` | Default (no claims) for most tests; `new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, "7")]))` for EC-003 | — |

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

private static ApplicationDbContext BuildSqlDb()
{
    var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options;
    return new ApplicationDbContext(opts);
}

private static MedicalCodeSuggestion SeedSuggestion(
    ClinicalDbContext pgDb, int patientId = 1,
    CodeType codeType = CodeType.ICD10,
    SuggestionStatus status = SuggestionStatus.Pending,
    double confidence = 0.85)
{
    var s = new MedicalCodeSuggestion
    {
        PatientId = patientId, CodeType = codeType,
        SuggestedCode = codeType == CodeType.ICD10 ? "J18.9" : "99213",
        CodeDescription = "Test", ConfidenceScore = confidence, Status = status
    };
    pgDb.MedicalCodeSuggestions.Add(s);
    pgDb.SaveChanges();
    return s;
}

// JWT actor pattern for CodingCompleteEndpoint
var httpContext = new DefaultHttpContext();
httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
    new[] { new Claim(JwtRegisteredClaimNames.Sub, "7") }));
```

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Mixed ICD10 + CPT Pending | 2 Pending rows for patient 1 | GET returns grouped dict with ICD10 and CPT keys |
| Accept Pending suggestion | `{status:"Accepted", verifiedById:42}` | `Status=Accepted`; `VerifiedById=42`; `VerifiedAt` set |
| Accept with different committedCode | `{committedCode:"Z00.0"}` ≠ `suggestedCode:"J18.9"` | `Status=Modified`; `CommittedCode="Z00.0"` |
| Missing verifiedById for accept | `{status:"Accepted", verifiedById:null}` | 422 |
| Already-Accepted PATCH | Suggestion already `Status=Accepted` | 409 |
| Accept-all with verifiedById=99 | 2 Pending rows | 0 Pending; all rows `VerifiedById=99` |
| Coding-complete with Pending | 1 Pending suggestion | 409 |
| Coding-complete no Pending | Patient seeded; no Pending | 200; `CodingStatus=Complete`; AuditLog written |
| Coding-complete with JWT sub=7 | `HttpContext.User.Sub="7"` | `AuditLog.ActorId=7` |

## Test Commands

- **Run Tests**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~CodeVerificationEndpointTests"`
- **Run with Coverage**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~CodeVerificationEndpointTests"`
- **Run Single Test**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~CodeVerificationEndpointTests.PatchCodeSuggestion_Returns200_WhenAccepted"`

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**: GET grouping logic; PATCH status validation; PATCH verifiedById guard; Already-Accepted 409; `committedCode` → Modified path; Accept-all bulk update; Accept-all verifiedById guard; Coding-complete pending count guard; Coding-complete patient 404; `CodingStatus=Complete` assignment; AuditLog writes for all 4 endpoints; JWT `sub` → `ActorId` extraction

## Documentation References

- **Framework Docs**: [xUnit.net](https://xunit.net/docs/getting-started/netcore/cmdline)
- **Mocking Guide**: [Moq Quickstart](https://github.com/moq/moq4/wiki/Quickstart)
- **ASP.NET Core Authorization**: [Role-Based Auth](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles)
- **JWT Claims**: [JwtRegisteredClaimNames](https://learn.microsoft.com/en-us/dotnet/api/system.identitymodel.tokens.jwt.jwtregisteredclaimnames)

## Implementation Checklist

- [x] Create `CodeVerificationEndpointTests.cs` with `BuildPgDb()`, `BuildSqlDb()`, `SeedSuggestion()`, `SeedPatient()` helpers
- [x] Implement GET grouping tests (TC-001, TC-002)
- [x] Implement PATCH accept/reject positive and negative tests (TC-003 through TC-008)
- [x] Implement accept-all tests with AuditLog verification (TC-009, TC-010, EC-002)
- [x] Implement coding-complete tests with AuditLog and CodingStatus checks (TC-011 through TC-013, EC-003)
- [x] Implement PATCH AuditLog verification (EC-001)
- [x] Run test suite and validate all tests pass
