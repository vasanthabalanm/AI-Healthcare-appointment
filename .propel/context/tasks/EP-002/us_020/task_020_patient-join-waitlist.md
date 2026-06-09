# Task - TASK_020

## Requirement Reference

- **User Story**: US_020 — Patient joins waitlist
- **Story Location**: `.propel/context/tasks/EP-002/us_020/us_020.md`
- **Parent Epic**: EP-002

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `POST /waitlist` creates a `WaitlistEntry` with `Status=Active`; returns 201 |
| AC-002 | Patient cannot have two Active waitlist entries (filtered partial unique index enforced) |
| AC-003 | Request for a past slot date returns 400 |
| AC-004 | Duplicate waitlist attempt returns 409 |

### Edge Cases

- Patient already has Active entry → 409 (unique constraint violation from partial index in us_008)
- `SlotDate` in the past → 400 `{"error":"Cannot join waitlist for a past slot"}`

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
| Database | SQL Server | 2022 / Express | `WaitlistEntry` storage |
| Backend | EF Core | 8.x | Entity persistence |

---

## Task Overview

Implement `POST /waitlist` for patients. Validate slot date is in the future. Detect duplicate Active entries (surface DB unique constraint as 409). Create `WaitlistEntry` with `Status=Active`.

---

## Dependent Tasks

- **TASK_001 (us_008)** — `WaitlistEntry` entity + partial unique index

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Appointments/JoinWaitlistEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Appointments/JoinWaitlistRequest.cs`

---

## Implementation Plan

1. Create `JoinWaitlistRequest` DTO: `SlotId`, `PreferredSlotDate`.
2. Implement `POST /waitlist` (`[Authorize(Roles="Patient")]`): validate `PreferredSlotDate > UtcNow.Date` → 400 if past; load Slot; insert `WaitlistEntry` with `PatientId` from JWT, `Status=Active`, `QueuedAt=UtcNow`; catch `DbUpdateException` with unique constraint message → 409; return 201.
3. Check existing Active entry before insert (application-level guard to avoid relying solely on DB exception for better error messages).

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Appointments/
├── GetSlotsEndpoint.cs
└── BookAppointmentEndpoint.cs
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/Appointments/JoinWaitlistEndpoint.cs` | POST /waitlist |
| CREATE | `src/ClinicalHealthcare.Api/Features/Appointments/JoinWaitlistRequest.cs` | Waitlist DTO |

---

## External References

- [EF Core Saving Data](https://learn.microsoft.com/en-us/ef/core/saving/)

---

## Build Commands

```bash
dotnet build
```

---

## Implementation Validation Strategy

- `POST /waitlist` with future date → 201; WaitlistEntry in DB with `Status=Active`.
- Second `POST /waitlist` for same patient → 409.
- `POST /waitlist` with past date → 400.
- `dotnet build` → 0 errors.

---

## Implementation Checklist

- [x] **[AC-001]** `POST /waitlist` creates `WaitlistEntry` with `Status=Active`; returns 201
- [x] **[AC-002]** Duplicate Active entry → 409 (application guard + DB unique constraint)
- [x] **[AC-003]** Past slot date → 400 with descriptive error
- [x] **[AC-004]** 409 returned on duplicate attempt with clear error message
- [x] Endpoint requires `[Authorize(Roles="Patient")]`
- [x] `PatientId` sourced from JWT claims (not request body)
- [x] `QueuedAt` set to `UtcNow` server-side
- [x] `dotnet build` passes with 0 errors
