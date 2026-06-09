# Unit Test Plan - TASK_039

## Requirement Reference
- **User Story**: us_039
- **Story Location**: `.propel/context/tasks/EP-006/us_039/us_039.md`
- **Layer**: BE
- **Related Test Plans**: `EP-006/us_038/unittest/test_plan_be_document-upload-clamav-scan.md` (upload + encrypt side; shares `AesEncryptionService(byte[])` test constructor and `WriteTempEncryptedFile` helper concept)
- **Acceptance Criteria Covered**:
  - AC-001: Document encrypted before HTTP 201 acknowledgement — no plaintext on disk
  - AC-002: AES key from `CLINICAL_AES_KEY` env var or Windows DPAPI — never from config files
  - AC-003: Patient JWT → HTTP 403 (enforced by `RequireAuthorization("StaffOrAdmin")` middleware — not directly testable in handler unit tests; documented as integration concern)
  - AC-004: Staff retrieves document → decrypted stream returned in-memory; no temp file; stream at position 0
  - AC-005: Tampered / corrupt encrypted blob → `CryptographicException` → HTTP 422

## Test Plan Overview

Tests `DownloadDocumentEndpoint.HandleDownload` (static handler) plus `AesEncryptionService.Decrypt` unit tests.
`IAesEncryptionService` is used as the **real** `AesEncryptionService(byte[] key)` test constructor (32-byte zero
key — no env var needed). `ApplicationDbContext` uses InMemory EF Core with per-test `Guid.NewGuid().ToString()`
database names. `WriteTempEncryptedFile` helper creates a real `.enc` file in `Path.GetTempFileName()` using the
same AES key so `HandleDownload` can call `aes.Decrypt(doc.EncryptedBlobPath)` against a valid blob.

**Note on AC-003 (role gate):**
`DownloadDocumentEndpoint` calls `RequireAuthorization("StaffOrAdmin")` at the routing level. A Patient JWT is
rejected by ASP.NET Core middleware before `HandleDownload` is invoked. Unit tests cannot exercise this path
without a full `WebApplicationFactory`. This gap is documented as `[SOURCE:INFERRED]` — integration test required.

**Note on AC-002 (key resolution):**
Key resolution via env var / DPAPI is in `AesEncryptionService()` (parameterless constructor). Unit tests use
`AesEncryptionService(byte[] key)` test constructor which bypasses env var resolution entirely. Key-resolution
paths are not covered by these unit tests; they require environment-level integration testing.

## Dependent Tasks

