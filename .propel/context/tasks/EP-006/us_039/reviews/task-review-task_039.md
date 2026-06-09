---
task_id: task_039
us_id: us_039
epic_id: EP-006
review_date: 2026-05-19
reviewer: GitHub Copilot (analyze-implementation)
---

# Implementation Analysis -- task_039_aes256-encryption-rest-retrieval.md

## Verdict

**Status:** Pass
**Summary:** All five acceptance criteria are fully implemented across `DownloadDocumentEndpoint`,
`AesEncryptionService.Decrypt`, and `IAesEncryptionService`. The DPAPI fallback (AC-001) is
Windows-only with a clear fail-fast exception on non-Windows platforms. Decryption is entirely
in-memory via `CryptoStream` — no temp file is written (AC-002). The endpoint requires `StaffOrAdmin`
(AC-003), catches `CryptographicException` → 422 (AC-004), and streams the decrypted PDF with
correct content-type (AC-005). IDOR protection (route `id` vs `doc.PatientId`) is enforced before
calling decrypt, preventing unnecessary key-material use. One low-severity test-helper issue
(`FileStreamHttpResult` null `StatusCode`) was identified and fixed inline. All 478 project tests
pass with 0 failures.

---

## Traceability Matrix

| Requirement / Acceptance Criterion | Evidence (file: fn / line) | Result |
|---|---|---|
| AC-001: Key from `CLINICAL_AES_KEY` env var (primary) | `AesEncryptionService.ResolveKey` L54-62 — `GetEnvironmentVariable("CLINICAL_AES_KEY")`, Base64-decode, 32-byte length check | Pass |
| AC-001: DPAPI fallback when env var absent (Windows) | `AesEncryptionService.ResolveKey` L65-80 — `RuntimeInformation.IsOSPlatform(Windows)` guard; `ProtectedData.Unprotect(blob, null, LocalMachine)` | Pass |
| AC-001: Non-Windows fail-fast when env var absent | `AesEncryptionService.ResolveKey` L67-69 — `PlatformNotSupportedException`-safe `InvalidOperationException` before any DPAPI call | Pass |
| AC-002: Decryption in-memory via `CryptoStream` | `AesEncryptionService.Decrypt` L143-148 — `new MemoryStream(ciphertext)` → `CryptoStream(Read)` → `cs.CopyTo(plaintext)`; no `File.Write` call | Pass |
| AC-002: No temp file written | Full `Decrypt` method — disk access is read-only (`File.ReadAllText`); no `File.Write*` call | Pass |
| AC-003: Download restricted to StaffOrAdmin | `DownloadDocumentEndpoint.MapEndpoints` L31 — `.RequireAuthorization("StaffOrAdmin")` | Pass |
| AC-003: IDOR guard — PatientId mismatch → 404 | `DownloadDocumentEndpoint.HandleDownload` L59-60 — `doc.PatientId != id` → `Results.NotFound` before decrypt | Pass |
| AC-004: `CryptographicException` → 422 | `DownloadDocumentEndpoint.HandleDownload` L63-67 — `catch (CryptographicException)` → `Results.UnprocessableEntity` | Pass |
| AC-004: `FileNotFoundException` → 404 (missing blob) | `DownloadDocumentEndpoint.HandleDownload` L68-71 — `catch (FileNotFoundException)` → `Results.NotFound` | Pass |
| AC-005: `GET /patients/{id}/documents/{docId}` endpoint | `DownloadDocumentEndpoint.MapEndpoints` L30 — `app.MapGet("/patients/{id:int}/documents/{docId:int}", HandleDownload)` | Pass |
| AC-005: Streams decrypted PDF to caller | `DownloadDocumentEndpoint.HandleDownload` L79 — `Results.File(plaintext, "application/pdf", fileName)` | Pass |
| AC-005: Correct filename fallback | `HandleDownload` L74-77 — `OriginalFileName ?? "document-{docId}.pdf"` | Pass |
| `IAesEncryptionService` updated with `Decrypt` | `IAesEncryptionService.cs` — `Stream Decrypt(string encryptedBlobPath)` added | Pass |
| AES-256-CBC used for decryption | `AesEncryptionService.Decrypt` L133-137 — `KeySize=256`, `Mode=CBC`, `Padding=PKCS7` | Pass |
| IV validated to 16 bytes | `AesEncryptionService.Decrypt` L128-130 — `iv.Length != 16` → `CryptographicException` | Pass |
| Bad Base64 in blob → `CryptographicException` | `AesEncryptionService.Decrypt` L122-127 — `catch (FormatException)` → `CryptographicException` | Pass |
| Missing IV separator → `CryptographicException` | `AesEncryptionService.Decrypt` L118-120 — `colonIdx < 0` → `CryptographicException` | Pass |
| DI registration (endpoint discoverable) | `Program.cs` — `IEndpointDefinition` auto-discovery via reflection picks up `DownloadDocumentEndpoint` | Pass |
| Convention test: no-op AES stub has `Decrypt` | `EndpointAuthorizationConventionTests` — `NoOpAesEncryptionService.Decrypt` returns `new MemoryStream()` | Pass |

---

## Logical & Design Findings

- **Business Logic:** The decrypt-then-stream ordering is correct. The IDOR guard fires before
  any key-material is used (`aes.Decrypt` is only called after confirming `doc.PatientId == id`),
  preventing any side-channel that could infer document existence through decryption timing.

- **Security:** Key resolution follows defence-in-depth: env var (12-factor app, suitable for
  container deployments) → DPAPI (Windows service host). The `key.Length != 32` fail-fast
  prevents silent use of a truncated key. `ArgumentException` on null/empty `encryptedBlobPath`
  prevents accidental null-path reads. The IDOR guard returns identical `NotFound` messages for
  "document doesn't exist" and "document belongs to different patient" — correct information
  hiding to prevent enumeration.

