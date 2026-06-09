# E2E Journey - Clinical Healthcare Platform

## Journey Index

| Journey ID | Name | UC Chain | Business Value | Priority |
|------------|------|----------|----------------|----------|
| E2E-001 | Patient New Visit — Register to Intake | UC-001 → UC-002 → UC-004 → UC-009 | Patient completes full new-visit lifecycle end-to-end | P0 |
| E2E-002 | Waitlist Slot Swap | UC-004 → UC-005 → UC-006 | Automated preferred slot management reduces scheduling gaps | P0 |
| E2E-003 | Staff Clinical Intelligence | UC-002 → UC-017 → UC-020 → UC-021 → UC-022 | Reduces clinical prep from 20 min to 2 min | P0 |
| E2E-004 | Staff Daily Operations | UC-002 → UC-013 → UC-014 → UC-015 → UC-016 | Staff manages complete patient flow for a service day | P0 |
| E2E-005 | Admin User Lifecycle | UC-002 → UC-003 → UC-023 | Admin governs Staff accounts and verifies compliance | P1 |

---

# E2E Journey - Patient New Visit

## Journey Definition
| Field | Value |
|-------|-------|
| Journey ID | E2E-001 |
| UC Chain | UC-001 → UC-002 → UC-004 → UC-009 |
| Business Value | A new patient can self-register, verify their email, book a first appointment, and submit a manual intake form — the complete pre-visit lifecycle — without Staff intervention |
| Session Requirements | New patient account created fresh per test run; JWT persisted across steps via `storageState`; appointment ID passed from booking step to intake step |
| Priority | P0 |
| Source | [SOURCE:INPUT] — Basis: BRD §3 "patient-centric appointment booking system"; UC-001–UC-004–UC-009 are the critical patient onboarding path |

## Journey Steps

### Step 1: UC-001 — Patient Self-Registration
- **Entry Point**: `http://localhost:4200/register`
- **Preconditions**: Clean database state; no account for `e2e-newpatient@test.dev`; SMTP stub active

#### Actions
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Navigate | `http://localhost:4200/register` | Registration form rendered |
| 2 | Fill | `getByLabel('First Name')` → `"E2E"` | Value set |
| 3 | Fill | `getByLabel('Last Name')` → `"Patient"` | Value set |
| 4 | Fill | `getByLabel('Date of Birth')` → `"1988-04-22"` | Value set |
| 5 | Fill | `getByLabel('Phone')` → `"555-E2E1"` | Value set |
| 6 | Fill | `getByLabel('Email')` → `"e2e-newpatient@test.dev"` | Value set |
| 7 | Fill | `getByLabel('Password')` → `"E2ePass@1"` | Value set |
| 8 | Click | `getByRole('button', { name: 'Create Account' })` | Form submitted |
| 9 | Assert | Success message | "Verify your email" message shown |
| 10 | Retrieve token | SMTP stub API | Verification token captured |
| 11 | Navigate | `http://localhost:4200/auth/verify-email?token={token}` | Verification page |
| 12 | Assert | Success message | "Email verified" |

- **Checkpoint**: `GET /patients?email=e2e-newpatient@test.dev` → `emailVerified: true`
- **Shared Data**: `patientEmail: "e2e-newpatient@test.dev"`, `patientPassword: "E2ePass@1"`

---

### Step 2: UC-002 — Login & Session Establishment
- **Entry Point**: `http://localhost:4200/login`
- **Preconditions**: Account verified in Step 1; Step 1 `patientEmail` and `patientPassword` available

#### Actions
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Navigate | `http://localhost:4200/login` | Login form |
| 2 | Fill | `getByLabel('Email')` → `patientEmail` | Value set |
| 3 | Fill | `getByLabel('Password')` → `patientPassword` | Value set |
| 4 | Click | `getByRole('button', { name: 'Sign In' })` | Login submitted |
| 5 | Assert URL | Current URL | Contains `/patient/dashboard` |
| 6 | Assert | JWT in localStorage | Token present and parseable |
| 7 | Save | `context.storageState({ path: 'patient-state.json' })` | Auth state persisted |