- TASK_001 (Entities) — `ClinicalDocument`, `UserAccount`, `VirusScanResult` entities
- TASK_001 (Data) — `ApplicationDbContext.ClinicalDocuments`
- TASK_038 — `AesEncryptionService.Encrypt` (used in `WriteTempEncryptedFile` helper)
- TASK_039 — `DownloadDocumentEndpoint`, `IAesEncryptionService.Decrypt`, `AesEncryptionService`

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `DownloadDocumentEndpoint` | class | `src/ClinicalHealthcare.Api/Features/ClinicalDocs/DownloadDocumentEndpoint.cs` | Load document; IDOR guard; decrypt in-memory; stream PDF; CryptographicException → 422; FileNotFoundException → 404 |
| `AesEncryptionService` | class | `src/ClinicalHealthcare.Infrastructure/Security/AesEncryptionService.cs` | AES-256-CBC decrypt; parse `Base64(IV):Base64(ciphertext)` blob; return `MemoryStream` at position 0 |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | Download valid document → 200 with `application/pdf` content type `[SOURCE:INPUT]` | Seed `UserAccount(id=5)` + `ClinicalDocument(PatientId=5, blobPath=WriteTempEncryptedFile(...))` using test AES key; `AesEncryptionService(TestAesKey())` | `HandleDownload(id:5, docId:doc.Id, db, aes, ct)` | HTTP 200; `IFileHttpResult` | `StatusCode==200`; result is `IFileHttpResult` (or `FileStreamHttpResult`) |
| TC-002 | positive | Decrypted content matches original plaintext `[SOURCE:INPUT]` | Seed document encrypted from `byte[] original = {0x25,0x50,0x44,0x46,0x2D,...}`; `AesEncryptionService(TestAesKey())` | `HandleDownload(...)` then read stream | Decrypted bytes == original bytes | `decryptedBytes.SequenceEqual(original)` |
| TC-003 | positive | AES Decrypt round-trip — plaintext recovered from encrypted blob `[SOURCE:INPUT]` | `new AesEncryptionService(new byte[32])`; `WriteTempEncryptedFile(plaintext, key)` creates `.enc` file | `aes.Decrypt(path)` | Stream at position 0; bytes match plaintext | `stream.Position==0`; `readBytes.SequenceEqual(plaintext)` |
| TC-004 | positive | `Decrypt` returns stream at position 0 `[SOURCE:INPUT]` Basis: `HandleDownload` passes stream directly to `Results.File`; stream must be at position 0. | `WriteTempEncryptedFile(plaintext, key)` | `aes.Decrypt(path)` | `stream.Position == 0` | `Assert.Equal(0L, stream.Position)` |
| TC-005 | negative | Corrupt encrypted file → `CryptographicException` → HTTP 422 `[SOURCE:INPUT]` | Seed document with `blobPath` pointing to a file containing random bytes (invalid Base64 or tampered ciphertext) | `HandleDownload(id:5, docId:doc.Id, db, aes, ct)` | HTTP 422 | `StatusCode==422` |
| TC-006 | negative | Wrong AES key → `CryptographicException` → HTTP 422 `[SOURCE:INPUT]` | `WriteTempEncryptedFile(plaintext, key=new byte[32])` encrypts with zero key; `AesEncryptionService(wrongKey)` where `wrongKey[0]=0xFF` | `HandleDownload(...)` | HTTP 422 | `StatusCode==422` |
| TC-007 | negative | IDOR guard — document belongs to different patient → 404 `[SOURCE:INPUT]` | Seed `ClinicalDocument(PatientId=5)`; request for `id=9` (different patient) | `HandleDownload(id:9, docId:doc.Id, db, aes, ct)` | HTTP 404 | `StatusCode==404` |
| TC-008 | negative | Unknown document ID → 404 `[SOURCE:INFERRED]` Basis: `HandleDownload` queries `db.ClinicalDocuments.FirstOrDefaultAsync(d => d.Id == docId)` → returns 404 if null. | No ClinicalDocument with target docId | `HandleDownload(id:5, docId:9999, db, aes, ct)` | HTTP 404 | `StatusCode==404` |
| TC-009 | negative | Missing `.enc` blob file on disk → 404 `[SOURCE:INFERRED]` Basis: `HandleDownload` catches `FileNotFoundException` from `aes.Decrypt` → returns 404. | Seed document with `EncryptedBlobPath="C:\nonexistent.enc"` | `HandleDownload(...)` | HTTP 404 | `StatusCode==404` |
| ES-001 | error | `AesEncryptionService.Decrypt` with missing IV separator → `CryptographicException` `[SOURCE:INFERRED]` Basis: `Decrypt` throws `CryptographicException("Encrypted blob format is invalid — missing IV separator.")` when colon absent. | Write temp file with content `"NOCOLONHEREATALL"` (no colon) | `aes.Decrypt(path)` | `CryptographicException` thrown | `Assert.Throws<CryptographicException>(...)` |

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/DownloadDocumentEndpointTests.cs` | TC-001 through ES-001 (10 test methods) |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ApplicationDbContext` | In-Memory EF Core | `UseInMemoryDatabase(Guid.NewGuid().ToString())`; `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` | Per-test isolated store |
| `IAesEncryptionService` | Real `AesEncryptionService(byte[] key)` | `new AesEncryptionService(new byte[32])` — 256-bit zero key; test constructor avoids env var resolution | Real decryption against temp files |
| `IAesEncryptionService` (wrong-key tests) | Real `AesEncryptionService(byte[] key)` | `new AesEncryptionService(wrongKey)` where `wrongKey[0]=0xFF` | Produces `CryptographicException` on decrypt |
| `ClinicalDocument` seed | EF Core in-memory | `db.ClinicalDocuments.Add(doc); await db.SaveChangesAsync()` | Per-test document with real blob path |
| Encrypted blob file | `Path.GetTempFileName()` | `WriteTempEncryptedFile(plaintext, key)` writes real `Base64(IV):Base64(ciphertext)` | Real `.enc` file in temp dir |
| Corrupt blob file | `Path.GetTempFileName()` | `File.WriteAllText(path, "not-valid-base64!!")` | Triggers `CryptographicException` in `Decrypt` |

