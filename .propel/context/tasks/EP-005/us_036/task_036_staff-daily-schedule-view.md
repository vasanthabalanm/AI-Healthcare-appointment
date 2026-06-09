# Task - TASK_036

## Requirement Reference

- **User Story**: US_036 — Staff daily schedule view
- **Story Location**: `.propel/context/tasks/EP-005/us_036/us_036.md`
- **Parent Epic**: EP-005

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `GET /schedule/today` returns appointments ordered by slot time ASC |
| AC-002 | Response includes `intakeStatus` (Submitted/Pending/NA) for each appointment |
| AC-003 | Response includes `riskFlag` (high-risk boolean) from `Appointment.IsHighRisk` |
| AC-004 | Optional `?date=` parameter allows viewing schedule for a different date |
| AC-005 | Results paginated: 50 per page |

### Edge Cases

- `?date=` in the past → still returns historical schedule (no date restriction)
- No appointments for the date → empty array with `totalCount=0`

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
| Backend | ASP.NET Core Web API | 8 LTS | Endpoint hosting |
| Backend | EF Core | 8.x | Appointment + IntakeRecord join |
| Database | SQL Server | 2022 / Express | Schedule data |

---

## Task Overview

Implement `GET /schedule/today[?date=][&page=]` for Staff. Join `Appointment` with `IntakeRecord` to determine `intakeStatus`. Include `riskFlag` from `Appointment.IsHighRisk`. Paginate 50/page.

---

## Dependent Tasks

- **TASK_001 (us_007)** — `Appointment` entity
- **TASK_001 (us_008)** — `IntakeRecord` entity
- **TASK_001 (us_022)** — `Appointment.IsHighRisk` field

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Staff/GetDailyScheduleEndpoint.cs`

---

## Implementation Plan

1. Implement `GET /schedule/today` (`[Authorize(Roles="Staff,Admin")]`): parse `?date=` or default to `UtcNow.Date`; query `Appointments` where `Slot.SlotTime.Date = date` and `Status != Cancelled`; left join `IntakeRecord` (latest version) by `PatientId`; map `intakeStatus`: if `IntakeRecord` exists → `Submitted`; if appointment exists but no intake → `Pending`; if walk-in/no context → `NA`; include `IsHighRisk` as `riskFlag`; order by `SlotTime ASC`; paginate 50/page; return `{totalCount, pageCount, data[]}`.

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Staff/
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/Staff/GetDailyScheduleEndpoint.cs` | GET /schedule/today |

---

## External References

- [EF Core Left Join](https://learn.microsoft.com/en-us/ef/core/querying/complex-query-operators)

---

## Build Commands

```bash
dotnet build
```

---

## Implementation Validation Strategy

- `GET /schedule/today` → appointments for today, ordered ASC, 50/page.
- Appointment with submitted intake → `intakeStatus=Submitted`.
- Appointment with no intake → `intakeStatus=Pending`.
- High-risk appointment → `riskFlag=true`.
- `?date=2024-01-01` → returns historical schedule for that date.

---

## Implementation Checklist

- [x] **[AC-001]** `GET /schedule/today` returns appointments ordered by slot time ASC
- [x] **[AC-002]** `intakeStatus` field computed from IntakeRecord join
- [x] **[AC-003]** `riskFlag` from `Appointment.IsHighRisk`
- [x] **[AC-004]** `?date=` parameter overrides default date
- [x] **[AC-005]** Results paginated at 50/page with `totalCount` in response
- [x] Cancelled appointments excluded from schedule
- [x] Endpoint requires `[Authorize(Roles="Staff,Admin")]`
- [x] `dotnet build` passes with 0 errors