- **Checkpoint**: `GET /auth/me` with JWT → `role: "patient"`, `id` captured
- **Shared Data**: `patientId`, `jwt: {token}`, auth storageState

---

### Step 3: UC-004 — Book Available Appointment
- **Entry Point**: `http://localhost:4200/patient/appointments/book`
- **Preconditions**: Patient authenticated from Step 2; at least one slot `isAvailable: true` in next 14 days

#### Actions
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Navigate | `http://localhost:4200/patient/appointments/book` | Booking calendar rendered |
| 2 | Click | First available date cell | Time slots shown |
| 3 | Click | First available time slot button | Slot selected; summary shown |
| 4 | Assert | Summary panel | Date, time, location visible |
| 5 | Click | `getByRole('button', { name: 'Confirm Booking' })` | Booking submitted |
| 6 | Assert | Confirmation message | Appointment confirmed |
| 7 | Assert URL | Current URL | Appointments page or confirmation |
| 8 | Assert API | `GET /appointments?patientId={id}` | Appointment with `status: "Scheduled"` |
| 9 | Poll SMTP stub | Confirmation email | Email with PDF attachment received ≤ 60 s |

- **Checkpoint**: `appointmentId` captured from response; `Slot.isAvailable: false` confirmed
- **Shared Data**: `appointmentId`, `slotId`

---

### Step 4: UC-009 — Submit Manual Intake Form
- **Entry Point**: `http://localhost:4200/patient/intake/{appointmentId}`
- **Preconditions**: Patient authenticated; `appointmentId` from Step 3; no prior intake for this appointment

#### Actions
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Navigate | `http://localhost:4200/patient/intake/{appointmentId}` | Intake method selection |
| 2 | Click | `getByRole('button', { name: /manual/i })` | Manual intake form opens |
| 3 | Fill | `getByLabel('Chief Complaint')` → `"Persistent cough for 2 weeks"` | Value set |
| 4 | Fill | `getByLabel('Medical History')` → `"Asthma diagnosed 2015"` | Value set |
| 5 | Fill | `getByLabel('Current Medications')` → `"Albuterol inhaler PRN"` | Value set |
| 6 | Fill | `getByLabel('Allergies')` → `"Penicillin — rash"` | Value set |
| 7 | Fill | Insurance fields | Provider + ID entered |
| 8 | Click | `getByRole('button', { name: /submit/i })` | Form submitted |
| 9 | Assert | Success message | "Intake submitted" |
| 10 | Assert API | `GET /intake/{appointmentId}` | `source: "Manual"`, `version: 1`, all fields non-null |

- **Checkpoint**: `IntakeRecord` exists with `version: 1` and correct `appointmentId`
- **Shared Data**: `intakeId`

## Cross-Journey Test Data
```yaml
e2e_001:
  patient:
    email: "e2e-newpatient@test.dev"
    password: "E2ePass@1"
    firstName: "E2E"
    lastName: "Patient"
    dob: "1988-04-22"
    phone: "555-E2E1"
  intake:
    chief_complaint: "Persistent cough for 2 weeks"
    medical_history: "Asthma diagnosed 2015"
    current_meds: "Albuterol inhaler PRN"
    allergies: "Penicillin — rash"
    insurance_provider: "BlueCross"
    insurance_id: "BC123456"
  confirmation_sla_ms: 60000
```

## Validation Commands
| Check | Command | Expected |
|-------|---------|----------|
| E2E-001 full run | `npx playwright test tests/e2e/journeys/e2e-001-patient-new-visit.spec.ts` | Exit 0 |
| With video | `npx playwright test tests/e2e/journeys/e2e-001-patient-new-visit.spec.ts --video on` | Exit 0 |
| CI headless | `npx playwright test tests/e2e/journeys/e2e-001-patient-new-visit.spec.ts --project=chromium` | Exit 0 |

---

# E2E Journey - Waitlist Slot Swap

