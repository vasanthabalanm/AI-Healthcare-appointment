# Task - TASK_046

## Requirement Reference

- **User Story**: US_046 — CPT procedure code generation via Ollama BioMistral
- **Story Location**: `.propel/context/tasks/EP-008/us_046/us_046.md`
- **Parent Epic**: EP-008

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `POST /patients/{id}/generate-codes {type:"CPT"}` uses distinct `CPT_PROMPT_TEMPLATE`; independent Hangfire job |
| AC-002 | `MedicalCodeSuggestion` rows with `codeType=CPT` stored separately from ICD-10 |
| AC-003 | "No procedures identified" response → zero rows inserted; API shows "No CPT suggestions available" |
| AC-004 | CPT format validation: regex `^\d{5}$`; non-matching codes rejected + WARNING logged |

### Edge Cases

- Both ICD-10 and CPT jobs can run concurrently (independent Hangfire jobs per type)
- Narrative responses without codes → parse with regex; zero valid codes → zero rows

---

## Design References

N/A — UI Impact: No

---

## AI References

- **AI Platform**: Ollama + BioMistral 7B Q4_K_M
- **Endpoint**: `POST localhost:11434/api/chat`
- **Timeout**: 30s
- **Reference**: AIR-004
- **Prompt template**: `CPT_PROMPT_TEMPLATE` (distinct from ICD-10)
- **Code validation regex**: `^\d{5}$`

---

## Mobile References

N/A — Mobile Impact: No

---

## Applicable Technology Stack

| Layer | Technology | Version | Justification |
|-------|-----------|---------|---------------|
| Backend | ASP.NET Core Web API | 8 LTS | Endpoint hosting |
| Backend | Hangfire | 1.8.x | Background CPT generation job |
| AI | Ollama BioMistral 7B Q4_K_M | 3.x | Code generation per design.md |
| Database | SQL Server 2022 | Express | `MedicalCodeSuggestion` in `ApplicationDbContext` |

---

## Task Overview

Extend `POST /patients/{id}/generate-codes` to handle `type=CPT`. Enqueue `GenerateCptCodesJob` (independent from ICD-10). Job calls Ollama with `CPT_PROMPT_TEMPLATE`, validates with `^\d{5}$`, inserts `MedicalCodeSuggestion` rows with `codeType=CPT`.

---

## Dependent Tasks

- **TASK_001 (us_044)** — Patient must be `ClinicalStatus=Verified`
- **TASK_001 (us_045)** — `OllamaCodeGenerationService` + `GenerateCodesEndpoint` already exist

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Coding/GenerateCodesEndpoint.cs`
- `src/ClinicalHealthcare.Infrastructure/Jobs/GenerateCptCodesJob.cs`
- `src/ClinicalHealthcare.Infrastructure/AI/OllamaCodeGenerationService.cs`

---

## Implementation Plan

1. Add `CPT_PROMPT_TEMPLATE` constant to `OllamaCodeGenerationService`; implement `GenerateCptAsync(patientId)`:
   - Build prompt with `CPT_PROMPT_TEMPLATE`.
   - Call `localhost:11434/api/chat` (30s timeout).
   - Parse response with regex to extract 5-digit codes; reject non-matching + WARNING log.
   - "No procedures identified" response → return empty list.
2. Implement `GenerateCptCodesJob.Execute(patientId)` with `[AutomaticRetry(Attempts=3, DelaysInSeconds=new[]{30,60,120})]`:
   - Call `GenerateCptAsync`; transactional insert of `MedicalCodeSuggestion` rows (`codeType=CPT`, `status=Pending`).
   - Dead-letter: rollback + "engine unavailable" status.
3. Extend `GenerateCodesEndpoint`: when `type=CPT` → enqueue `GenerateCptCodesJob`; can run concurrently with ICD-10 job.
4. Handle zero CPT codes result: return `{"message":"No CPT suggestions available"}` via job result flag.

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Coding/GenerateCodesEndpoint.cs
src/ClinicalHealthcare.Infrastructure/AI/OllamaCodeGenerationService.cs
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Infrastructure/Jobs/GenerateCptCodesJob.cs` | Hangfire CPT generation job |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/AI/OllamaCodeGenerationService.cs` | Add CPT_PROMPT_TEMPLATE + GenerateCptAsync |
| MODIFY | `src/ClinicalHealthcare.Api/Features/Coding/GenerateCodesEndpoint.cs` | Route type=CPT to GenerateCptCodesJob |

---

## External References

- [Ollama API docs](https://github.com/ollama/ollama/blob/main/docs/api.md)

---

## Build Commands

```bash
dotnet build
```

---

## Implementation Validation Strategy

- `type=CPT` → `GenerateCptCodesJob` enqueued independently from ICD-10.
- Valid CPT response → `MedicalCodeSuggestion` rows with `codeType=CPT`.
- "No procedures identified" response → zero rows; response shows "No CPT suggestions available".
- Code `ABC12` fails `^\d{5}$` → rejected + WARNING log.
- Both ICD-10 and CPT jobs enqueued simultaneously → both execute independently.

---

## Implementation Checklist

- [x] **[AC-001]** `type=CPT` uses `CPT_PROMPT_TEMPLATE`; independent `GenerateCptCodesJob`
- [x] **[AC-002]** Rows inserted with `codeType=CPT` separate from ICD-10 rows
- [x] **[AC-003]** "No procedures identified" → zero rows; "No CPT suggestions available" message
- [x] **[AC-004]** Regex `^\d{5}$` validates CPT codes; invalid → rejected + WARNING log
- [x] ICD-10 and CPT jobs can run concurrently without interference
- [x] Dead-letter → rollback partial rows + "engine unavailable" status
- [x] Retry: 30s/60s/120s; 3 attempts max
- [x] `dotnet build` passes with 0 errors
