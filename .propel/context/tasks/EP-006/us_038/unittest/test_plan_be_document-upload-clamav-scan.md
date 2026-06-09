# Unit Test Plan - TASK_038

## Requirement Reference
- **User Story**: us_038
- **Story Location**: `.propel/context/tasks/EP-006/us_038/us_038.md`
- **Layer**: BE
- **Related Test Plans**: `EP-006/us_039/unittest/test_plan_be_aes256-encryption-rest-retrieval.md` (AES unit tests share `AesEncryptionService(byte[])` test constructor)
- **Acceptance Criteria Covered**:
  - AC-001: Valid PDF ≤ 10 MB → ClamAV scans clean → encrypted, ClinicalDocument inserted, OCR job enqueued → HTTP 201
  - AC-002: ClamAV returns `Infected` → HTTP 422; no file written; no ClinicalDocument row
  - AC-003: ClamAV daemon unreachable (`ClamAvUnavailableException`) → HTTP 503; no file written
  - AC-004: OCR Hangfire job enqueued asynchronously — upload response does not wait for OCR
  - AC-005: Non-PDF MIME type → HTTP 400 (**gap: US specifies 415**; source returns `Results.BadRequest`)

## Test Plan Overview

Tests `UploadDocumentEndpoint.HandleUpload` (static handler) plus `AesEncryptionService.Encrypt` unit tests.
`IClamAvScanService` and `IAesEncryptionService` are mocked via Moq. `IBackgroundJobClient` is a Moq mock
(no Hangfire server). `ICacheService` is a Moq no-op. `ApplicationDbContext` uses InMemory EF Core with a
per-test `Guid.NewGuid().ToString()` database name. `IFormFile` is built from `FormFile` with a fixed
`MemoryStream` seeded with correct or incorrect PDF magic bytes.

**Gap noted:**
- AC-005 specifies HTTP 415 Unsupported Media Type; the source returns `Results.BadRequest` (HTTP 400).
  Tests `TC-010` and `TC-011` verify source behaviour (HTTP 400) and document the divergence as `[SOURCE:INPUT]`.
- Edge case: file over 10 MB — US specifies HTTP 413; source returns `Results.BadRequest` (HTTP 400).
  Test `EC-001` verifies source behaviour and documents the gap as `[SOURCE:INPUT]`.

## Dependent Tasks

