# Task - TASK_039

## Requirement Reference

- **User Story**: US_039 — AES-256 encryption at rest + role-gated retrieval
- **Story Location**: `.propel/context/tasks/EP-006/us_039/us_039.md`
- **Parent Epic**: EP-006

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | Decryption key sourced from `CLINICAL_AES_KEY` env var or Windows DPAPI fallback |
| AC-002 | Decryption performed in-memory; no temp file written |
| AC-003 | Document download restricted to Staff and Admin roles |
| AC-004 | File corruption or wrong key → `CryptographicException` → 422 |
| AC-005 | `GET /patients/{id}/documents/{docId}` streams decrypted PDF to caller |

### Edge Cases

- `CLINICAL_AES_KEY` env var absent → fall back to Windows DPAPI `ProtectedData.Unprotect()`
- Large file (10MB) → stream in chunks; no full in-memory buffer allocation

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
| Backend | AES-256-CBC | .NET 8 BCL | Encryption at rest per design.md |
| Backend | ASP.NET Core Web API | 8 LTS | File streaming endpoint |
| Database | SQL Server | 2022 / Express | `ClinicalDocument` metadata |

---

## Task Overview

Implement `GET /patients/{id}/documents/{docId}` for Staff/Admin. Decrypt AES-256-CBC in-memory (no temp file). Stream decrypted PDF to caller. Handle `CryptographicException` as 422. Key from `CLINICAL_AES_KEY` env var with DPAPI fallback.

---

## Dependent Tasks

- **TASK_001 (us_038)** — `AesEncryptionService` + `ClinicalDocument` entity

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/ClinicalDocs/DownloadDocumentEndpoint.cs`
- `src/ClinicalHealthcare.Infrastructure/Security/AesEncryptionService.cs` — add `Decrypt` method + DPAPI fallback

---

## Implementation Plan

1. Add `Decrypt(string encryptedBlobPath) → Stream` to `AesEncryptionService`: read encrypted file; extract IV from first 16 bytes; decrypt with AES-256-CBC key; return in-memory `MemoryStream`; catch `CryptographicException` → rethrow for endpoint to map to 422.
2. Key resolution in `AesEncryptionService`: if `CLINICAL_AES_KEY` env var set → decode Base64; else → `ProtectedData.Unprotect(stored_encrypted_key, null, DataProtectionScope.LocalMachine)`.
3. Implement `GET /patients/{id}/documents/{docId}` (`[Authorize(Roles="Staff,Admin")]`): load `ClinicalDocument`; verify `PatientId = id` param → 404 if mismatch; call `AesEncryptionService.Decrypt(blobPath)`; return `FileStreamResult` with `content-type: application/pdf`, `content-disposition: attachment; filename="..."`.
4. Catch `CryptographicException` → 422 `{"error":"Document integrity check failed"}`.
5. Stream in chunks using `CryptoStream` to avoid full in-memory buffer for large files.

---

## Current Project State

```
src/ClinicalHealthcare.Infrastructure/Security/
├── IClamAvScanService.cs
├── ClamAvScanService.cs
├── IAesEncryptionService.cs
└── AesEncryptionService.cs  (encrypt only; add decrypt)
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Security/AesEncryptionService.cs` | Add Decrypt method + DPAPI fallback |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Security/IAesEncryptionService.cs` | Add Decrypt to interface |
| CREATE | `src/ClinicalHealthcare.Api/Features/ClinicalDocs/DownloadDocumentEndpoint.cs` | GET /patients/{id}/documents/{docId} |

---

## External References

- [AES-256-CBC .NET CryptoStream](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cryptostream)
- [Windows DPAPI ProtectedData](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.protecteddata)

---

## Build Commands

```bash
dotnet build
```

---

## Implementation Validation Strategy

- Staff downloads document → PDF received correctly.
- Admin downloads document → PDF received.
- Patient attempts download → 403.
- Corrupt encrypted file → 422 `"Document integrity check failed"`.
- `CLINICAL_AES_KEY` absent on Windows → DPAPI fallback used; decryption succeeds.
- No temp file created on disk during decryption.

---

## Implementation Checklist

- [x] **[AC-001]** Key from `CLINICAL_AES_KEY` env var; DPAPI fallback if absent
- [x] **[AC-002]** Decryption in-memory via `CryptoStream`; no temp file written
- [x] **[AC-003]** Download restricted to Staff and Admin (`[Authorize(Roles="Staff,Admin")]`)
- [x] **[AC-004]** `CryptographicException` caught → 422
- [x] **[AC-005]** `GET /patients/{id}/documents/{docId}` streams decrypted PDF
- [x] `IAesEncryptionService` updated with `Decrypt` signature
- [x] PatientId validation (route param vs DB record) to prevent IDOR
- [x] `dotnet build` passes with 0 errors