## Journey Definition
| Field | Value |
|-------|-------|
| Journey ID | E2E-002 |
| UC Chain | UC-004 → UC-005 → UC-006 |
| Business Value | Validates the dynamic slot swap pipeline — the core scheduling differentiator that reduces missed appointments by automatically reallocating freed slots to waitlisted patients |
| Session Requirements | Two patient accounts; Staff account for slot manipulation; Hangfire `SwapMonitorJob` running; SMTP + SMS stubs active; `appointmentId` and `waitlistEntryId` passed across steps |
| Priority | P0 |
| Source | [SOURCE:INPUT] — Basis: BRD §4 "Dynamic Preferred Slot Swap"; FR-009 automated slot swap; UC-005–UC-006 chain |

## Journey Steps

### Step 1: UC-004 — Patient A Books Available Slot
- **Entry Point**: `http://localhost:4200/patient/appointments/book`
- **Preconditions**: Patient A (`patient@clinicalhub.dev`) authenticated; at least 2 available slots

#### Actions
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as Patient A | Patient A credentials | JWT obtained |
| 2 | Book slot X | `POST /appointments { slotId: slotX }` via API | HTTP 201; `appointmentId_A` captured |
| 3 | Assert | `Slot X.isAvailable` | `false` |

- **Checkpoint**: Patient A has `status: "Scheduled"` appointment at slot X
- **Shared Data**: `appointmentId_A`, `slotX_id`

---

### Step 2: UC-005 — Patient B Books Alternative + Joins Waitlist for Slot X
- **Entry Point**: `http://localhost:4200/patient/appointments/book`
- **Preconditions**: Patient B (`vasanthabalan.murugesan@kanini.com`) authenticated; slot Y (not X) available

#### Actions
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as Patient B | Patient B credentials | JWT obtained |
| 2 | Book slot Y | `POST /appointments { slotId: slotY }` | HTTP 201; `appointmentId_B` captured |
| 3 | Navigate | Appointments page → waitlist option | Waitlist UI |
| 4 | Select | Preferred slot X (unavailable) | Slot X selected |
| 5 | Click | `getByRole('button', { name: /join waitlist/i })` | Waitlist submitted |
| 6 | Assert | Confirmation message | "On waitlist for [slot X time]" |
| 7 | Assert API | `GET /waitlist?patientId={B_id}` | Entry with `status: "Active"`, `preferredSlotId: slotX_id` |

- **Checkpoint**: `WaitlistEntry.status = "Active"` for Patient B targeting slot X
- **Shared Data**: `waitlistEntryId`, `appointmentId_B`, `slotY_id`

---

### Step 3: UC-006 — Slot X Released; Swap Notification Triggered
- **Entry Point**: Staff action or Patient A cancellation releasing slot X
- **Preconditions**: Patient A's appointment at slot X cancellable; SMTP + SMS stubs active

#### Actions
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Patient A cancels | `DELETE /appointments/{appointmentId_A}` | HTTP 200; slot X released |
| 2 | Assert | `Slot X.isAvailable` | `true` |
| 3 | Trigger | `SwapMonitorJob.ExecuteAsync` | Job executes |
| 4 | Assert SMTP mock | Emails sent | Patient B's email in recipients with swap offer |
| 5 | Assert SMS mock | SMS sent | Patient B's phone in recipients |
| 6 | Assert API | `GET /waitlist/{waitlistEntryId}` | `status: "Offered"` |

- **Checkpoint**: Patient B's `WaitlistEntry.status = "Offered"`
- **Shared Data**: Swap offer notification content

---

### Step 4: UC-006 — Patient B Accepts Swap
- **Entry Point**: Notification link or `http://localhost:4200/patient/appointments`
- **Preconditions**: `WaitlistEntry.status = "Offered"`; Patient B authenticated; within 2-hour window

#### Actions
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as Patient B | Patient B credentials | JWT |
| 2 | Navigate | Appointments page or notification link | Swap offer visible |
| 3 | Click | `getByRole('button', { name: /accept/i })` | Acceptance submitted |
| 4 | Assert | Appointment summary | Shows slot X time |
| 5 | Assert API | `GET /appointments/{appointmentId_B}` | `slotId: slotX_id` |
| 6 | Assert API | `GET /slots/{slotY_id}` | `isAvailable: true` (old slot released) |
| 7 | Assert API | `GET /waitlist/{waitlistEntryId}` | `status: "Accepted"` |
| 8 | Assert SMTP mock | New confirmation email | PDF attachment with updated slot X |

