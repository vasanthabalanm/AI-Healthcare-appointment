# Bug Fix Task - BUG_PAT_001 through BUG_PAT_005

## Bug Report Reference
- Bug ID: BUG_PAT_001-005
- Source: Direct user report — patient feature area (intake, booking, waitlist, slot legend)

---

## Bug Summary

### Issue Classification
- **Priority**: High
- **Severity**: Core patient workflows non-functional
- **Affected Version**: Current HEAD
- **Environment**: Local dev — Angular (port 4200) → .NET API (port 5153), no Redis, no Rasa

### Steps to Reproduce

#### BUG_PAT_001 — CORS blocks all authenticated API calls
1. Start Angular dev server (`npm start`) and API (`dotnet run`)
2. Log in as a patient; token stored in localStorage
3. Navigate to `/patient/intake/manual` and submit the form
4. **Expected**: 201 Created
5. **Actual**: HTTP status 0 (CORS error) → UI shows "Failed to submit intake. Please try again."

#### BUG_PAT_002 — Manual Intake 409 conflict loop
1. Submit intake successfully once as a patient
2. Return to `/patient/intake/manual` and submit again (even after first intake is superseded)
3. **Expected**: New intake record created
4. **Actual**: 409 "Patient already has an active intake record." — `IsLatest` filter missing

#### BUG_PAT_003 — Waitlist contract mismatch
1. Navigate to `/patient/waitlist`, select a slot, enter appointment ID, click Join Waitlist
2. **Expected**: 201 - joined waitlist
3. **Actual**: 400 "Cannot join waitlist for a past slot." — frontend sends `confirmedAppointmentId` (unknown field); backend expects `preferredSlotDate` (missing → defaults to 0001-01-01)

#### BUG_PAT_004 — BookComponent missing RouterModule
1. Navigate to `/patient/book`, select a date with slots, observe slot panel
2. Click "Join waitlist for a different slot" link
3. **Expected**: Navigates to `/patient/waitlist`
4. **Actual**: Link renders as plain text, Angular `routerLink` directive not applied (RouterModule not imported)

#### BUG_PAT_005 — Slot legend "Fully booked" never triggered
1. Navigate to `/patient/book`
2. **Expected**: Calendar days with all slots booked show grey dot with "Fully booked" legend
3. **Actual**: `isFullyBooked` is hardcoded `false` in `calDays()` — grey dot never appears

**Error Output**:
```text
BUG_PAT_001: HttpErrorResponse { status: 0, statusText: "Unknown Error" }
BUG_PAT_002: HTTP 409 { error: "Patient already has an active intake record." }
BUG_PAT_003: HTTP 400 { error: "Cannot join waitlist for a past slot." }
BUG_PAT_004: routerLink attribute is a no-op (no error, silent failure)
BUG_PAT_005: No error — logical bug, wrong data
```

---

### Root Cause Analysis

#### BUG_PAT_001 — CORS
- **File**: `src/ClinicalHealthcare.Api/Program.cs:265`
- **Component**: CORS middleware configuration
- **Cause**: `GetSection("AllowedOrigins").Get<string[]>()` returns `null` when `ALLOWED_ORIGINS=http://localhost:4200` is set as a scalar env var (DotNetEnv). ASP.NET Core array binding requires `__0`, `__1` index notation. Scalar env var does not bind to `string[]` → `origins = []` → no CORS policy → browser blocks all preflight requests.
- **[SOURCE:INFERRED]** Basis: ASP.NET Core ConfigurationBinder does not auto-convert scalar sections to `string[]`; confirmed by CORS policy conditional `if (origins.Length > 0)`.

#### BUG_PAT_002 — Manual Intake
- **File**: `src/ClinicalHealthcare.Api/Features/Intake/SubmitManualIntakeEndpoint.cs:86`
- **Component**: SubmitManualIntakeEndpoint
- **Cause**: `AnyAsync(r => r.PatientId == patientId, ct)` missing `&& r.IsLatest` predicate. Code comment says "reject if patient already has an active (IsLatest=true) intake" but implementation omits the condition, treating ANY historical record as a blocker.
- **[SOURCE:INPUT]** Basis: Code comment says `IsLatest=true` but query has no such filter.

#### BUG_PAT_003 — Waitlist
- **File**: `clinical-hub/src/app/core/services/appointment.service.ts:JoinWaitlistPayload`
- **File**: `clinical-hub/src/app/features/patient/waitlist/waitlist.component.ts:join()`
- **Component**: WaitlistComponent + appointment.service.ts
- **Cause**: `JoinWaitlistPayload` = `{ preferredSlotId, confirmedAppointmentId }`. Backend `JoinWaitlistRequest` = `(int? PreferredSlotId, DateOnly PreferredSlotDate)`. Fields `confirmedAppointmentId` and `preferredSlotDate` don't match. `PreferredSlotDate` is a non-nullable positional parameter → defaults to `0001-01-01` → past date check fails.
- **[SOURCE:INPUT]** Basis: Direct comparison of service interface and backend record.

#### BUG_PAT_004 — RouterModule
- **File**: `clinical-hub/src/app/features/patient/book/book.component.ts:7`
- **Component**: BookComponent
- **Cause**: `imports: [CommonModule]` — `RouterModule` missing. Template uses `routerLink="/patient/waitlist"`.
- **[SOURCE:INPUT]** Basis: Angular standalone component must import RouterModule to use routerLink.

#### BUG_PAT_005 — isFullyBooked
- **File**: `clinical-hub/src/app/features/patient/book/book.component.ts:calDays()`
- **Component**: BookComponent
- **Cause**: `isFullyBooked: false` hardcoded for every day. `prefetchMonth()` only sets `availableDays` (days with > 0 available slots) but never computes fully-booked state. `GetSlotsEndpoint` only returns `isAvailable=true` slots — impossible to distinguish "fully booked" from "no slots scheduled" without a dedicated availability endpoint.
- **[SOURCE:INFERRED]** Basis: Legend shows 3 states but only 2 are computable from current data.

