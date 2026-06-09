# Task - TASK_040

## Requirement Reference

- **User Story**: US_040 — OCR via Tesseract 5.x Hangfire job
- **Story Location**: `.propel/context/tasks/EP-007/us_040/us_040.md`
- **Parent Epic**: EP-007

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | Hangfire `OcrDocumentJob` decrypts document then performs OCR via Tesseract 5.x P/Invoke |
| AC-002 | Confidence ≥0.75 → `OcrStatus=Extracted`, <0.75 → `LowConfidence`, empty → `NoData` |
| AC-003 | Multi-page PDF → average confidence across pages |
| AC-004 | `ClinicalDocument.RawOcrText` stored after successful extraction |
| AC-005 | Job retries 3× on failure; dead-letter after exhaustion |

### Edge Cases

- Tesseract P/Invoke fails (native library not found) → 3× retry; dead-letter; `OcrStatus=NoData`
- Encrypted document has been deleted from disk → job fails; `OcrStatus=NoData`

---

## Design References

N/A — UI Impact: No

---

## AI References

- **AI Platform**: Tesseract 5.x (via .NET NuGet P/Invoke)
- **Confidence Threshold**: 0.75

---

## Mobile References

N/A — Mobile Impact: No

---

## Applicable Technology Stack

| Layer | Technology | Version | Justification |
|-------|-----------|---------|---------------|
| Backend | Tesseract (.NET NuGet) | 5.x | OCR via P/Invoke per design.md |
| Backend | Hangfire | 1.8.x | Background job per design.md |
| Backend | AES-256-CBC | .NET 8 BCL | Decrypt before OCR |

---

## Task Overview

Implement `OcrDocumentJob` as a full Hangfire job. Decrypt the clinical document in-memory. Run Tesseract 5.x OCR (P/Invoke). Calculate average confidence for multi-page PDFs. Store `RawOcrText` and `OcrStatus` on `ClinicalDocument`. 3× retry.

---

## Dependent Tasks

- **TASK_001 (us_038)** — `OcrDocumentJob` stub + `AesEncryptionService`
- **TASK_001 (us_009)** — `ClinicalDocument` entity with `OcrStatus` field

---

## Impacted Components

- `src/ClinicalHealthcare.Infrastructure/Jobs/OcrDocumentJob.cs`
- `src/ClinicalHealthcare.Infrastructure/OCR/TesseractOcrService.cs`

---

## Implementation Plan

1. Add `Tesseract` NuGet (`Tesseract` 5.x) to `ClinicalHealthcare.Infrastructure`; ensure `tessdata/eng.traineddata` is bundled.
2. Create `ITesseractOcrService` / `TesseractOcrService`: `OcrAsync(Stream pdfStream) → (string rawText, float averageConfidence)`; iterate PDF pages (use `PDFiumSharp` or `PdfPig` for page rendering); run `Tesseract.TesseractEngine.Process(pixImage)`; collect per-page confidence; average across pages.
3. Implement `OcrDocumentJob.Execute(documentId)` (replaces stub): load `ClinicalDocument`; call `AesEncryptionService.Decrypt(blobPath)` → in-memory stream; call `TesseractOcrService.OcrAsync(stream)` → `(rawText, confidence)`; set `OcrStatus`: ≥0.75 → Extracted, <0.75 → LowConfidence, rawText empty → NoData; save `RawOcrText` + `OcrStatus` to `ClinicalDocument`.
4. Decorate `OcrDocumentJob` with `[AutomaticRetry(Attempts=3)]`.
5. Register `ITesseractOcrService` in DI.

---

## Current Project State

```
src/ClinicalHealthcare.Infrastructure/Jobs/
└── OcrDocumentJob.cs  (stub from us_038)
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Jobs/OcrDocumentJob.cs` | Replace stub with full OCR implementation |
| CREATE | `src/ClinicalHealthcare.Infrastructure/OCR/ITesseractOcrService.cs` | OCR service abstraction |
| CREATE | `src/ClinicalHealthcare.Infrastructure/OCR/TesseractOcrService.cs` | Tesseract 5.x P/Invoke implementation |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Entities/ClinicalDocument.cs` | Add RawOcrText field |
| MODIFY | `src/ClinicalHealthcare.Api/Program.cs` | Register ITesseractOcrService |

---

## External References

- [Tesseract .NET NuGet](https://github.com/charlesw/tesseract)
- [PdfPig](https://uglytoad.github.io/PdfPig/) (PDF page rendering for Tesseract)

---

## Build Commands

```bash
dotnet add src/ClinicalHealthcare.Infrastructure package Tesseract
dotnet add src/ClinicalHealthcare.Infrastructure package PdfPig
dotnet build
```

---

## Implementation Validation Strategy

- Upload PDF with clear text → `OcrStatus=Extracted`; `RawOcrText` populated; confidence ≥0.75.
- Upload low-quality scan → `OcrStatus=LowConfidence`; confidence <0.75.
- Multi-page PDF → average confidence used.
- Tesseract P/Invoke fails → 3× retry → dead-letter; `OcrStatus=NoData`.
- `dotnet build` → 0 errors.

---

## Implementation Checklist

- [x] **[AC-001]** `OcrDocumentJob` decrypts document in-memory; runs Tesseract P/Invoke
- [x] **[AC-002]** Confidence ≥0.75 → Extracted; <0.75 → LowConfidence; empty → NoData
- [x] **[AC-003]** Multi-page PDF: average confidence across all pages
- [x] **[AC-004]** `ClinicalDocument.RawOcrText` stored after extraction
- [x] **[AC-005]** `[AutomaticRetry(Attempts=3)]` on job; dead-letter after exhaustion
- [x] `tessdata/eng.traineddata` bundled in output directory
- [x] No temp file written during OCR processing
- [x] `dotnet build` passes with 0 errors
