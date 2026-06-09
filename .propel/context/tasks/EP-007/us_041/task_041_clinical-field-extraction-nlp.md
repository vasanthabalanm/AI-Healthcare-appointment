# Task - TASK_041

## Requirement Reference

- **User Story**: US_041 — Structured clinical field extraction from OCR
- **Story Location**: `.propel/context/tasks/EP-007/us_041/us_041.md`
- **Parent Epic**: EP-007

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | NLP pipeline extracts `VitalSign`, `MedicalHistory`, `Medication`, `Allergy`, `Diagnosis` field types |
| AC-002 | Confidence score from OCR is propagated to each `ExtractedClinicalField` |
| AC-003 | Unrecognised text → DEBUG log; no `Unknown` type inserted |
| AC-004 | `ExtractedClinicalField` rows inserted in `ClinicalDbContext` (PostgreSQL) |

### Edge Cases

- OCR text is empty or `OcrStatus=NoData` → extraction job skips; no `ExtractedClinicalField` rows inserted
- Duplicate field value (same fieldName + value for same patient) → handled by de-duplication job (us_042)

---

## Design References

N/A — UI Impact: No

---

## AI References

- **AI Platform**: NLP pipeline (rule-based regex/keyword matching on `RawOcrText`)
- **Field Types**: VitalSign, MedicalHistory, Medication, Allergy, Diagnosis

---

## Mobile References

N/A — Mobile Impact: No

---

## Applicable Technology Stack

| Layer | Technology | Version | Justification |
|-------|-----------|---------|---------------|
| Backend | Hangfire | 1.8.x | Background extraction job |
| Database | PostgreSQL | 16.x | `ExtractedClinicalField` storage in `ClinicalDbContext` |
| Backend | EF Core | 8.x | Npgsql provider |

---

## Task Overview

Implement `ExtractClinicalFieldsJob` Hangfire job. Parse `RawOcrText` using a rule-based NLP pipeline. Extract fields of the 5 defined types. Insert `ExtractedClinicalField` rows in PostgreSQL. Propagate OCR confidence. Log unrecognised text as DEBUG.

---

## Dependent Tasks

- **TASK_001 (us_040)** — OCR job provides `RawOcrText`; enqueues extraction job
- **TASK_001 (us_010)** — `ExtractedClinicalField` entity in PostgreSQL

---

## Impacted Components

- `src/ClinicalHealthcare.Infrastructure/Jobs/ExtractClinicalFieldsJob.cs`
- `src/ClinicalHealthcare.Infrastructure/NLP/ClinicalFieldExtractor.cs`

---

## Implementation Plan

1. Create `ClinicalFieldExtractor.Extract(string rawOcrText, float confidence) → IList<ExtractedFieldDto>`: define regex/keyword patterns for each field type:
   - `VitalSign`: BP, HR, temperature, weight, height patterns.
   - `MedicalHistory`: ICD-10 code patterns, chronic condition keywords.
   - `Medication`: drug name + dosage pattern.
   - `Allergy`: allergy/reaction keyword patterns.
   - `Diagnosis`: diagnosis: / assessment: patterns.
   - Unrecognised → DEBUG log; skip.
2. Implement `ExtractClinicalFieldsJob.Execute(documentId)`: load `ClinicalDocument`; if `OcrStatus=NoData` → return; call `ClinicalFieldExtractor.Extract(rawOcrText, confidence)`; insert `ExtractedClinicalField` rows via `ClinicalDbContext`; enqueue `DeduplicateClinicalFieldsJob` (us_042).
3. Enqueue `ExtractClinicalFieldsJob` from `OcrDocumentJob` on success (chain jobs).
4. Register `ClinicalFieldExtractor` in DI.

---

## Current Project State

```
src/ClinicalHealthcare.Infrastructure/Jobs/
└── OcrDocumentJob.cs
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Infrastructure/Jobs/ExtractClinicalFieldsJob.cs` | Hangfire extraction job |
| CREATE | `src/ClinicalHealthcare.Infrastructure/NLP/ClinicalFieldExtractor.cs` | Rule-based NLP pipeline |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Jobs/OcrDocumentJob.cs` | Enqueue ExtractClinicalFieldsJob on OCR success |
| MODIFY | `src/ClinicalHealthcare.Api/Program.cs` | Register ClinicalFieldExtractor |

---

## External References

- [Regex in .NET](https://learn.microsoft.com/en-us/dotnet/standard/base-types/regular-expressions)
- [Npgsql EF Core](https://www.npgsql.org/efcore/)

---

## Build Commands

```bash
dotnet build
```

---

## Implementation Validation Strategy

- OCR text with "BP: 120/80" → `VitalSign` field extracted.
- OCR text with "Medication: Metformin 500mg" → `Medication` field extracted.
- Unrecognised text → DEBUG log; no `ExtractedClinicalField` row for it.
- `OcrStatus=NoData` → job exits; no fields inserted.
- `ExtractedClinicalField` rows appear in PostgreSQL with confidence propagated.

---

## Implementation Checklist

- [x] **[AC-001]** NLP pipeline extracts all 5 field types (VitalSign/MedicalHistory/Medication/Allergy/Diagnosis)
- [x] **[AC-002]** OCR confidence propagated to each `ExtractedClinicalField.ConfidenceScore`
- [x] **[AC-003]** Unrecognised text → DEBUG log; no Unknown-type rows inserted
- [x] **[AC-004]** `ExtractedClinicalField` rows inserted in `ClinicalDbContext` (PostgreSQL)
- [x] `OcrStatus=NoData` → extraction skipped
- [x] `DeduplicateClinicalFieldsJob` enqueued after extraction
- [x] `ClinicalFieldExtractor` registered in DI
- [x] `dotnet build` passes with 0 errors