- TASK_001 (Entities) — `ClinicalDocument`, `UserAccount`, `VirusScanResult`, `OcrStatus` entities
- TASK_001 (Data) — `ApplicationDbContext.ClinicalDocuments`, `ApplicationDbContext.UserAccounts`
- TASK_038 — `UploadDocumentEndpoint`, `IClamAvScanService`, `ClamAvUnavailableException`, `IAesEncryptionService`, `AesEncryptionService`, `OcrDocumentJob`
- TASK_039 — `IAesEncryptionService` interface (shared by both endpoints)

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `UploadDocumentEndpoint` | class | `src/ClinicalHealthcare.Api/Features/ClinicalDocs/UploadDocumentEndpoint.cs` | MIME check; size check; magic bytes; patient guard; ClamAV gate; AES encrypt; ClinicalDocument insert; OCR enqueue; 360° cache invalidation |
| `AesEncryptionService` | class | `src/ClinicalHealthcare.Infrastructure/Security/AesEncryptionService.cs` | AES-256-CBC encrypt; random IV per call |
| `IClamAvScanService` | interface | `src/ClinicalHealthcare.Infrastructure/Security/IClamAvScanService.cs` | Mocked — Clean / Infected / ClamAvUnavailableException |
| `IAesEncryptionService` | interface | `src/ClinicalHealthcare.Infrastructure/Security/IAesEncryptionService.cs` | Mocked for upload handler; real test constructor for AES unit tests |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | Valid PDF → ClamAV Clean → 201; ClinicalDocument row persisted `[SOURCE:INPUT]` | Seed `UserAccount(id=1)`; `BuildPdfFormFile(validMagic:true)`; staff JWT (sub="1"); ClamAV mock → `Clean`; AES mock → `(new byte[16], new byte[16])` | `HandleUpload(1, file, ctx, db, clamAv, aesMock, jobs, cache, ct)` | HTTP 201; `db.ClinicalDocuments.Count()==1`; `PatientId==1`; `UploadedByStaffId==1` | `StatusCode==201`; `db.ClinicalDocuments.Single().PatientId==1`; `VirusScanResult==Clean` |
| TC-002 | positive | Valid PDF → AES `Encrypt` called once `[SOURCE:INPUT]` | Same as TC-001 | Same as TC-001 | `aesMock.Verify(a => a.Encrypt(It.IsAny<Stream>()), Times.Once)` | `aesMock` verify passes |
| TC-003 | positive | Valid PDF → OCR job enqueued once `[SOURCE:INPUT]` | Same as TC-001; `Mock<IBackgroundJobClient>` | Same as TC-001 | `jobs.Verify(j => j.Create(It.Is<Job>(jj => jj.Type==typeof(OcrDocumentJob)), It.IsAny<IState>()), Times.Once)` | Hangfire mock verify passes |
| TC-004 | positive | Valid PDF → 360° view cache invalidated `[SOURCE:INPUT]` | Same as TC-001; `Mock<ICacheService>` | Same as TC-001 | `cache.Verify(c => c.DeleteAsync(It.Is<string>(k => k.Contains("1")), It.IsAny<CancellationToken>()), Times.Once)` | Cache mock verify passes |
| TC-005 | positive | AES `Encrypt` returns non-empty ciphertext `[SOURCE:INPUT]` | `new AesEncryptionService(new byte[32])`; `MemoryStream` with 256 bytes of plaintext | `aes.Encrypt(stream)` | `ciphertext.Length > 0`; `iv.Length == 16` | `Assert.NotEmpty(ciphertext)`; `Assert.Equal(16, iv.Length)` |
| TC-006 | positive | AES `Encrypt` generates different IV on each call (random IV per file) `[SOURCE:INPUT]` | `new AesEncryptionService(new byte[32])`; same plaintext stream reused twice | Two calls to `aes.Encrypt(stream)` | IVs differ | `Assert.NotEqual(iv1, iv2)` (byte sequence) |
| TC-007 | negative | ClamAV returns `Infected` → 422; no ClinicalDocument row `[SOURCE:INPUT]` | Seed `UserAccount(id=1)`; valid PDF; ClamAV mock → `Infected` | `HandleUpload(...)` | HTTP 422; `db.ClinicalDocuments.Count()==0` | `StatusCode==422`; `db.ClinicalDocuments.Count()==0` |
| TC-008 | negative | ClamAV throws `ClamAvUnavailableException` → 503 `[SOURCE:INPUT]` | Seed `UserAccount(id=1)`; valid PDF; ClamAV mock throws `new ClamAvUnavailableException("daemon down")` | `HandleUpload(...)` | HTTP 503 | `StatusCode==503` |
| TC-009 | negative | ClamAV unavailable → no ClinicalDocument row created `[SOURCE:INPUT]` | Same as TC-008 | Same as TC-008 | `db.ClinicalDocuments.Count()==0`; AES mock not called | `db.ClinicalDocuments.Count()==0`; `aesMock.Verify(a=>a.Encrypt(It.IsAny<Stream>()), Times.Never)` |
| TC-010 | negative | Non-PDF MIME type → HTTP 400 **[SOURCE:INPUT] Gap: US specifies 415; source returns 400** | `BuildPdfFormFile(contentType:"image/jpeg")` | `HandleUpload(...)` | HTTP 400 | `StatusCode==400` |
| TC-011 | negative | Unknown patient → 404 `[SOURCE:INFERRED]` Basis: `HandleUpload` calls `db.UserAccounts.AnyAsync(u => u.Id == id)` before ClamAV scan; returns 404 if false. | No `UserAccount` for `id=99`; valid PDF; ClamAV mock → `Clean` | `HandleUpload(99, ...)` | HTTP 404 | `StatusCode==404` |
| TC-012 | negative | Missing JWT sub claim → 401 `[SOURCE:INFERRED]` Basis: `HandleUpload` reads JWT sub before any file processing (OWASP A01). | `DefaultHttpContext` with no claims | `HandleUpload(1, file, emptyCtx, ...)` | HTTP 401 | `StatusCode==401` |
| EC-001 | edge_case | File exceeds 10 MB → HTTP 400 **[SOURCE:INPUT] Gap: US specifies 413; source returns 400** | `BuildPdfFormFile(sizeBytes: 10L * 1024 * 1024 + 1)` | `HandleUpload(...)` | HTTP 400 | `StatusCode==400` |
| EC-002 | edge_case | Invalid magic bytes (PDF-extension but non-PDF content) → 400 `[SOURCE:INPUT]` | `BuildPdfFormFile(validMagic:false, contentType:"application/pdf")` | `HandleUpload(...)` | HTTP 400 | `StatusCode==400` |

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/UploadDocumentEndpointTests.cs` | TC-001 through EC-002 (14 test methods) |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ApplicationDbContext` | In-Memory EF Core | `UseInMemoryDatabase(Guid.NewGuid().ToString())`; `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` | Per-test isolated store |
| `IClamAvScanService` | `Mock<IClamAvScanService>` | `.Setup(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>())).ReturnsAsync(ClamAvScanResult.Clean)` or `.Infected`; or `.ThrowsAsync(new ClamAvUnavailableException(...))` | Controlled scan result |
| `IAesEncryptionService` | `Mock<IAesEncryptionService>` | `.Setup(a => a.Encrypt(It.IsAny<Stream>())).Returns((new byte[16], new byte[16]))` — fake ciphertext + IV so file-write succeeds | Tuple with empty arrays |
| `IBackgroundJobClient` | `Mock<IBackgroundJobClient>` | No-op `.Create(...)` default; verify `Times.Once` for TC-003 | Mock object |
| `ICacheService` | `Mock<ICacheService>` | `.Returns(Task.CompletedTask)` default; verify `DeleteAsync` key for TC-004 | Mock object |
| `IFormFile` | `FormFile` (real) | `new FormFile(stream, 0, size, "file", "test.pdf")` with `ContentType` and magic bytes set | Real `IFormFile` |
| `HttpContext` (staff) | `DefaultHttpContext` | JWT sub claim via `ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, staffId.ToString())], "TestAuth"))` | StaffId from sub |
| `HttpContext` (no claims) | `DefaultHttpContext` | Empty `ClaimsPrincipal` | 401 path |
| `AesEncryptionService` (unit tests) | Real implementation | `new AesEncryptionService(new byte[32])` — 256-bit zero key; avoids env var | Test constructor |

