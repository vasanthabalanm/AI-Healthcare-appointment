# Task - TASK_038

## Requirement Reference

- **User Story**: US_038 — Clinical document upload + ClamAV virus scan
- **Story Location**: `.propel/context/tasks/EP-006/us_038/us_038.md`
- **Parent Epic**: EP-006

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | Upload accepts PDF only, max 10MB |
| AC-002 | ClamAV scan via nClam TCP before any disk write; infected → 422; unavailable → 503 (no bypass) |
| AC-003 | File encrypted with AES-256-CBC then written to disk |
| AC-004 | `ClinicalDocument` row inserted after successful encrypted write |
| AC-005 | Hangfire OCR job enqueued after document row inserted |

### Edge Cases

- ClamAV daemon TCP unavailable → 503; file never written; `ClinicalDocument` row not created
- PDF MIME type spoofing (non-PDF with `.pdf` extension) → validate magic bytes; reject if not valid PDF

---

## Design References

N/A — UI Impact: No

---

## AI References

N/A — AI Impact: No

---

## Mobile References

N/A — Mobile Impact: No

---

## Applicable Technology Stack

| Layer | Technology | Version | Justification |
|-------|-----------|---------|---------------|
| Backend | ASP.NET Core Web API | 8 LTS | File upload endpoint |
| Backend | nClam | 1.x | ClamAV TCP client per design.md |
| Backend | AES-256-CBC | .NET 8 BCL | Document encryption at rest per design.md |
| Backend | Hangfire | 1.8.x | OCR job enqueue per design.md |

---

## Task Overview

Implement `POST /patients/{id}/documents` file upload endpoint. Validate PDF + ≤10MB. Scan with ClamAV via nClam. Encrypt with AES-256-CBC. Write encrypted file to disk. Insert `ClinicalDocument`. Enqueue Hangfire OCR job.

---

## Dependent Tasks

- **TASK_001 (us_009)** — `ClinicalDocument` entity
- **TASK_001 (us_004)** — Hangfire infrastructure
- **TASK_001 (us_039)** — AES-256-CBC encryption (same key/IV pattern)

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/ClinicalDocs/UploadDocumentEndpoint.cs`
- `src/ClinicalHealthcare.Infrastructure/Security/ClamAvScanService.cs`
- `src/ClinicalHealthcare.Infrastructure/Security/AesEncryptionService.cs`
- `src/ClinicalHealthcare.Infrastructure/Jobs/OcrDocumentJob.cs`

---

## Implementation Plan

1. Create `IClamAvScanService` / `ClamAvScanService` using `nClam`; reads `CLAMAV_HOST` + `CLAMAV_PORT` env vars; `ScanAsync(Stream stream)` → returns `Clean/Infected`; TCP unavailable → throw `ClamAvUnavailableException`.
2. Create `IAesEncryptionService` / `AesEncryptionService`: `Encrypt(Stream input) → (byte[] ciphertext, byte[] iv)`; uses `CLINICAL_AES_KEY` env var; AES-256-CBC; IV generated randomly per file.
3. Implement `POST /patients/{id}/documents` (`[Authorize(Roles="Staff,Admin")]`): validate PDF MIME + magic bytes (`%PDF-`); validate ≤10MB; scan via `ClamAvScanService` → 422 on infected; 503 on unavailable; encrypt stream → write `{iv}:{ciphertext}` to disk at `DOCUMENT_STORAGE_PATH/{guid}.enc`; insert `ClinicalDocument` with `EncryptedBlobPath`, `VirusScanResult=Clean`; enqueue `OcrDocumentJob`; return 201.
4. Create `OcrDocumentJob` Hangfire job stub (full implementation in us_040).
5. PDF magic bytes validation: first 5 bytes must be `%PDF-` (0x25 0x50 0x44 0x46 0x2D).

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/ClinicalDocs/
└── README.md
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/ClinicalDocs/UploadDocumentEndpoint.cs` | POST /patients/{id}/documents |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Security/IClamAvScanService.cs` | ClamAV abstraction |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Security/ClamAvScanService.cs` | nClam TCP scan implementation |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Security/IAesEncryptionService.cs` | AES abstraction |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Security/AesEncryptionService.cs` | AES-256-CBC encryption |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Jobs/OcrDocumentJob.cs` | Hangfire OCR job stub |
| MODIFY | `src/ClinicalHealthcare.Api/Program.cs` | Register ClamAV + AES services |

---

## External References

- [nClam](https://github.com/tekmaven/nClam)
- [AES-256-CBC .NET](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aes)
- [OWASP File Upload Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/File_Upload_Cheat_Sheet.html)

---

## Build Commands

```bash
dotnet add src/ClinicalHealthcare.Infrastructure package nClam
dotnet build
```

---

## Implementation Validation Strategy

- Upload valid PDF ≤10MB → 201; encrypted file on disk; `ClinicalDocument` in DB; OCR job in Hangfire.
- Upload infected file → 422.
- ClamAV unavailable → 503; no file written; no DB row.
- Non-PDF MIME or invalid magic bytes → 400.
- File > 10MB → 400.
- `CLINICAL_AES_KEY` env var used; never hardcoded.

---

## Implementation Checklist

- [x] **[AC-001]** PDF-only + ≤10MB validation (MIME + magic bytes)
- [x] **[AC-002]** ClamAV scan: infected → 422; unavailable → 503 (no bypass)
- [x] **[AC-003]** File encrypted AES-256-CBC before disk write
- [x] **[AC-004]** `ClinicalDocument` row inserted after successful encrypted write
- [x] **[AC-005]** `OcrDocumentJob` enqueued via Hangfire after insert
- [x] IV generated randomly per file; stored with ciphertext
- [x] `CLAMAV_HOST`, `CLAMAV_PORT`, `CLINICAL_AES_KEY`, `DOCUMENT_STORAGE_PATH` from env vars
- [x] `dotnet build` passes with 0 errors