---

### Impact Assessment
- **Affected Features**: All patient-facing features (intake, booking, waitlist)
- **User Impact**: 100% of patient users cannot complete any core workflow when using Angular dev server against local API
- **Data Integrity Risk**: No — bugs are presentation/networking layer
- **Security Implications**: None — CORS fix only adds the dev origin; auth is unaffected

---

## Fix Overview

1. **CORS**: Add scalar-string fallback in Program.cs CORS block so `ALLOWED_ORIGINS=http://localhost:4200` works.
2. **Manual Intake**: Add `&& r.IsLatest` to the duplicate-check predicate.
3. **Waitlist**: Rewrite `JoinWaitlistPayload`, `WaitlistComponent.join()`, and the whole WaitlistComponent UI (contract alignment + styles + remove `confirmedAppointmentId` concept).
4. **RouterModule**: Add `RouterModule` to BookComponent imports.
5. **Slot legend**: Modify `GetSlotsEndpoint` to accept `allSlots=true` query param returning unavailable slots too (with `isAvailable` field); update BookComponent `prefetchMonth` to compute `isFullyBooked`.

---

## Fix Dependencies
- No external dependencies; all fixes are self-contained

---

## Impacted Components

### Backend (.NET)
- `src/ClinicalHealthcare.Api/Program.cs` — UPDATED (CORS scalar fallback)
- `src/ClinicalHealthcare.Api/Features/Intake/SubmitManualIntakeEndpoint.cs` — UPDATED (IsLatest filter)
- `src/ClinicalHealthcare.Api/Features/Appointments/GetSlotsEndpoint.cs` — UPDATED (allSlots param + isAvailable in DTO)

### Frontend (Angular)
- `clinical-hub/src/app/core/services/appointment.service.ts` — UPDATED (JoinWaitlistPayload, Slot interface)
- `clinical-hub/src/app/features/patient/book/book.component.ts` — UPDATED (RouterModule, isFullyBooked logic)
- `clinical-hub/src/app/features/patient/waitlist/waitlist.component.ts` — UPDATED (full rewrite)

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | `src/ClinicalHealthcare.Api/Program.cs` | Add scalar-string CORS origin fallback |
| MODIFY | `src/ClinicalHealthcare.Api/Features/Intake/SubmitManualIntakeEndpoint.cs` | Add `&& r.IsLatest` to AnyAsync |
| MODIFY | `src/ClinicalHealthcare.Api/Features/Appointments/GetSlotsEndpoint.cs` | Add `allSlots` query param, add `IsAvailable` to SlotDto |
| MODIFY | `clinical-hub/src/app/core/services/appointment.service.ts` | Fix JoinWaitlistPayload, add IsAvailable to Slot |
| MODIFY | `clinical-hub/src/app/features/patient/book/book.component.ts` | Add RouterModule, fix isFullyBooked |
| MODIFY | `clinical-hub/src/app/features/patient/waitlist/waitlist.component.ts` | Fix contract, remove apptId, add styles |

---

## Implementation Plan

1. Fix CORS in Program.cs (unblocks all subsequent testing)
2. Fix Manual Intake IsLatest filter
3. Fix GetSlotsEndpoint: add `allSlots` param and `IsAvailable` to SlotDto
4. Fix appointment.service.ts: update Slot interface, fix JoinWaitlistPayload
5. Fix BookComponent: add RouterModule, update prefetchMonth for isFullyBooked
6. Rewrite WaitlistComponent: fix contract, styles, remove wrong UI

---

## Regression Prevention Strategy

- [ ] Unit test: CORS policy resolves scalar env var to single-origin array
- [ ] Unit test: SubmitManualIntakeEndpoint — second submission with `IsLatest=false` record succeeds
- [ ] Unit test: JoinWaitlistEndpoint — valid PreferredSlotDate in future succeeds
- [ ] Integration: POST /intake/manual with valid JWT returns 201
- [ ] Integration: POST /appointments with valid slot returns 201
- [ ] Integration: POST /waitlist with valid date returns 201

---

## Rollback Procedure
1. Revert `Program.cs` CORS change — `ALLOWED_ORIGINS__0` notation in .env or appsettings.json
2. Revert `SubmitManualIntakeEndpoint.cs` — remove `&& r.IsLatest`
3. Frontend rollback — `git checkout` affected component files

---

## Build Commands
```powershell
# Backend rebuild
dotnet build src/ClinicalHealthcare.Api --configuration Debug

# Frontend
cd clinical-hub && npm start

# Tests
dotnet test tests/ClinicalHealthcare.Infrastructure.Tests
```

---

## Implementation Validation Strategy

- [ ] POST /intake/manual returns 201 from Angular app (no CORS error)
- [ ] POST /appointments returns 201 for a valid available slot
- [ ] POST /waitlist with preferredSlotDate returns 201
- [ ] "Join waitlist for a different slot" link navigates correctly
- [ ] Calendar shows grey dot for fully-booked days
- [ ] All 721 existing tests still pass

---

## Implementation Checklist

- [ ] Program.cs CORS fallback added
- [ ] SubmitManualIntakeEndpoint.cs `IsLatest` filter added
- [ ] GetSlotsEndpoint allSlots param + IsAvailable DTO field
- [ ] appointment.service.ts Slot + JoinWaitlistPayload updated
- [ ] BookComponent RouterModule + isFullyBooked logic
- [ ] WaitlistComponent full rewrite (contract + styles)
