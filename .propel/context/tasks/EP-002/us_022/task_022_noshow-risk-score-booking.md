# Task - TASK_022

## Requirement Reference

- **User Story**: US_022 — No-show risk score at booking
- **Story Location**: `.propel/context/tasks/EP-002/us_022/us_022.md`
- **Parent Epic**: EP-002

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | Risk score (0–100) is calculated and stored on `Appointment` at booking time only |
| AC-002 | Score components: prior no-show count + appointment lead time + intake completion status |
| AC-003 | Score threshold for high-risk flag: `AppSettings.NoShowRiskThreshold` (default 70) |
| AC-004 | Score is recalculated only at booking time; not recalculated on subsequent updates |
| AC-005 | `Appointment.NoShowRiskScore` and `Appointment.IsHighRisk` stored in SQL Server |

### Edge Cases

- First-time patient (no prior no-shows, no intake) → baseline score based on lead time only
- Lead time < 24h → maximum lead-time risk contribution

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
| Backend | ASP.NET Core Web API | 8 LTS | Business logic in feature endpoint |
| Database | SQL Server | 2022 / Express | Score storage in Appointment entity |
| Backend | EF Core | 8.x | Data access |

---

## Task Overview

Implement rule-based no-show risk scoring in the `BookAppointmentEndpoint`. Calculate score from three components (prior no-shows, lead time, intake completeness), apply configurable threshold, and store `NoShowRiskScore` + `IsHighRisk` on the `Appointment` entity.

---

## Dependent Tasks

- **TASK_001 (us_019)** — `BookAppointmentEndpoint` (risk score calculated here)
- **TASK_001 (us_007)** — `Appointment` entity (add score fields)

---

## Impacted Components

- `src/ClinicalHealthcare.Infrastructure/Services/NoShowRiskScoreService.cs`
- `src/ClinicalHealthcare.Api/Features/Appointments/BookAppointmentEndpoint.cs`
- `src/ClinicalHealthcare.Infrastructure/Entities/Appointment.cs`
- `src/ClinicalHealthcare.Infrastructure/Configuration/AppSettings.cs`

---

## Implementation Plan

1. Add `NoShowRiskScore` (int) and `IsHighRisk` (bool) to `Appointment` entity; create migration.
2. Create `NoShowRiskScoreService` with `CalculateAsync(patientId, slotTime, appDbContext)`:
   - Component 1: query `Appointments` for patient where `Status=NoShow`; each no-show = +20 points (max 60).
   - Component 2: lead time = `slotTime - UtcNow`; <24h = +30, 24–72h = +15, >72h = +0.
   - Component 3: no completed IntakeRecord for patient = +10; completed = +0.
   - Total = sum of components (capped at 100).
3. Register `NoShowRiskScoreService` in DI.
4. In `BookAppointmentEndpoint`, after slot validation, call `NoShowRiskScoreService.CalculateAsync(...)`; set `Appointment.NoShowRiskScore` and `Appointment.IsHighRisk = score >= AppSettings.NoShowRiskThreshold`.
5. Add `NoShowRiskThreshold = 70` to `AppSettings`.
6. Create EF Core migration for new `Appointment` fields.

---

## Current Project State

```
src/ClinicalHealthcare.Infrastructure/Entities/Appointment.cs  (existing)
src/ClinicalHealthcare.Api/Features/Appointments/BookAppointmentEndpoint.cs  (existing)
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Infrastructure/Services/NoShowRiskScoreService.cs` | Rule-based risk scoring |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Entities/Appointment.cs` | Add NoShowRiskScore + IsHighRisk fields |
| MODIFY | `src/ClinicalHealthcare.Api/Features/Appointments/BookAppointmentEndpoint.cs` | Call risk score service before saving |
| MODIFY | `src/ClinicalHealthcare.Infrastructure/Configuration/AppSettings.cs` | Add NoShowRiskThreshold = 70 |
| CREATE | `src/ClinicalHealthcare.Infrastructure.SqlMigrations/Migrations/*_AppointmentRiskScore.cs` | Migration |

---

## External References

- [Rule-Based Risk Scoring Patterns](https://martinfowler.com/bliki/RuleObject.html)

---

## Build Commands

```bash
dotnet ef migrations add AppointmentRiskScore --project src/ClinicalHealthcare.Infrastructure.SqlMigrations --startup-project src/ClinicalHealthcare.Api --context ApplicationDbContext
dotnet build
```

---

## Implementation Validation Strategy

- Patient with 3 prior no-shows, lead time 12h, no intake → score = 60+30+10 = 100; `IsHighRisk=true`.
- Patient with 0 no-shows, lead time 5 days, intake complete → score = 0; `IsHighRisk=false`.
- Score stored on `Appointment` in DB; not recalculated on PATCH.
- `AppSettings.NoShowRiskThreshold` configurable; default 70.
- `dotnet build` → 0 errors.

---

## Implementation Checklist

- [x] **[AC-001]** Risk score (0–100) calculated and stored at booking time only
- [x] **[AC-002]** Three components: prior no-show count + lead time + intake completion
- [x] **[AC-003]** `IsHighRisk = score >= AppSettings.NoShowRiskThreshold` (default 70)
- [x] **[AC-004]** Score not recalculated on subsequent appointment updates
- [x] **[AC-005]** `NoShowRiskScore` and `IsHighRisk` persisted in SQL Server via migration
- [x] `NoShowRiskScoreService` registered in DI
- [x] Score capped at 100
- [x] `dotnet build` passes with 0 errors