- **Error Handling:** Three distinct error paths from `Decrypt`:
  1. `FormatException` (bad Base64) → wrapped in `CryptographicException` → 422.
  2. `CryptographicException` (wrong key / corrupt padding) → propagates → 422.
  3. `FileNotFoundException` (blob deleted from disk) → caught separately → 404.
  This three-way split is semantically correct — 404 for missing file is operationally actionable
  distinct from 422 for data corruption.

- **Data Access:** `AsNoTracking()` on the document load — correct for read-only streaming. The
  query is by primary key (`d.Id == docId`) — single row lookup, O(1) via index.

- **Performance:** Decrypted content held entirely in `MemoryStream` before streaming. For the
  maximum document size of 10 MB (enforced at upload), peak memory usage is ~10 MB per request —
  acceptable. A streaming `CryptoStream` piped directly to the HTTP response body would reduce
  peak memory, but is out of scope for this task.

- **Patterns & Standards:** `IEndpointDefinition` vertical-slice pattern followed. Public static
  `HandleDownload` is directly unit-testable. `WithName` / `WithTags` / `Produces` metadata
  complete for Swagger/OpenAPI documentation.

### F1 — LOW: `StatusCode` helper returned 0 for `FileStreamHttpResult`

| Attribute | Value |
|---|---|
| Severity | Low |
| Location | `DownloadDocumentEndpointTests.cs` — `StatusCode` helper method |
| Finding | `Results.File(...)` returns `FileStreamHttpResult` which implements `IStatusCodeHttpResult` with `StatusCode = null` (meaning default 200). The original helper used `sc.StatusCode is not null` guard, so it fell through to the reflection fallback which also returned null → 0. The test `Download_ValidDocument_Returns200WithPdfContent` failed with `Expected: 200 / Actual: 0`. |
| Fix Applied | Changed `if (result is IStatusCodeHttpResult sc && sc.StatusCode is not null) return sc.StatusCode.Value;` to `if (result is IStatusCodeHttpResult sc) return sc.StatusCode ?? 200;`. The `?? 200` correctly handles the null-means-default convention used by file results. |
| Blocking | No — fixed inline before test run reported |

### F2 — INFORMATIONAL: Peak memory holds full decrypted plaintext in `MemoryStream`

| Attribute | Value |
|---|---|
| Severity | Informational |
| Location | `AesEncryptionService.Decrypt` — `MemoryStream plaintext` |
| Finding | The full plaintext is buffered in memory before returning to the endpoint, which then passes the same stream to `Results.File`. For 10 MB documents this means ~10 MB resident per request. A `CryptoStream` piped directly to the HTTP response would reduce peak memory to the pipe buffer size (~4 KB), but requires bypassing `Results.File` helper. |
| Recommendation | Accepted for this phase — document size is bounded to 10 MB by upload validation. Consider streaming response for TASK_04x if memory pressure is observed under load. No action required now. |
| Blocking | No |

---

## Test Review

- **Existing Tests:**
  - `DownloadDocumentEndpointTests.cs` — 12 tests, all green (478 total project tests)
  - AC-005: `Download_ValidDocument_Returns200WithPdfContent`, `Download_ValidDocument_DecryptedContentMatchesOriginal` (2 tests)
  - AC-004: `Download_CorruptFile_Returns422`, `Download_WrongKey_Returns422` (2 tests)
  - AC-003 / IDOR: `Download_WrongPatientId_Returns404`, `Download_DocumentNotFound_Returns404`, `Download_FileNotFoundOnDisk_Returns404` (3 tests)
  - AC-001 key resolution: `AesEncryptionService_ResolveKey_FromEnvVar`, `AesEncryptionService_ResolveKey_InvalidLength_Throws`, `AesEncryptionService_TestConstructor_InvalidKeyLength_Throws` (3 tests)
  - Blob parsing: `AesEncryptionService_Decrypt_MissingSeparator_ThrowsCryptographicException`, `AesEncryptionService_Decrypt_InvalidBase64_ThrowsCryptographicException` (2 tests)
  - Convention test: `EndpointAuthorizationConventionTests` updated with `NoOpAesEncryptionService.Decrypt` — passes ✓

- **Missing Tests (must add):** None — all acceptance criteria and stated edge cases have direct coverage.

  - [ ] Optional (non-blocking): `AesEncryptionService_ResolveKey_DpapiPath_Windows` — requires Windows-only integration test with a real DPAPI-protected key file; out of scope for unit tests.
  - [ ] Optional (non-blocking): Test that `Results.File` `Content-Disposition` header includes the original file name (requires HTTP integration test).

---

## Validation Results

- **Commands Executed:** `dotnet test -v q`
- **Outcomes:**

```text
Passed!  - Failed: 0, Passed:  13, Skipped: 0, Total:  13  -- ClinicalHealthcare.Api.Tests.dll
Passed!  - Failed: 0, Passed: 465, Skipped: 0, Total: 465  -- ClinicalHealthcare.Infrastructure.Tests.dll
```

**Total: 478 tests, 0 failed, 0 skipped.**
**Build: 0 errors, 0 warnings.**

---

## Fix Plan (Prioritized)

1. **(Optional) Streaming response body for large files** — `AesEncryptionService.Decrypt` — Future task — Risk: L

---

## Appendix

- **Context7 References:** None required — analysis based on direct source inspection.
- **Search Evidence:** `DownloadDocumentEndpoint.cs`, `AesEncryptionService.cs`, `DownloadDocumentEndpointTests.cs`, `EndpointAuthorizationConventionTests.cs` read directly.
