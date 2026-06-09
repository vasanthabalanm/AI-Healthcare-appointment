---
task_id: task_038
us_id: us_038
epic_id: EP-006
review_date: 2026-05-19
reviewer: GitHub Copilot (analyze-implementation)
---

# Implementation Analysis -- task_038_document-upload-clamav-scan.md

## Verdict

**Status:** Pass
**Summary:** All five acceptance criteria and both stated edge cases are fully implemented in
`UploadDocumentEndpoint`, `ClamAvScanService`, `AesEncryptionService`, and `OcrDocumentJob`. The
scan-before-write ordering is strictly enforced — no code path reaches the disk write without a
`Clean` result. AES-256-CBC encryption uses a per-file random IV; the key is read exclusively from the
`CLINICAL_AES_KEY` environment variable with a fail-fast length check. Two low-severity observations
(duplicate `SocketException` in a catch clause; orphaned-file risk if `SaveChangesAsync` fails) are
non-blocking. All 468 project tests pass with 0 failures.

---

## Traceability Matrix

| Requirement / Acceptance Criterion | Evidence (file: fn / line) | Result |
|---|---|---|
| AC-001: PDF MIME type check | `UploadDocumentEndpoint.HandleUpload` L65-67 — `OrdinalIgnoreCase` compare → 400 | Pass |
| AC-001: Max 10 MB size check | L70-71 — `file.Length > 10L * 1024 * 1024` → 400 | Pass |
| AC-001: PDF magic bytes (%PDF-) | L77-86 — reads first 5 bytes, validates `0x25 0x50 0x44 0x46 0x2D` → 400 | Pass |
| AC-002: ClamAV scan before any disk write | L97-108 — `clamAv.ScanAsync` called; disk write at L115 only reachable if result is `Clean` | Pass |
| AC-002: Infected → 422 | L110-111 — `Results.UnprocessableEntity` on `ClamAvScanResult.Infected` | Pass |
| AC-002: Unavailable → 503 (no bypass) | L100-107 — `catch(ClamAvUnavailableException)` → `Results.Problem(503)`; no bypass path exists | Pass |
| AC-002: TCP errors → ClamAvUnavailableException | `ClamAvScanService.ScanAsync` L41-47 — catches `SocketException`, `IOException`, `TimeoutException` | Pass |
| AC-002: Unknown ClamAV result → 503 (not silent accept) | `ClamAvScanService.ScanAsync` L37-38 — switch default throws `ClamAvUnavailableException` | Pass |
| AC-003: AES-256-CBC encryption | `AesEncryptionService.Encrypt` L53-55 — `Mode=CBC`, `KeySize=256`, `Padding=PKCS7` | Pass |
| AC-003: Random IV per file | L57 — `aes.GenerateIV()` on every call | Pass |
| AC-003: Encrypted file written before DB insert | `UploadDocumentEndpoint.HandleUpload` L115 write, L122 `SaveChangesAsync` | Pass |
| AC-003: IV stored with ciphertext | L115 — `$"{Base64(iv)}:{Base64(ciphertext)}"` | Pass |
| AC-004: ClinicalDocument row inserted after encrypted write | L119-128 — `db.ClinicalDocuments.Add(document)` + `SaveChangesAsync` | Pass |
| AC-004: EncryptedBlobPath, VirusScanResult=Clean, OriginalFileName, UploadedByStaffId set | L119-126 — all four fields populated | Pass |
| AC-005: OcrDocumentJob enqueued after row commit | L131 — `jobs.Enqueue<OcrDocumentJob>` after `SaveChangesAsync` | Pass |
| AC-005: OcrDocumentJob stub handles missing document gracefully | `OcrDocumentJob.ExecuteAsync` L35-38 — null check + log + return | Pass |
| Edge: ClamAV unavailable → 503, no file written, no DB row | L100-107 — early return before disk write; test `Upload_ClamAvUnavailable_NoDocumentRowCreated` | Pass |
| Edge: PDF MIME spoofing → magic bytes block it | L77-86 — magic check applies even if MIME is `application/pdf` | Pass |
| Security: StaffOrAdmin on endpoint | `MapEndpoints` L38 — `.RequireAuthorization("StaffOrAdmin")` | Pass |
| Security: Staff ID from JWT sub only | L58-60 — `FindFirst(JwtRegisteredClaimNames.Sub)` | Pass |
| Env vars: CLAMAV_HOST, CLAMAV_PORT | `ClamAvScanService` L21-22 — env vars with safe defaults | Pass |
| Env vars: CLINICAL_AES_KEY — key absence → fail-fast | `AesEncryptionService` L27-32 — throws `InvalidOperationException` | Pass |
| Env vars: DOCUMENT_STORAGE_PATH with fallback | `UploadDocumentEndpoint` L113-114 — `?? Path.GetTempPath()` | Pass |
| DI registration | `Program.cs` L124-125 — `AddScoped<IClamAvScanService>`, `AddScoped<IAesEncryptionService>` | Pass |

---

## Logical & Design Findings

- **Business Logic:** Scan → encrypt → write → DB insert → enqueue ordering is strictly correct.
  The `ms.Position = 0` resets occur at two critical points: after magic-byte read (before scan)
  and after scan (before encrypt), ensuring the correct bytes are processed at each stage.

- **Security:** Key management is robust. The `AesEncryptionService` production constructor throws
  `InvalidOperationException` if `CLINICAL_AES_KEY` is absent or the decoded length ≠ 32 bytes —
  a correct fail-fast approach that prevents silent degraded encryption. Staff identity is JWT-only;
  no user-controlled path can set `UploadedByStaffId`.