- **Checkpoint**: Patient B's appointment at slot X confirmed; slot Y released
- **Shared Data**: Final appointment state

## Cross-Journey Test Data
```yaml
e2e_002:
  patient_a:
    email: "patient@clinicalhub.dev"
    password: "Patient@1234"
    id: 3
  patient_b:
    email: "vasanthabalan.murugesan@kanini.com"
    id: 15
  slot_x: "{unavailable_after_step_1}"
  slot_y: "{alternative_available_slot}"
  swap_window_hours: 2
  expected_final_slot_for_b: "slot_x"
```

## Validation Commands
| Check | Command | Expected |
|-------|---------|----------|
| E2E-002 full run | `npx playwright test tests/e2e/journeys/e2e-002-waitlist-swap.spec.ts` | Exit 0 |
| CI headless | `npx playwright test tests/e2e/journeys/e2e-002-waitlist-swap.spec.ts --project=chromium` | Exit 0 |

---

# E2E Journey - Staff Clinical Intelligence

## Journey Definition
| Field | Value |
|-------|-------|
| Journey ID | E2E-003 |
| UC Chain | UC-002 → UC-017 → UC-020 → UC-021 → UC-022 |
| Business Value | Validates the end-to-end clinical intelligence pipeline — Staff logs in, patient uploads a clinical document, Staff reviews the 360° view, generates ICD-10 and CPT codes, and verifies them — reducing clinical prep from 20 minutes to 2 minutes |
| Session Requirements | Staff JWT persisted; `patientId=19` (vasant veera) pre-seeded with `verificationStatus=1`; document uploaded before code generation; Hangfire jobs complete between steps |
| Priority | P0 |
| Source | [SOURCE:INPUT] — Basis: BRD §3 "20-minute search task into a 2-minute verification action"; UC-017–UC-022 chain; FR-026, FR-032, FR-033, FR-034, FR-035 |

## Journey Steps

### Step 1: UC-002 — Staff Login
- **Entry Point**: `http://localhost:4200/login`
- **Preconditions**: Staff account `staff@clinicalhub.dev` active

#### Actions
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Navigate | `http://localhost:4200/login` | Login form |
| 2 | Fill | `getByLabel('Email')` → `"staff@clinicalhub.dev"` | Value set |
| 3 | Fill | `getByLabel('Password')` → `"Staff@1234"` | Value set |
| 4 | Click | `getByRole('button', { name: 'Sign In' })` | Submitted |
| 5 | Assert URL | Current URL | Contains `/staff/` |
| 6 | Save storageState | `staff-state.json` | Persisted for subsequent steps |

- **Checkpoint**: Staff JWT valid; `role: "staff"` confirmed
- **Shared Data**: `staffJwt`, `staffId: 2`

---

### Step 2: UC-017 — Patient Uploads Clinical Document (via Patient Session)
- **Entry Point**: `http://localhost:4200/patient/documents`
- **Preconditions**: Patient `patientId=19` authenticated; `clinical-report.pdf` test fixture available

#### Actions
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as patient 19 | Patient 19 credentials | Patient JWT |
| 2 | Navigate | `http://localhost:4200/patient/documents` | Documents page |
| 3 | Click | `getByRole('button', { name: /upload/i })` | Upload modal opens |
| 4 | Set file | `locator('input[type="file"]')` → `clinical-report.pdf` | File selected |
| 5 | Click | Upload confirm button | Upload submitted |
| 6 | Assert | Document list | New document entry with `status: "Pass"` |
| 7 | Assert API | `GET /documents?patientId=19` | `virusScanResult: "Pass"`, `extractionStatus: "Pending"` |
| 8 | Trigger OCR | `OcrExtractionJob` or poll until `extractionStatus: "Extracted"` | Max 30 s |
| 9 | Assert API | `GET /documents/{id}` | `extractionStatus: "Extracted"` |

