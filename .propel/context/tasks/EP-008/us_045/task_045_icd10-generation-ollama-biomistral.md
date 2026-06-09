# Task - TASK_045

## Requirement Reference

- **User Story**: US_045 — ICD-10 generation via Ollama BioMistral 7B
- **Story Location**: `.propel/context/tasks/EP-008/us_045/us_045.md`
- **Parent Epic**: EP-008

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `POST /patients/{id}/generate-codes {type:"ICD10"}` → 409 if not Verified; 202 + Hangfire job if Verified |
| AC-002 | Hangfire job calls Ollama `POST localhost:11434/api/chat`; inserts `MedicalCodeSuggestion` rows with `codeType=ICD10`, `status=Pending` |
| AC-003 | `confidence_score < 0.60` → `lowConfidenceFlag=true` |
| AC-004 | Ollama timeout (30s) → retry 30s/60s/120s; dead-letter after 3 failures; show "engine unavailable" |
| AC-005 | Dead-letter → no partial Pending rows from failed attempt; coding dashboard shows "AI engine unavailable" |

### Edge Cases

- Max 20 suggestions; extras silently dropped + DEBUG log
- Malformed code (fails regex `[A-Z][0-9]{2}(\.[0-9]{1,4})?`) → rejected; WARNING logged

---

## Design References

N/A — UI Impact: No

---

## AI References

- **AI Platform**: Ollama + BioMistral 7B Q4_K_M
- **Endpoint**: `POST localhost:11434/api/chat`
- **Timeout**: 30s
- **Reference**: AIR-003
- **Confidence threshold**: 0.60 → `lowConfidenceFlag`
- **Code validation regex**: `[A-Z][0-9]{2}(\.[0-9]{1,4})?`
- **Max suggestions**: 20

---

## Mobile References

N/A — Mobile Impact: No

---

## Applicable Technology Stack

| Layer | Technology | Version | Justification |
|-------|-----------|---------|---------------|
| Backend | ASP.NET Core Web API | 8 LTS | Endpoint hosting |
| Backend | Hangfire | 1.8.x | Background ICD-10 generation job |
| AI | Ollama BioMistral 7B Q4_K_M | 3.x | Code generation per design.md |
| Database | SQL Server 2022 | Express | `MedicalCodeSuggestion` in `ApplicationDbContext` |

---

## Task Overview

Implement `POST /patients/{id}/generate-codes` for ICD-10 type. Enqueue `GenerateIcd10CodesJob`. Job calls Ollama (30s timeout), parses response, validates codes, inserts up to 20 `MedicalCodeSuggestion` rows. Handles dead-letter as atomic rollback.

---

## Dependent Tasks

- **TASK_001 (us_044)** — Patient must be `ClinicalStatus=Verified`
- **TASK_001 (us_047)** — `MedicalCodeSuggestion` entity used by verification interface

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Coding/GenerateCodesEndpoint.cs`
- `src/ClinicalHealthcare.Infrastructure/Jobs/GenerateIcd10CodesJob.cs`
- `src/ClinicalHealthcare.Infrastructure/AI/OllamaCodeGenerationService.cs`

---

## Implementation Plan

1. Implement `POST /patients/{id}/generate-codes` (`[Authorize(Roles="Staff,Admin")]`):
   - Check `ClinicalStatus=Verified` → 409 if not.
   - Check `type` param = `ICD10`; enqueue `GenerateIcd10CodesJob(patientId)` via Hangfire.
   - Return 202.
2. Implement `OllamaCodeGenerationService.GenerateIcd10Async(patientId) → IList<CodeSuggestionDto>`:
   - Build prompt using patient 360° view data + `ICD10_PROMPT_TEMPLATE`.
   - `HttpClient.PostAsync("http://localhost:11434/api/chat", payload, CancellationToken(30s))`.
   - Parse response; extract code + confidence_score per suggestion.
   - Validate each code against `[A-Z][0-9]{2}(\.[0-9]{1,4})?` regex; reject invalid + WARNING log.
   - Cap at 20; drop extras + DEBUG log.
   - Return list.
3. Implement `GenerateIcd10CodesJob.Execute(patientId)` decorated with `[AutomaticRetry(Attempts=3, DelaysInSeconds=new[]{30,60,120})]`:
   - Call `OllamaCodeGenerationService.GenerateIcd10Async(patientId)` in transaction.
   - On success: insert `MedicalCodeSuggestion` rows (ICD10, Pending, lowConfidenceFlag if score<0.60).
   - On dead-letter (all retries exhausted): rollback any partial rows; update job status record to "engine unavailable".
4. Register `OllamaCodeGenerationService` in DI.

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Coding/
└── README.md
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/Coding/GenerateCodesEndpoint.cs` | POST /patients/{id}/generate-codes |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Jobs/GenerateIcd10CodesJob.cs` | Hangfire ICD-10 generation job |
| CREATE | `src/ClinicalHealthcare.Infrastructure/AI/OllamaCodeGenerationService.cs` | Ollama HTTP client + parse |
| MODIFY | `src/ClinicalHealthcare.Api/Program.cs` | Register OllamaCodeGenerationService |

---

## External References

- [Ollama API docs](https://github.com/ollama/ollama/blob/main/docs/api.md)
- [Hangfire retry](https://docs.hangfire.io/en/latest/background-processing/dealing-with-exceptions.html)

---

## Build Commands

```bash
dotnet build
```

---

## Implementation Validation Strategy

- Unverified patient → 409.
- Verified patient → 202; `GenerateIcd10CodesJob` enqueued.
- Ollama returns 3 codes → all valid format → 3 `MedicalCodeSuggestion` rows; `status=Pending`.
- Code with confidence 0.45 → `lowConfidenceFlag=true`.
- Ollama returns malformed code → rejected; WARNING log; not inserted.
- Ollama returns 25 codes → 20 inserted; 5 dropped + DEBUG log.
- Ollama timeout → 3 retries; dead-letter → no partial rows.

---

## Implementation Checklist

- [x] **[AC-001]** Unverified patient → 409; Verified → 202 + job enqueued
- [x] **[AC-002]** Job inserts `MedicalCodeSuggestion` rows: `codeType=ICD10`, `status=Pending`
- [x] **[AC-003]** `confidence_score < 0.60` → `lowConfidenceFlag=true`
- [x] **[AC-004]** Ollama timeout 30s; retry 30s/60s/120s; dead-letter after 3 failures
- [x] **[AC-005]** Dead-letter → no partial Pending rows (transactional rollback)
- [x] Regex `[A-Z][0-9]{2}(\.[0-9]{1,4})?` rejects malformed codes + WARNING log
- [x] Max 20 suggestions; extras dropped + DEBUG log
- [x] `dotnet build` passes with 0 errors