### IFormFile Helper Pattern

```csharp
private static IFormFile BuildPdfFormFile(long sizeBytes = 1024, string contentType = "application/pdf",
    bool validMagic = true)
{
    var bytes = new byte[sizeBytes];
    if (validMagic)
    {
        bytes[0] = 0x25; bytes[1] = 0x50; bytes[2] = 0x44;
        bytes[3] = 0x46; bytes[4] = 0x2D; // %PDF-
    }
    var stream = new MemoryStream(bytes);
    return new FormFile(stream, 0, bytes.Length, "file", "test.pdf")
    {
        Headers     = new HeaderDictionary(),
        ContentType = contentType
    };
}
```

### AES Encrypt Verify Pattern

```csharp
// TC-002 — verify Encrypt called once
aesMock.Verify(a => a.Encrypt(It.IsAny<Stream>()), Times.Once);

// TC-003 — verify Hangfire job enqueued (Hangfire v1.8 IBackgroundJobClient)
jobsMock.Verify(j => j.Create(
    It.Is<Job>(job => job.Type == typeof(OcrDocumentJob)),
    It.IsAny<IState>()), Times.Once);

// TC-004 — verify 360° cache invalidated for patientId=1
cacheMock.Verify(c => c.DeleteAsync(
    It.Is<string>(k => k.Contains("1")),
    It.IsAny<CancellationToken>()), Times.Once);
```

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Valid PDF clean scan | 1024-byte PDF with magic bytes; MIME `application/pdf`; ClamAV Clean | 201; ClinicalDocument row; AES called; OCR enqueued |
| AES encrypt unit | 256-byte plaintext; 32-byte zero key | Non-empty ciphertext; IV = 16 bytes |
| Random IV | Same plaintext; two encrypt calls | IV1 ≠ IV2 |
| Infected file | ClamAV → Infected | 422; no ClinicalDocument row |
| ClamAV unavailable | ClamAvUnavailableException thrown | 503; no ClinicalDocument row; Encrypt not called |
| Wrong MIME | contentType = `image/jpeg` | 400 (gap: US says 415) |
| File too large | sizeBytes = 10 MB + 1 | 400 (gap: US says 413) |
| Bad magic bytes | First 5 bytes not `%PDF-` | 400 |
| Unknown patient | No UserAccount for id | 404 |
| No JWT sub | DefaultHttpContext no claims | 401 |

## Test Commands

- **Run Tests**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~UploadDocumentEndpointTests"`
- **Run with Coverage**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~UploadDocumentEndpointTests"`
- **Run Single Test**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~UploadDocumentEndpointTests.Upload_ValidPdf_Returns201AndPersistsDocumentRow"`

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**: MIME guard; size guard; magic-bytes guard; patient-exists guard; ClamAV-infected branch; ClamAV-unavailable branch; AES encrypt call; `SaveChangesAsync`; Hangfire enqueue; 360° cache delete; JWT sub 401 guard

## Documentation References

- **Framework Docs**: [xUnit.net](https://xunit.net/docs/getting-started/netcore/cmdline)
- **Mocking Guide**: [Moq Quickstart](https://github.com/moq/moq4/wiki/Quickstart)
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/StaffWalkInEndpointTests.cs`
- **Hangfire IBackgroundJobClient**: [Hangfire Client](https://docs.hangfire.io/en/latest/background-methods/calling-methods-in-background.html)
- **nClam**: [nClam NuGet](https://www.nuget.org/packages/nClam/)

## Implementation Checklist

- [x] Create test file `tests/.../Features/UploadDocumentEndpointTests.cs`
- [x] Set up `BuildDb()`, `BuildStaffContext()`, `BuildPdfFormFile()` helpers
- [x] Set up `CreateMocks()` helper returning `(clamAv, aesMock, jobs)`
- [x] Implement positive test cases (TC-001 to TC-006)
- [x] Implement negative test cases (TC-007 to TC-012)
- [x] Implement edge case tests (EC-001, EC-002)
- [x] Run test suite and validate all 14 tests pass