- **Checkpoint**: `documentId` captured; `ExtractedClinicalFields` rows present for patient 19
- **Shared Data**: `documentId`, `patientId: 19`

---

### Step 3: UC-020 — Staff Reviews 360° Patient View
- **Entry Point**: `http://localhost:4200/staff/patients/19/view360`
- **Preconditions**: Staff authenticated (Step 1 storageState); patient 19 has extracted fields; conflicts resolved

#### Actions
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Restore staff session | From `staff-state.json` | Staff authenticated |
| 2 | Navigate | `http://localhost:4200/staff/patients/19/view360` | 360° view loads |
| 3 | Assert | Page heading | "360° Patient View" visible |
| 4 | Assert | All sections | Vitals, History, Medications, Allergies populated |
| 5 | Assert | Verification status | Not "Verified" (ready to verify) |
| 6 | Check for conflicts | Conflict flags section | No unresolved conflicts |
| 7 | Click | `getByRole('button', { name: /verify/i })` | Verification submitted (if applicable) |
| 8 | Assert | Status badge | `"Verified"` |

- **Checkpoint**: `verificationStatus: "Verified"` (or already 1 in DB); `GET /patients/19/view360` returns complete view
- **Shared Data**: `verified: true`

---

### Step 4: UC-021 — Generate ICD-10 and CPT Codes
- **Entry Point**: `http://localhost:4200/staff/patients/19/view360`
- **Preconditions**: Patient 19 verified; Staff authenticated; `FallbackCodeGenerationService` active

#### Actions
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Assert | "Generate codes" button | Enabled (patient verified) |
| 2 | Click | `getByRole('button', { name: /generate codes/i })` | Code generation triggered |
| 3 | Assert | Feedback | "Codes being generated" or loading state |
| 4 | Poll API | `GET /coding?patientId=19` until results | Max 30 s |
| 5 | Navigate | `http://localhost:4200/staff/coding?patientId=19` | Coding verification page |
| 6 | Assert | ICD-10 suggestions | At least 1 pending suggestion visible |
| 7 | Assert | CPT suggestions | At least 1 pending suggestion visible |
| 8 | Assert | Each suggestion | Has `code`, `description`, `confidenceScore`, `status: "Pending"` |

- **Checkpoint**: Both ICD-10 and CPT `MedicalCodeSuggestion` rows present with `status: "Pending"`
- **Shared Data**: `suggestionIds: [...]`

---

### Step 5: UC-022 — Staff Verifies Medical Codes
- **Entry Point**: `http://localhost:4200/staff/coding?patientId=19`
- **Preconditions**: Code suggestions in `status: "Pending"` from Step 4

#### Actions
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Accept first ICD-10 | `getByRole('button', { name: /accept/i })` first row | Row → `"Accepted"` |
| 2 | Modify second ICD-10 | Edit code → `"J06.9"` → save | Row → `"Modified"` with `committedCode: "J06.9"` |
| 3 | Reject CPT suggestion | `getByRole('button', { name: /reject/i })` | Row → `"Rejected"` |
| 4 | Assert | All suggestions actioned | No `"Pending"` rows remaining |
| 5 | Assert API | `GET /coding?patientId=19` | Mix of `Accepted`, `Modified`, `Rejected` statuses |
| 6 | Assert | All accepted/modified rows | `verifiedById: 2`, `verifiedAt` set |
| 7 | Assert | Trust-First constraint | No row has `verifiedById: null` in `Accepted`/`Modified` state |

- **Checkpoint**: All suggestions actioned; `verifiedById` non-null for all committed codes
- **Shared Data**: Final code verification state

## Cross-Journey Test Data
```yaml
e2e_003:
  staff:
    email: "staff@clinicalhub.dev"
    password: "Staff@1234"
    id: 2
  patient:
    id: 19
    verification_status: 1
  test_document: "clinical-report.pdf"
  ocr_poll_max_seconds: 30
  code_gen_poll_max_seconds: 30
  expected_icd10_min: 1
  expected_cpt_min: 1
  manual_modification:
    code: "J06.9"
    description: "Acute upper respiratory infection, unspecified"
```