### WriteTempEncryptedFile Helper Pattern

```csharp
private static string WriteTempEncryptedFile(byte[] plaintext, byte[] key)
{
    using var aes = Aes.Create();
    aes.KeySize = 256; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
    aes.Key = key; aes.GenerateIV();

    using var ms = new MemoryStream();
    using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        cs.Write(plaintext);

    var path = Path.GetTempFileName();
    File.WriteAllText(path,
        $"{Convert.ToBase64String(aes.IV)}:{Convert.ToBase64String(ms.ToArray())}");
    return path;
}
```

### StatusCode Helper Pattern

```csharp
private static int StatusCode(IResult result)
{
    if (result is IStatusCodeHttpResult sc)
        return sc.StatusCode ?? 200; // null → FileStreamHttpResult default 200
    return (int)(result.GetType().GetProperty("StatusCode")?.GetValue(result) ?? 200);
}
```

### SeedDocument Helper Pattern

```csharp
private static async Task<ClinicalDocument> SeedDocumentAsync(
    ApplicationDbContext db, int patientId, string blobPath, string fileName = "report.pdf")
{
    var doc = new ClinicalDocument
    {
        PatientId         = patientId,
        OriginalFileName  = fileName,
        EncryptedBlobPath = blobPath,
        VirusScanResult   = VirusScanResult.Clean,
    };
    db.ClinicalDocuments.Add(doc);
    await db.SaveChangesAsync();
    return doc;
}
```

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Valid download | `WriteTempEncryptedFile` with matching key; correct `PatientId` | 200; `IFileHttpResult` |
| Decrypted content | PDF magic bytes `{0x25,0x50,0x44,0x46,0x2D}` as plaintext | Decrypted bytes == original |
| AES round-trip | 256-byte plaintext; 32-byte zero key | Encrypt → Decrypt = identity; stream position 0 |
| Stream position | Any plaintext | `stream.Position == 0` after `Decrypt` |
| Corrupt blob | File content = random/invalid bytes | 422 |
| Wrong key | Encrypted with key A; decrypt with key B | 422 |
| IDOR | `PatientId=5`; request `id=9` | 404 |
| Unknown docId | `docId=9999` not in DB | 404 |
| Missing blob | `EncryptedBlobPath` points to non-existent file | 404 |
| No IV separator | Blob content has no `:` character | `CryptographicException` |

## Test Commands

- **Run Tests**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~DownloadDocumentEndpointTests"`
- **Run with Coverage**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~DownloadDocumentEndpointTests"`
- **Run Single Test**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~DownloadDocumentEndpointTests.Download_ValidDocument_Returns200WithPdfContent"`

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**: `Decrypt` format guard (missing colon); Base64 parse error → `CryptographicException`; IV length guard; `CryptoStream` bad-padding path; IDOR `PatientId != id` guard; `FileNotFoundException` → 404; `CryptographicException` → 422; stream position 0

## Documentation References

- **Framework Docs**: [xUnit.net](https://xunit.net/docs/getting-started/netcore/cmdline)
- **Mocking Guide**: [Moq Quickstart](https://github.com/moq/moq4/wiki/Quickstart)
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/UploadDocumentEndpointTests.cs`
- **System.Security.Cryptography.Aes**: [Microsoft Docs](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aes)

## Implementation Checklist

- [x] Create test file `tests/.../Features/DownloadDocumentEndpointTests.cs`
- [x] Set up `BuildDb()`, `WriteTempEncryptedFile()`, `SeedDocumentAsync()` helpers
- [x] Implement positive test cases (TC-001 to TC-004)
- [x] Implement negative test cases (TC-005 to TC-009)
- [x] Implement error scenario tests (ES-001)
- [x] Run test suite and validate all 10 tests pass