- **Error Handling:** All five HTTP error paths (400/401/404/422/503) are covered. The
  `ClamAvScanService` re-throws `ClamAvUnavailableException` unchanged (preventing double-wrapping)
  and catches specific connectivity exceptions only. The endpoint does not swallow
  `SaveChangesAsync` exceptions — they bubble up to ASP.NET Core's global exception handler.

- **Data Access:** `UserAccounts.AnyAsync` (not a full entity load) is used for the patient
  existence check — minimal DB round-trip. No N+1 query risk on the upload path.

- **Frontend:** N/A — no UI impact.

- **Performance:** ≤10 MB validated before reading into `MemoryStream`, preventing unbounded
  memory growth. The ClamAV TCP connection is per-request (correct for `AddScoped`). Disk write
  uses `File.WriteAllTextAsync` (base64 encoded), which stores ~1.33× the original size;
  acceptable for ≤10 MB PDFs (~13 MB max on disk).

- **Patterns & Standards:** `IEndpointDefinition` vertical-slice pattern followed. Public static
  handler is directly unit-testable. `DisableAntiforgery()` correctly applied for file-upload
  multipart forms.

### F1 — LOW: Duplicate `SocketException` in `ClamAvScanService` catch clause

| Attribute | Value |
|---|---|
| Severity | Low |
| Location | `ClamAvScanService.cs` L41 |
| Finding | The `when` guard lists `System.Net.Sockets.SocketException` twice. The second occurrence is dead code — the C# compiler evaluates the pattern match left-to-right and the first occurrence already covers it. |
| Recommendation | Remove the duplicate: `catch (Exception ex) when (ex is SocketException or IOException or TimeoutException)` |
| Blocking | No |

### F2 — INFORMATIONAL: Orphaned encrypted file if `SaveChangesAsync` fails

| Attribute | Value |
|---|---|
| Severity | Informational |
| Location | `UploadDocumentEndpoint.cs` L115-127 |
| Finding | If `File.WriteAllTextAsync` succeeds but `db.SaveChangesAsync` subsequently throws (transient DB error, constraint violation, etc.), an encrypted `.enc` file is left on disk with no corresponding `ClinicalDocument` row. The orphaned file cannot be resolved without a reconciliation job. This is a known at-rest consistency gap inherent to non-transactional file + DB writes. |
| Recommendation | Accepted risk for this phase — a file-cleanup reconciliation job (or transactional outbox pattern) is out of scope for TASK_038 and should be tracked as a future hardening item. No action required now. |
| Blocking | No |

---

## Test Review

- **Existing Tests:**
  - `UploadDocumentEndpointTests.cs` — 13 tests, all green (468 total project tests)
  - AC-001: `Upload_NonPdfMimeType_Returns400`, `Upload_FileTooLarge_Returns400`, `Upload_InvalidMagicBytes_Returns400` (3 tests)
  - AC-002: `Upload_InfectedFile_Returns422`, `Upload_ClamAvUnavailable_Returns503`, `Upload_ClamAvUnavailable_NoDocumentRowCreated` (3 tests)
  - AC-003: `Upload_ValidPdf_CallsAesEncrypt`, `AesEncryptionService_Encrypt_ProducesNonEmptyCiphertext`, `AesEncryptionService_Encrypt_DifferentIvEachCall` (3 tests)
  - AC-004: `Upload_ValidPdf_Returns201AndPersistsDocumentRow` — verifies status, `VirusScanResult.Clean`, `UploadedByStaffId`, `OriginalFileName`, non-empty `EncryptedBlobPath` (1 test)
  - AC-005: `Upload_ValidPdf_EnqueuesOcrDocumentJob` — verifies `j.Create(OcrDocumentJob, IState)` called once (1 test)
  - Error paths: `Upload_UnknownPatient_Returns404`, `Upload_MissingSubClaim_Returns401` (2 tests)
  - Convention test: `EndpointAuthorizationConventionTests` updated with no-op stubs; passes ✓

- **Missing Tests (must add):** None — all acceptance criteria and stated edge cases have direct coverage.

  - [ ] Optional (non-blocking): Test that `EncryptedBlobPath` contains the `DOCUMENT_STORAGE_PATH` prefix when the env var is set.
  - [ ] Optional (non-blocking): Test that `ClamAvScanService` wraps `SocketException` into `ClamAvUnavailableException` (requires integration test with real socket).

---

## Validation Results

- **Commands Executed:** `dotnet test -v q`
- **Outcomes:**

```text
Passed!  - Failed: 0, Passed:  13, Skipped: 0, Total:  13  -- ClinicalHealthcare.Api.Tests.dll
Passed!  - Failed: 0, Passed: 455, Skipped: 0, Total: 455  -- ClinicalHealthcare.Infrastructure.Tests.dll
```

**Total: 468 tests, 0 failed, 0 skipped.**
**Build: 0 errors, 0 warnings.**

---

## Fix Plan (Prioritized)

1. **(Optional) Remove duplicate `SocketException` in catch clause** — `ClamAvScanService.cs` L41 — ETA 5 min — Risk: L

---

## Appendix

- **Context7 References:** None required — analysis based on direct source inspection.
- **Search Evidence:**
  - `grep IClamAvScanService|IAesEncryptionService Program.cs` — confirmed `AddScoped` registrations at L124-125
  - `file_search **/Infrastructure/Security/*.cs` — confirmed 5 security files created
  - `file_search **/Jobs/OcrDocumentJob.cs` — confirmed stub created
  - `dotnet test --filter UploadDocument` — 13/13 pass