## Validation Commands
| Check | Command | Expected |
|-------|---------|----------|
| E2E-003 full run | `npx playwright test tests/e2e/journeys/e2e-003-staff-clinical-intelligence.spec.ts` | Exit 0 |
| With trace | `npx playwright test tests/e2e/journeys/e2e-003-staff-clinical-intelligence.spec.ts --trace on` | Exit 0 |

---

# E2E Journey - Staff Daily Operations

## Journey Definition
| Field | Value |
|-------|-------|
| Journey ID | E2E-004 |
| UC Chain | UC-002 → UC-013 → UC-014 → UC-015 → UC-016 |
| Business Value | Validates Staff's full day-of-service workflow — from login through walk-in registration, queue management, patient check-in, and no-show risk review — ensuring all operational tools work in concert |
| Session Requirements | Staff JWT persisted; patients (existing + new) available; at least one appointment with high risk score on today's schedule; same-day queue empty at start |
| Priority | P0 |
| Source | [SOURCE:INPUT] — Basis: BRD §4 "Centralized Staff Control"; FR-019–FR-022, FR-025; UC-013–UC-016 chain |

## Journey Steps

### Step 1: UC-002 — Staff Login
- **Entry Point**: `http://localhost:4200/login`
- **Preconditions**: Staff account active; date is a scheduled clinic day

#### Actions
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Navigate | `http://localhost:4200/login` | Login form |
| 2 | Fill + submit | Staff credentials | Staff dashboard loaded |
| 3 | Assert | Staff nav | Schedule, Queue, Patients links visible |
| 4 | Save storageState | `staff-ops-state.json` | Persisted |

- **Checkpoint**: `GET /auth/me` → `role: "staff"`
- **Shared Data**: `staffJwt`, `staffId: 2`

---

### Step 2: UC-013 — Register Walk-In Patient
- **Entry Point**: `http://localhost:4200/staff/queue`
- **Preconditions**: Staff authenticated; patient `Alex Rivera` (ID=3) in system; queue empty

#### Actions
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Navigate | `http://localhost:4200/staff/queue` | Queue page; empty list |
| 2 | Click | `getByRole('button', { name: /walk-in|register/i })` | Walk-in form |
| 3 | Type | `getByRole('searchbox')` → `"Alex"` | Patient suggestion shown |
| 4 | Click | Patient result `"Alex Rivera"` | Patient selected |
| 5 | Click | `getByRole('button', { name: /add to queue/i })` | Walk-in registered |
| 6 | Assert | Queue list | Alex Rivera at position 1 with walk-in flag |
| 7 | Assert | `getByText(/estimated wait/i)` | Wait time shown |

- **Checkpoint**: Queue has 1 entry; `GET /staff/queue` returns entry with `walkInFlag: true`
- **Shared Data**: `queueEntryId_alex`

---

### Step 3: UC-014 — Manage Same-Day Queue
- **Entry Point**: `http://localhost:4200/staff/queue`
- **Preconditions**: Staff authenticated; queue has ≥ 2 entries (add second walk-in for this step)

#### Actions
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Add second walk-in | New patient `"Queue Test"` | 2 entries in queue |
| 2 | Assert | Queue rows | 2 rows; positions 1 and 2; estimated wait shown |
| 3 | Reorder | Drag entry at position 2 to position 1 | Queue reordered |
| 4 | Assert | Queue rows | Positions updated; "Queue Test" now at position 1 |
| 5 | Assert | Estimated wait recalculated | Position 2 wait ≠ position 1 wait |
| 6 | Remove | `getByRole('button', { name: /remove/i })` on entry 2 | Entry removed |
| 7 | Assert | Queue rows | 1 entry remaining |

- **Checkpoint**: Queue reflects new order; removed entry absent from `GET /staff/queue`
- **Shared Data**: `queueEntryId_alex` (still in queue at position 1)

---

### Step 4: UC-015 — Check In Patient Arrival
- **Entry Point**: `http://localhost:4200/staff/schedule`
- **Preconditions**: Staff authenticated; appointment for today with `status: "Scheduled"` in daily schedule

#### Actions
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Navigate | `http://localhost:4200/staff/schedule` | Daily schedule |
| 2 | Assert | Schedule rows | Appointment(s) for today visible with `"Scheduled"` status |
| 3 | Locate patient row | `getByRole('row')` containing today's appointment | Row found |
| 4 | Click | `getByRole('button', { name: /check in/i })` | Check-in submitted |
| 5 | Assert | Row status badge | `"Arrived"` |
| 6 | Assert API | `GET /appointments/{id}` | `status: "Arrived"`, `arrivedAt` non-null |
| 7 | Assert API | AuditLog | Entry with `actorId: 2`, `actionType: "UPDATE"` |

- **Checkpoint**: Appointment `status: "Arrived"`; audit entry written
- **Shared Data**: `arrivedAppointmentId`

---

### Step 5: UC-016 — Review No-Show Risk Alerts
- **Entry Point**: `http://localhost:4200/staff/schedule`
- **Preconditions**: Staff authenticated; at least one appointment with `noShowRiskScore >= highRiskThreshold` on today's schedule

#### Actions
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Navigate | `http://localhost:4200/staff/schedule` | Daily schedule |
| 2 | Assert | High-risk row | Risk flag indicator (icon/badge) visible |
| 3 | Click | Risk flag or row detail | Risk details shown |
| 4 | Record outreach note | `getByLabel(/note|outreach/i)` → `"Called patient, confirmed attendance"` | Note entered |
| 5 | Click | `getByRole('button', { name: /save/i })` | Note saved |
| 6 | Assert | Row | Note saved confirmation |
| 7 | Assert API | `GET /appointments/{high_risk_id}` | Outreach note present |

- **Checkpoint**: Risk review logged; no-show history available for future risk scoring

## Cross-Journey Test Data
```yaml
e2e_004:
  staff:
    email: "staff@clinicalhub.dev"
    password: "Staff@1234"
    id: 2
  existing_patient:
    name: "Alex Rivera"
    id: 3
  new_walkin:
    name: "Queue Test"
    dob: "1980-01-15"
    phone: "555-0001"
  high_risk_threshold: 7
  today_date: "{dynamic: UtcNow.Date}"
  outreach_note: "Called patient, confirmed attendance"
```

## Validation Commands
| Check | Command | Expected |
|-------|---------|----------|
| E2E-004 full run | `npx playwright test tests/e2e/journeys/e2e-004-staff-daily-ops.spec.ts` | Exit 0 |
| CI headless | `npx playwright test tests/e2e/journeys/e2e-004-staff-daily-ops.spec.ts --project=chromium` | Exit 0 |

---

# E2E Journey - Admin User Lifecycle

## Journey Definition
| Field | Value |
|-------|-------|
| Journey ID | E2E-005 |
| UC Chain | UC-002 → UC-003 → UC-023 |
| Business Value | Validates the Admin governance workflow — Admin creates a new Staff account, deactivates it, and verifies compliance via the immutable audit log — ensuring platform access control and HIPAA audit readiness |
| Session Requirements | Admin JWT persisted; initial Staff account count ≥ 2 (to allow deactivation without triggering last-admin guard) |
| Priority | P1 |
| Source | [SOURCE:INPUT] — Basis: BRD §6 "Admin (user management)"; FR-002, FR-005; UC-003, UC-023 chain |

## Journey Steps

### Step 1: UC-002 — Admin Login
- **Entry Point**: `http://localhost:4200/login`
- **Preconditions**: Admin account `admin@clinicalhub.dev` active

#### Actions
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Navigate | `http://localhost:4200/login` | Login form |
| 2 | Fill + submit | Admin credentials | Admin dashboard |
| 3 | Assert | Admin nav | "User Management" and "Audit Log" visible |
| 4 | Save storageState | `admin-state.json` | Persisted |

- **Checkpoint**: `GET /auth/me` → `role: "admin"`
- **Shared Data**: `adminJwt`, `adminId: 1`

---

### Step 2: UC-003 — Create New Staff Account
- **Entry Point**: `http://localhost:4200/admin/users`
- **Preconditions**: Admin authenticated; `e2e-newstaff@test.dev` not registered

#### Actions
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Navigate | `http://localhost:4200/admin/users` | User list |
| 2 | Click | `getByRole('button', { name: 'Create User' })` | Create user form |
| 3 | Fill | `getByLabel('Email')` → `"e2e-newstaff@test.dev"` | Value set |
| 4 | Fill | `getByLabel('First Name')` → `"E2E"` | Value set |
| 5 | Fill | `getByLabel('Last Name')` → `"Staff"` | Value set |
| 6 | Select | `getByRole('combobox', { name: 'Role' })` → `"Staff"` | Role set |
| 7 | Click | `getByRole('button', { name: 'Create' })` | Submitted |
| 8 | Assert | User list | `"E2E Staff"` entry visible with `Inactive` status |
| 9 | Assert API | `GET /admin/users?email=e2e-newstaff@test.dev` | User present |
| 10 | Capture | New user ID from response | `newStaffId` stored |

- **Checkpoint**: New Staff account created with `isActive: false`; credential-setup email enqueued
- **Shared Data**: `newStaffId`, `newStaffEmail: "e2e-newstaff@test.dev"`

---

### Step 3: UC-003 — Deactivate Staff Account
- **Entry Point**: `http://localhost:4200/admin/users`
- **Preconditions**: Admin authenticated; new Staff account from Step 2 exists

#### Actions
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Locate new staff row | `getByRole('row')` containing `"E2E Staff"` | Row found |
| 2 | Click | `getByRole('button', { name: /deactivate/i })` within row | Confirm dialog |
| 3 | Confirm | `getByRole('button', { name: /confirm/i })` | Deactivation applied |
| 4 | Assert | Row status badge | `"Inactive"` |
| 5 | Assert API | `GET /admin/users/{newStaffId}` | `isActive: false` |
| 6 | Attempt login | With `e2e-newstaff@test.dev` credentials | HTTP 401 or error message |

- **Checkpoint**: Inactive account login blocked; `isActive: false` confirmed
- **Shared Data**: `deactivatedAt`

---

### Step 4: UC-023 — Admin Reviews Audit Log for Lifecycle Events
- **Entry Point**: `http://localhost:4200/admin/audit`
- **Preconditions**: Admin authenticated; audit log has entries from Steps 2–3

#### Actions
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Navigate | `http://localhost:4200/admin/audit` | Audit log page |
| 2 | Filter by actor | `getByLabel(/actor/i)` → `adminId` | Filter applied |
| 3 | Filter by entity type | `UserAccount` | Filtered |
| 4 | Assert | Log entries | CREATE entry for `e2e-newstaff@test.dev` visible |
| 5 | Assert | Log entries | UPDATE/DEACTIVATE entry for same account visible |
| 6 | Assert | All entries | Immutable — no Edit/Delete buttons in UI |
| 7 | Attempt modification | `DELETE /admin/audit/{id}` via API | HTTP 405 |
| 8 | Assert | New audit entry | Modification attempt itself logged |

- **Checkpoint**: Full lifecycle traceable in audit log; modification attempt blocked at API layer

## Cross-Journey Test Data
```yaml
e2e_005:
  admin:
    email: "admin@clinicalhub.dev"
    password: "Admin@1234"
    id: 1
  new_staff:
    email: "e2e-newstaff@test.dev"
    firstName: "E2E"
    lastName: "Staff"
    role: "staff"
  expected_audit_actions: ["CREATE", "UPDATE"]
  audit_modification_expected_status: 405
```

## Validation Commands
| Check | Command | Expected |
|-------|---------|----------|
| E2E-005 full run | `npx playwright test tests/e2e/journeys/e2e-005-admin-lifecycle.spec.ts` | Exit 0 |
| CI headless | `npx playwright test tests/e2e/journeys/e2e-005-admin-lifecycle.spec.ts --project=chromium` | Exit 0 |
| All E2E journeys | `npx playwright test tests/e2e/journeys/ --project=chromium` | Exit 0 |
