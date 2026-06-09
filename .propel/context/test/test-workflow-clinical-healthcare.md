# Test Workflow - Unified Patient Access & Clinical Intelligence Platform

## Test Configuration
| Setting | Value |
|---------|-------|
| Framework | Playwright |
| Framework Version | 1.44.x |
| Target Application | Angular 18 SPA — `http://localhost:4200` + .NET 8 API — `http://localhost:5153` |
| Source Requirements | UC-001–UC-024, FR-001–FR-035, NFR-004, NFR-007, NFR-008, AIR-001, AIR-005, DR-003 |

## Requirements Coverage
| Requirement ID | Type | Test Cases | Priority |
|----------------|------|------------|----------|
| UC-001, FR-001 | Functional | FT-001, FT-002, FT-003, FT-004 | P0 |
| UC-002, FR-003, FR-004, FR-006 | Functional / Security | FT-005, FT-006, FT-007, FT-008, FT-009 | P0 |
| UC-003, FR-002 | Functional | FT-010, FT-011, FT-012 | P0 |
| UC-004, FR-007, FR-010 | Functional | FT-013, FT-014, FT-015 | P0 |
| UC-005, FR-008, DR-003 | Functional / Data | FT-016, FT-017 | P1 |
| UC-006, FR-009 | Functional | FT-018, FT-019, FT-020 | P0 |
| UC-007, FR-011 | Functional | FT-021, FT-022, FT-023 | P0 |
| UC-008, FR-015, AIR-001 | Functional / AI | FT-024, FT-025 | P1 |
| UC-009, FR-016, FR-017 | Functional | FT-026, FT-027, FT-028 | P0 |
| UC-010, FR-018 | Functional | FT-029, FT-030 | P1 |
| UC-011, FR-014 | Functional / Performance | FT-031 | P0 |
| UC-013, FR-019 | Functional | FT-032, FT-033 | P0 |
| UC-014, FR-020 | Functional | FT-034, FT-035 | P0 |
| UC-015, FR-021 | Functional / Security | FT-036, FT-037 | P0 |
| UC-016, FR-025 | Functional | FT-038 | P1 |
| UC-017, FR-026, FR-027 | Functional / Security | FT-039, FT-040, FT-041, FT-042 | P0 |
| UC-018, FR-028 | Functional / AI | FT-043 | P1 |
| UC-019, FR-031 | Functional | FT-044, FT-045 | P0 |
| UC-020, FR-032 | Functional | FT-046 | P0 |
| UC-021, FR-033, AIR-005 | Functional / AI / Security | FT-047, FT-048, FT-049 | P0 |
| UC-022, FR-035 | Functional | FT-050, FT-051, FT-052 | P0 |
| UC-023, FR-005, NFR-008 | Functional / Security | FT-053, FT-054 | P0 |
| UC-024, FR-004, NFR-007 | Security | FT-055 | P0 |

---

## Test Scenarios

### FT-001 — Patient registers with valid data
- **Type**: happy_path
- **Priority**: P0
- **Requirement**: UC-001, FR-001
- **Preconditions**: No account exists for `newpatient@test.dev`; SMTP stub active; app running
- **Source**: [SOURCE:INPUT] — Basis: UC-001 main success scenario; FR-001 self-registration requirement

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Navigate | `http://localhost:4200/register` | Registration form rendered |
| 2 | Fill | `getByLabel('First Name')` → `"Test"` | Field value set |
| 3 | Fill | `getByLabel('Last Name')` → `"Patient"` | Field value set |
| 4 | Fill | `getByLabel('Date of Birth')` → `"1990-06-15"` | Field value set |
| 5 | Fill | `getByLabel('Phone')` → `"555-0101"` | Field value set |
| 6 | Fill | `getByLabel('Email')` → `"newpatient@test.dev"` | Field value set |
| 7 | Fill | `getByLabel('Password')` → `"TestPass@123"` | Field value set |
| 8 | Click | `getByRole('button', { name: 'Create Account' })` | Form submitted |
| 9 | Assert | `getByRole('alert')` or success banner | Message contains "Verify your email" |
| 10 | Assert API | `GET /auth/me` after email click | `emailVerified: false` initially |

#### Test Data
```yaml
valid_registration:
  firstName: "Test"
  lastName: "Patient"
  dob: "1990-06-15"
  phone: "555-0101"
  email: "newpatient@test.dev"
  password: "TestPass@123"
```

---

### FT-002 — Registration with duplicate email → 409
- **Type**: error
- **Priority**: P0
- **Requirement**: UC-001, FR-001
- **Preconditions**: Account `patient@clinicalhub.dev` already exists
- **Source**: [SOURCE:INPUT] — Basis: UC-001 extension 5b; duplicate email must be rejected

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Navigate | `http://localhost:4200/register` | Registration form rendered |
| 2 | Fill | `getByLabel('Email')` → `"patient@clinicalhub.dev"` | Field value set |
| 3 | Fill all other required fields | Form | Valid data entered |
| 4 | Click | `getByRole('button', { name: 'Create Account' })` | Form submitted |
| 5 | Assert | `getByRole('alert')` | Error visible — email already registered |
| 6 | Assert API | Response status | HTTP 409 |

#### Test Data
```yaml
duplicate_email: "patient@clinicalhub.dev"
other_fields:
  firstName: "Another"
  lastName: "Person"
  dob: "1985-03-10"
  password: "TestPass@123"
```

---

### FT-003 — Email verification link activates account
- **Type**: happy_path
- **Priority**: P0
- **Requirement**: UC-001, FR-001
- **Preconditions**: Unverified account exists; valid single-use token issued
- **Source**: [SOURCE:INPUT] — Basis: UC-001 step 5; FR-001 requires email verification

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Navigate | `http://localhost:4200/auth/verify-email?token={validToken}` | Page loads |
| 2 | Assert | `getByRole('heading')` or success message | "Email verified" confirmation shown |
| 3 | Assert API | `GET /patients/{id}` | `emailVerified: true` |
| 4 | Navigate | `http://localhost:4200/auth/verify-email?token={validToken}` (repeat) | Second use rejected |
| 5 | Assert | Response or page | Error message — token already used |

#### Test Data
```yaml
valid_token: "{generated_from_seed_or_test_setup}"
reuse_expected_status: 400
```

---

### FT-004 — Expired verification token rejected
- **Type**: error
- **Priority**: P1
- **Requirement**: UC-001, FR-001
- **Preconditions**: Token with `CreatedAt` > 24 hours ago
- **Source**: [SOURCE:INPUT] — Basis: UC-001 extension 5a; tokens expire at 24 hours

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Navigate | `http://localhost:4200/auth/verify-email?token={expiredToken}` | Page loads |
| 2 | Assert | Error or alert element | Message indicates token expired |
| 3 | Assert API | Response status | HTTP 400 or 410 |
| 4 | Assert DB | `emailVerified` | Remains `false` |

#### Test Data
```yaml
expired_token: "{seeded_token_created_25_hours_ago}"
expected_status: 400
```

---

### FT-005 — Staff login — happy path
- **Type**: happy_path
- **Priority**: P0
- **Requirement**: UC-002, FR-003
- **Preconditions**: Staff account `staff@clinicalhub.dev` / `Staff@1234` exists and active
- **Source**: [SOURCE:INPUT] — Basis: UC-002 main success scenario; Staff role required for operational features

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Navigate | `http://localhost:4200/login` | Login form rendered |
| 2 | Fill | `getByLabel('Email')` → `"staff@clinicalhub.dev"` | Field value set |
| 3 | Fill | `getByLabel('Password')` → `"Staff@1234"` | Field value set |
| 4 | Click | `getByRole('button', { name: 'Sign In' })` | Form submitted |
| 5 | Assert URL | Current URL | Contains `/staff/` |
| 6 | Assert | `getByRole('navigation')` | Staff nav items visible (Schedule, Queue, Patients) |
| 7 | Assert localStorage/cookie | JWT token | Present and parseable |

#### Test Data
```yaml
staff_credentials:
  email: "staff@clinicalhub.dev"
  password: "Staff@1234"
expected_redirect: "/staff/dashboard"
```

---

### FT-006 — Patient login — happy path
- **Type**: happy_path
- **Priority**: P0
- **Requirement**: UC-002, FR-003
- **Preconditions**: Patient account `patient@clinicalhub.dev` / `Patient@1234` exists
- **Source**: [SOURCE:INPUT] — Basis: UC-002 main success scenario; Patient role

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Navigate | `http://localhost:4200/login` | Login form rendered |
| 2 | Fill | `getByLabel('Email')` → `"patient@clinicalhub.dev"` | Field value set |
| 3 | Fill | `getByLabel('Password')` → `"Patient@1234"` | Field value set |
| 4 | Click | `getByRole('button', { name: 'Sign In' })` | Form submitted |
| 5 | Assert URL | Current URL | Contains `/patient/` |
| 6 | Assert | `getByRole('heading', { name: /dashboard/i })` | Patient dashboard visible |

#### Test Data
```yaml
patient_credentials:
  email: "patient@clinicalhub.dev"
  password: "Patient@1234"
expected_redirect: "/patient/dashboard"
```

---

### FT-007 — Invalid credentials → generic error, no role disclosure
- **Type**: error
- **Priority**: P0
- **Requirement**: UC-002, FR-003
- **Preconditions**: App running
- **Source**: [SOURCE:INPUT] — Basis: UC-002 extension 2a; OWASP A07 — no account enumeration

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Navigate | `http://localhost:4200/login` | Login form rendered |
| 2 | Fill | `getByLabel('Email')` → `"wrong@test.dev"` | Field value set |
| 3 | Fill | `getByLabel('Password')` → `"WrongPass@1"` | Field value set |
| 4 | Click | `getByRole('button', { name: 'Sign In' })` | Form submitted |
| 5 | Assert | `getByRole('alert')` | Generic error — does NOT reveal "user not found" or role |
| 6 | Assert | Error text | Does not contain "account", "does not exist", or role names |
| 7 | Assert URL | Current URL | Remains `/login` |

#### Test Data
```yaml
invalid_credentials:
  email: "notexist@test.dev"
  password: "WrongPass@1"
forbidden_error_substrings: ["not found", "no account", "staff", "admin"]
```

---

### FT-008 — Patient JWT accessing Staff route → 403
- **Type**: security
- **Priority**: P0
- **Requirement**: UC-002, FR-003, NFR-005
- **Preconditions**: Patient JWT available; Staff endpoint accessible
- **Source**: [SOURCE:INPUT] — Basis: FR-003 cross-role access must be denied; NFR-005 HTTP 403

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as patient | `/login` | Patient JWT obtained |
| 2 | Call API directly | `GET http://localhost:5153/api/staff/queue` with patient Bearer token | HTTP 403 |
| 3 | Navigate in browser | `http://localhost:4200/staff/queue` | Redirected to `/login?reason=unauthorized` or patient dashboard |
| 4 | Assert | `getByRole('alert')` or redirect | Unauthorized message or redirect |

#### Test Data
```yaml
patient_jwt: "{obtained_from_FT-006}"
forbidden_endpoint: "http://localhost:5153/api/staff/queue"
expected_status: 403
```

---

### FT-009 — Password reset via email token
- **Type**: happy_path
- **Priority**: P1
- **Requirement**: UC-002, FR-006
- **Preconditions**: Active patient account; SMTP stub active
- **Source**: [SOURCE:INPUT] — Basis: FR-006 password reset flow; token TTL 60 min, single-use

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Navigate | `http://localhost:4200/forgot-password` | Forgot password form rendered |
| 2 | Fill | `getByLabel('Email')` → `"patient@clinicalhub.dev"` | Field value set |
| 3 | Click | `getByRole('button', { name: /send/i })` | Request submitted |
| 4 | Assert | Success message | Indicates reset email sent |
| 5 | Retrieve token | SMTP stub / API endpoint | Reset token obtained |
| 6 | Navigate | `http://localhost:4200/reset-password?token={token}` | Reset form rendered |
| 7 | Fill | `getByLabel('New Password')` → `"NewPass@456"` | Field value set |
| 8 | Fill | `getByLabel('Confirm Password')` → `"NewPass@456"` | Field value set |
| 9 | Click | `getByRole('button', { name: /reset/i })` | Form submitted |
| 10 | Assert | Redirect or success | Redirected to login with success message |
| 11 | Login | With new password `"NewPass@456"` | Login succeeds |

#### Test Data
```yaml
reset_email: "patient@clinicalhub.dev"
new_password: "NewPass@456"
token_ttl_minutes: 60
```

---

### FT-010 — Admin creates Staff account
- **Type**: happy_path
- **Priority**: P1
- **Requirement**: UC-003, FR-002
- **Preconditions**: Admin JWT; target email not registered
- **Source**: [SOURCE:INPUT] — Basis: UC-003 main success scenario; FR-002 Admin manages Staff accounts

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as admin | `admin@clinicalhub.dev` / `Admin@1234` | Admin JWT obtained |
| 2 | Navigate | `http://localhost:4200/admin/users` | User management page |
| 3 | Click | `getByRole('button', { name: 'Create User' })` | Create user form/modal |
| 4 | Fill | `getByLabel('Email')` → `"newstaff@clinicalhub.dev"` | Field value set |
| 5 | Fill | `getByLabel('First Name')` → `"New"` | Field value set |
| 6 | Fill | `getByLabel('Last Name')` → `"Staff"` | Field value set |
| 7 | Select | `getByRole('combobox', { name: 'Role' })` → `"Staff"` | Role selected |
| 8 | Click | `getByRole('button', { name: 'Create' })` | Form submitted |
| 9 | Assert | User list | New staff entry visible with `Inactive` status |
| 10 | Assert API | `GET /admin/users` | New user present |

#### Test Data
```yaml
admin_credentials:
  email: "admin@clinicalhub.dev"
  password: "Admin@1234"
new_staff:
  email: "newstaff@clinicalhub.dev"
  firstName: "New"
  lastName: "Staff"
  role: "staff"
```

---

### FT-011 — Admin deactivates Staff account
- **Type**: happy_path
- **Priority**: P1
- **Requirement**: UC-003, FR-002
- **Preconditions**: Admin JWT; Staff account ID=2 active; other Admin exists
- **Source**: [SOURCE:INPUT] — Basis: UC-003 step 5; FR-002 Admin can deactivate accounts

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as admin | Admin credentials | Admin JWT |
| 2 | Navigate | `http://localhost:4200/admin/users` | User list visible |
| 3 | Find Staff row | `getByRole('row')` containing `"Jordan Chen"` | Row found |
| 4 | Click | `getByRole('button', { name: 'Deactivate' })` within row | Confirmation dialog |
| 5 | Confirm | `getByRole('button', { name: 'Confirm' })` | Dialog dismissed |
| 6 | Assert | Staff row status badge | Shows `"Inactive"` |
| 7 | Assert API | `GET /admin/users/2` | `isActive: false` |

#### Test Data
```yaml
target_staff_id: 2
target_staff_name: "Jordan Chen"
```

---

### FT-012 — Cannot deactivate last Admin account
- **Type**: edge_case
- **Priority**: P0
- **Requirement**: UC-003, FR-002
- **Preconditions**: Only one active Admin account; Admin JWT
- **Source**: [SOURCE:INPUT] — Basis: UC-003 extension 5a; last-Admin guard prevents platform lockout

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as admin | Admin credentials | Admin JWT |
| 2 | Navigate | `http://localhost:4200/admin/users` | User list |
| 3 | Click Deactivate | Own Admin account row | Confirm dialog or blocked |
| 4 | Assert | Response or UI message | HTTP 409 or error "Cannot deactivate last admin" |
| 5 | Assert | Admin account status | Still `Active` |

#### Test Data
```yaml
expected_error: "Cannot deactivate the last active administrator"
expected_status: 409
```

---

### FT-013 — Patient books available slot — happy path
- **Type**: happy_path
- **Priority**: P0
- **Requirement**: UC-004, FR-007
- **Preconditions**: Patient logged in; at least one slot `isAvailable: true` exists for next 14 days
- **Source**: [SOURCE:INPUT] — Basis: UC-004 main success scenario; FR-007 slot calendar and booking

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as patient | Patient credentials | Patient dashboard |
| 2 | Navigate | `http://localhost:4200/patient/appointments/book` | Booking calendar rendered |
| 3 | Click | First available date cell | Available time slots shown |
| 4 | Click | `getByRole('button', { name: /09:00|11:00|14:00/i })` | Slot selected |
| 5 | Assert | Appointment summary panel | Date, time, location displayed |
| 6 | Click | `getByRole('button', { name: 'Confirm Booking' })` | Booking submitted |
| 7 | Assert URL | Current URL | `/patient/appointments` or confirmation page |
| 8 | Assert | Confirmation message | Contains appointment date/time |
| 9 | Assert API | `GET /appointments?patientId={id}` | New appointment with `status: "Scheduled"` |

#### Test Data
```yaml
target_slot: "next available in 14-day window"
expected_status: "Scheduled"
expected_slot_available: false
```

---

### FT-014 — Concurrent booking same slot → exactly one succeeds
- **Type**: edge_case
- **Priority**: P0
- **Requirement**: UC-004, FR-007, DR-002
- **Preconditions**: Two patient accounts; same slot ID; rowversion concurrency enabled
- **Source**: [SOURCE:INPUT] — Basis: UC-004 extension 2a; DR-002 rowversion optimistic lock prevents double-booking

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Obtain tokens | Both patient JWTs | Two valid tokens |
| 2 | POST simultaneous | `POST /appointments { slotId }` × 2 in parallel | Race condition |
| 3 | Assert | Response statuses | Exactly one HTTP 201; one HTTP 409 |
| 4 | Assert DB | `Appointments` count for slot | Exactly 1 |
| 5 | Assert | `Slot.isAvailable` | `false` |

#### Test Data
```yaml
patient_a: "patient@clinicalhub.dev"
patient_b: "vasanthabalan.murugesan@kanini.com"
shared_slot_id: "{available_slot_id}"
```

---

### FT-015 — No-show risk score stored on booking
- **Type**: happy_path
- **Priority**: P1
- **Requirement**: UC-004, FR-010
- **Preconditions**: Patient with prior no-show history; short booking lead time
- **Source**: [SOURCE:INPUT] — Basis: FR-010 risk score calculated at booking time; displayed to Staff

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Book appointment | Via API `POST /appointments` | HTTP 201 |
| 2 | Assert response | `noShowRiskScore` field | Value > 0 |
| 3 | Login as staff | Staff credentials | Staff JWT |
| 4 | Call | `GET /staff/schedule?date={today}` | Response includes appointment |
| 5 | Assert | `noShowRiskScore` in appointment record | Matches booked value |

#### Test Data
```yaml
booking_lead_hours: 2
patient_prior_noshows: 2
expected_risk_score_min: 1
```

---

### FT-016 — Patient joins waitlist for preferred slot
- **Type**: happy_path
- **Priority**: P1
- **Requirement**: UC-005, FR-008, DR-003
- **Preconditions**: Patient has confirmed appointment; preferred slot unavailable
- **Source**: [SOURCE:INPUT] — Basis: UC-005 success scenario; FR-008 waitlist registration

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as patient | Patient credentials | Patient JWT |
| 2 | Navigate | `http://localhost:4200/patient/appointments` | Appointments page |
| 3 | Find confirmed appointment | `getByRole('row', { name: /Scheduled/ })` | Row found |
| 4 | Click | `getByRole('button', { name: /waitlist|preferred/i })` | Waitlist selector shown |
| 5 | Select | Unavailable preferred slot | Slot selected |
| 6 | Click | `getByRole('button', { name: /join waitlist/i })` | Submitted |
| 7 | Assert | Confirmation message | "On waitlist for [date/time]" |
| 8 | Assert API | `GET /waitlist?patientId={id}` | Entry with `status: "Active"` |

#### Test Data
```yaml
preferred_slot: "{any unavailable slot id}"
expected_waitlist_status: "Active"
```

---

### FT-017 — Second waitlist entry replaces first (unique partial index)
- **Type**: edge_case
- **Priority**: P1
- **Requirement**: UC-005, DR-003
- **Preconditions**: Patient already has Active waitlist entry
- **Source**: [SOURCE:INPUT] — Basis: DR-003 unique partial index — one Active entry per patient; UC-005 extension 1a

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Seed | Existing `WaitlistEntry(Active)` for patient | Initial state |
| 2 | `POST /waitlist { preferredSlotId: newSlotId }` | API call | HTTP 200 or 201 |
| 3 | Assert API | `GET /waitlist?patientId={id}` | Exactly one `Active` entry |
| 4 | Assert | Prior entry | `status: "Expired"` |

#### Test Data
```yaml
old_preferred_slot_id: "{slot_a}"
new_preferred_slot_id: "{slot_b}"
max_active_entries_per_patient: 1
```

---

### FT-018 — Slot release triggers swap notification to first waitlisted patient
- **Type**: happy_path
- **Priority**: P0
- **Requirement**: UC-006, FR-009
- **Preconditions**: Patient A has Active waitlist entry for slot X; SMTP + SMS stubs active
- **Source**: [SOURCE:INPUT] — Basis: UC-006 success scenario; FR-009 automated slot swap notification

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Seed | `WaitlistEntry(Active)` for patient A targeting slot X | State ready |
| 2 | Release slot | `DELETE /appointments/{id}` (releases slot X) | HTTP 200 |
| 3 | Trigger job | `SwapMonitorJob` execution | Job runs |
| 4 | Assert SMTP mock | Email sent | Patient A's email in recipients |
| 5 | Assert SMS mock | SMS sent | Patient A's phone in recipients |
| 6 | Assert API | `GET /waitlist/{entryId}` | `status: "Offered"` |

#### Test Data
```yaml
patient_a_email: "patient@clinicalhub.dev"
swap_window_hours: 2
expected_waitlist_status: "Offered"
```

---

### FT-019 — Patient accepts swap → appointment updated
- **Type**: happy_path
- **Priority**: P0
- **Requirement**: UC-006, FR-009
- **Preconditions**: `WaitlistEntry.status = "Offered"`; patient JWT
- **Source**: [SOURCE:INPUT] — Basis: UC-006 step 4-5; FR-009 patient accepts → appointment moves to preferred slot

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as patient | Patient credentials | JWT |
| 2 | Navigate | Appointment detail or notification link | Swap offer visible |
| 3 | Click | `getByRole('button', { name: /accept/i })` | Acceptance submitted |
| 4 | Assert | `getByRole('alert')` or confirmation | "Appointment updated to [new date/time]" |
| 5 | Assert API | `GET /appointments/{id}` | `slotId` = preferred slot |
| 6 | Assert API | Old slot | `isAvailable: true` |
| 7 | Assert API | WaitlistEntry | `status: "Accepted"` |

#### Test Data
```yaml
old_slot_id: "{original_slot}"
preferred_slot_id: "{preferred_slot}"
expected_appointment_slot: "{preferred_slot_id}"
```

---

### FT-020 — Swap offer window expires → implicit decline
- **Type**: edge_case
- **Priority**: P1
- **Requirement**: UC-006, FR-009
- **Preconditions**: `WaitlistEntry.status = "Offered"` with `offeredAt > 2 h ago`
- **Source**: [SOURCE:INPUT] — Basis: UC-006 extension 4b; configurable 2-hour window default

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Seed | WaitlistEntry `status=Offered`, `offeredAt = UtcNow.AddHours(-3)` | Expired state |
| 2 | Trigger | `SwapMonitorJob.ExecuteAsync` | Job processes |
| 3 | Assert API | `GET /waitlist/{entryId}` | `status: "Expired"` |
| 4 | Assert | Slot availability | Slot still available for next patient |

#### Test Data
```yaml
offer_age_hours: 3
swap_window_hours: 2
expected_status: "Expired"
```

---

### FT-021 — Patient cancels before cutoff — slot released
- **Type**: happy_path
- **Priority**: P0
- **Requirement**: UC-007, FR-011
- **Preconditions**: Patient logged in; appointment `status: "Scheduled"` with slot > cutoff window away
- **Source**: [SOURCE:INPUT] — Basis: UC-007 cancel path; FR-011 cancellation before cutoff releases slot

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as patient | Patient credentials | Patient dashboard |
| 2 | Navigate | `http://localhost:4200/patient/appointments` | Appointments list |
| 3 | Find appointment | `getByRole('row', { name: /Scheduled/ })` | Row found |
| 4 | Click | `getByRole('button', { name: /cancel/i })` | Cancel confirmation dialog |
| 5 | Click | `getByRole('button', { name: /confirm/i })` | Cancellation confirmed |
| 6 | Assert | Appointment status | Shows `"Cancelled"` |
| 7 | Assert API | `GET /slots/{slotId}` | `isAvailable: true` |

#### Test Data
```yaml
appointment_slot_hours_ahead: 6
cancellation_cutoff_hours: 2
expected_status: "Cancelled"
expected_slot_available: true
```

---

### FT-022 — Cancel inside cutoff window → blocked
- **Type**: error
- **Priority**: P0
- **Requirement**: UC-007, FR-011
- **Preconditions**: Appointment slot within cutoff window (e.g., < 2 h away)
- **Source**: [SOURCE:INPUT] — Basis: UC-007 extension 2a; cutoff window blocks last-minute cancellations

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as patient | Patient credentials | JWT |
| 2 | Call API | `DELETE /appointments/{id}` with slot time `UtcNow.AddMinutes(30)` | HTTP 422 or 409 |
| 3 | Navigate UI | Appointments page | Cancel button disabled or shows locked state |
| 4 | Assert | Error message | References cutoff window |
| 5 | Assert API | `GET /appointments/{id}` | `status: "Scheduled"` (unchanged) |

#### Test Data
```yaml
minutes_until_appointment: 30
cutoff_hours: 2
expected_ui_state: "cancel_disabled"
expected_api_status: 422
```

---

### FT-023 — Patient reschedules appointment
- **Type**: happy_path
- **Priority**: P1
- **Requirement**: UC-007, FR-011
- **Preconditions**: Existing appointment outside cutoff; alternative slot available
- **Source**: [SOURCE:INPUT] — Basis: UC-007 reschedule path; FR-011 reschedule releases original slot

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as patient | Patient credentials | JWT |
| 2 | Navigate | Appointments page | Appointment list |
| 3 | Click | `getByRole('button', { name: /reschedule/i })` | Slot picker opens |
| 4 | Select | New available slot | New slot chosen |
| 5 | Click | `getByRole('button', { name: /confirm/i })` | Reschedule submitted |
| 6 | Assert | Appointment shows new time | Updated date/time displayed |
| 7 | Assert API | Old slot | `isAvailable: true` |
| 8 | Assert | Confirmation email | New PDF confirmation enqueued |

#### Test Data
```yaml
original_slot_hours_ahead: 8
new_slot: "{any_available_slot}"
```

---

### FT-024 — AI intake multi-turn session — fields captured and confirmed
- **Type**: happy_path
- **Priority**: P1
- **Requirement**: UC-008, FR-015
- **Preconditions**: Patient JWT; confirmed appointment; Rasa mock returns `confidence >= 0.70`
- **Source**: [SOURCE:INPUT] — Basis: UC-008 success scenario; FR-015 AI conversational intake

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as patient | Patient credentials | JWT |
| 2 | Navigate | `http://localhost:4200/patient/intake/{appointmentId}` | Intake selection page |
| 3 | Click | `getByRole('button', { name: /AI-Assisted/i })` | Chat interface opens |
| 4 | Type response | `getByRole('textbox')` → `"Headache and fever for 3 days"` | Message sent |
| 5 | Assert | AI response | Acknowledgement + next prompt shown |
| 6 | Complete turns | Medications, allergies, history prompts | All fields captured |
| 7 | Assert | Summary review panel | All 5 field groups populated |
| 8 | Click | `getByRole('button', { name: /confirm|submit/i })` | Intake confirmed |
| 9 | Assert | Success message | Intake saved |
| 10 | Assert API | `GET /intake/{appointmentId}` | `source: "AI"`, all fields non-null |

#### Test Data
```yaml
chief_complaint: "Headache and fever for 3 days"
medications: "Ibuprofen 400mg"
allergies: "Penicillin"
medical_history: "Hypertension"
demographics: "already on profile"
rasa_confidence_mock: 0.85
```

---

### FT-025 — AI intake clarification when Rasa confidence < 0.70
- **Type**: edge_case
- **Priority**: P1
- **Requirement**: UC-008, AIR-001
- **Preconditions**: Rasa mock returns `confidence: 0.55` for a turn response
- **Source**: [SOURCE:INPUT] — Basis: AIR-001 clarification threshold = 0.70; UC-008 extension 2a

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Start AI intake session | `POST /intake/ai/start` | Session created |
| 2 | Submit ambiguous response | `POST /intake/ai/turn { message: "uh maybe something" }` | API call |
| 3 | Assert API response | `requiresClarification` field | `true` |
| 4 | Assert UI | Chat message | Follow-up clarification prompt shown |
| 5 | Assert | No IntakeRecord stored yet | `GET /intake` returns no confirmed record |

#### Test Data
```yaml
ambiguous_message: "uh maybe something"
rasa_confidence_mock: 0.55
threshold: 0.70
expected_requires_clarification: true
```

---

### FT-026 — Manual intake form submission — happy path
- **Type**: happy_path
- **Priority**: P0
- **Requirement**: UC-009, FR-016
- **Preconditions**: Patient logged in; confirmed appointment; no prior intake for appointment
- **Source**: [SOURCE:INPUT] — Basis: UC-009 success scenario; FR-016 manual intake form

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as patient | Patient credentials | JWT |
| 2 | Navigate | `http://localhost:4200/patient/intake/{appointmentId}` | Intake page |
| 3 | Click | `getByRole('button', { name: /manual/i })` | Manual form opens |
| 4 | Fill | `getByLabel('Chief Complaint')` → `"Chest pain"` | Field set |
| 5 | Fill | `getByLabel('Medical History')` → `"Hypertension"` | Field set |
| 6 | Fill | `getByLabel('Current Medications')` → `"Lisinopril 10mg"` | Field set |
| 7 | Fill | `getByLabel('Allergies')` → `"None"` | Field set |
| 8 | Click | `getByRole('button', { name: /submit/i })` | Form submitted |
| 9 | Assert | Success message | "Intake submitted" |
| 10 | Assert API | `GET /intake/{appointmentId}` | `source: "Manual"`, `version: 1` |

#### Test Data
```yaml
chief_complaint: "Chest pain"
medical_history: "Hypertension"
current_meds: "Lisinopril 10mg"
allergies: "None"
```

---

### FT-027 — Manual intake missing required field → 422
- **Type**: error
- **Priority**: P0
- **Requirement**: UC-009, FR-016
- **Preconditions**: Patient JWT; confirmed appointment
- **Source**: [SOURCE:INPUT] — Basis: UC-009 extension 3a; FR-016 required fields block submission

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as patient | Patient credentials | JWT |
| 2 | Navigate | Intake form (manual) | Form visible |
| 3 | Leave | `getByLabel('Chief Complaint')` | Empty (required field) |
| 4 | Fill other fields | Medical history, medications, allergies | Valid data |
| 5 | Click | Submit button | Validation fires |
| 6 | Assert | Validation error on Chief Complaint field | Error indicator shown |
| 7 | Assert URL | Current URL | Remains on intake page |
| 8 | Assert API | `POST /intake/manual` without chiefComplaint | HTTP 422 |

#### Test Data
```yaml
missing_field: "chiefComplaint"
expected_api_status: 422
```

---

### FT-028 — Intake edit increments version and audits delta
- **Type**: happy_path
- **Priority**: P1
- **Requirement**: UC-009, FR-017, DR-004
- **Preconditions**: `IntakeRecord(version=1)` exists; before check-in cutoff
- **Source**: [SOURCE:INPUT] — Basis: FR-017 edit any submitted intake data; DR-004 version increment; audit delta

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as patient | Patient credentials | JWT |
| 2 | Navigate | `http://localhost:4200/patient/intake/{appointmentId}/edit` | Edit form loaded with v1 data |
| 3 | Update | `getByLabel('Chief Complaint')` → `"Severe headache"` | Updated value |
| 4 | Click | `getByRole('button', { name: /save/i })` | Update submitted |
| 5 | Assert | Success message | Edit saved |
| 6 | Assert API | `GET /intake/{appointmentId}` | `version: 2`, updated `chiefComplaint` |
| 7 | Assert API | Audit log | Entry with before/after `chiefComplaint` values |

#### Test Data
```yaml
original_chief_complaint: "Chest pain"
updated_chief_complaint: "Severe headache"
expected_version: 2
```

---

### FT-029 — Insurance pre-check match → Validated
- **Type**: happy_path
- **Priority**: P2
- **Requirement**: UC-010, FR-018
- **Preconditions**: `InsuranceDummyRecord` seeded with matching provider/ID
- **Source**: [SOURCE:INPUT] — Basis: UC-010 success scenario; FR-018 soft validation

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | During intake | `getByLabel('Insurance Provider')` → `"BlueCross"` | Field set |
| 2 | Fill | `getByLabel('Insurance ID')` → `"BC123456"` | Field set |
| 3 | Trigger check | Tab/blur from Insurance ID field or check button | Validation call |
| 4 | Assert | Validation indicator | `"Validated"` badge shown (green) |
| 5 | Assert API | `POST /intake/insurance-check` | `result: "Validated"` |

#### Test Data
```yaml
provider: "BlueCross"
insurance_id: "BC123456"
expected_result: "Validated"
```

---

### FT-030 — Insurance pre-check no match → non-blocking warning
- **Type**: error
- **Priority**: P1
- **Requirement**: UC-010, FR-018
- **Preconditions**: Insurance provider/ID not in dummy records
- **Source**: [SOURCE:INPUT] — Basis: UC-010 extension 2a; mismatch is non-blocking

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Fill | `getByLabel('Insurance Provider')` → `"FakeInsurance"` | Field set |
| 2 | Fill | `getByLabel('Insurance ID')` → `"FAKE999"` | Field set |
| 3 | Trigger check | Tab or check button | Validation call |
| 4 | Assert | Warning indicator | `"Not Verified"` warning shown (non-blocking) |
| 5 | Assert | Submit button | Still enabled — intake NOT blocked |
| 6 | Assert API | `POST /intake/insurance-check` | `result: "NotVerified"` |

#### Test Data
```yaml
provider: "FakeInsurance"
insurance_id: "FAKE999"
expected_result: "NotVerified"
form_still_submittable: true
```

---

### FT-031 — Confirmation email with PDF delivered ≤ 60 s
- **Type**: performance
- **Priority**: P0
- **Requirement**: UC-011, FR-014, NFR-002
- **Preconditions**: SMTP stub capturing emails; Hangfire running; booking just made
- **Source**: [SOURCE:INPUT] — Basis: NFR-002 SLA ≤ 60 s from booking; FR-014 PDF attachment required

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Record timestamp | `bookingStart = Date.now()` | Baseline recorded |
| 2 | Book appointment | `POST /appointments` | HTTP 201 |
| 3 | Poll SMTP stub | Check for received email with PDF attachment | Max 60 s |
| 4 | Assert | Email received | Within 60 s of booking |
| 5 | Assert | PDF attachment | Present; byte length > 0 |
| 6 | Assert | `Date.now() - bookingStart` | ≤ 60000 ms |

#### Test Data
```yaml
sla_ms: 60000
poll_interval_ms: 2000
max_poll_attempts: 30
attachment_mime: "application/pdf"
```

---

### FT-032 — Staff registers walk-in (existing patient)
- **Type**: happy_path
- **Priority**: P0
- **Requirement**: UC-013, FR-019
- **Preconditions**: Staff JWT; patient `Alex Rivera` (ID=3) exists
- **Source**: [SOURCE:INPUT] — Basis: UC-013 main scenario with existing patient; FR-019

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as staff | Staff credentials | Staff dashboard |
| 2 | Navigate | `http://localhost:4200/staff/queue` | Queue page |
| 3 | Click | `getByRole('button', { name: /walk-in|register/i })` | Walk-in form |
| 4 | Search | `getByRole('searchbox')` → `"Alex Rivera"` | Patient found in results |
| 5 | Select | Patient result | Patient selected |
| 6 | Click | `getByRole('button', { name: /add to queue/i })` | Walk-in submitted |
| 7 | Assert | Queue list | Alex Rivera appears with walk-in flag |
| 8 | Assert API | `GET /staff/queue` | Entry with `walkInFlag: true` |

#### Test Data
```yaml
existing_patient_name: "Alex Rivera"
existing_patient_id: 3
expected_walk_in_flag: true
```

---

### FT-033 — Staff registers walk-in (new minimal patient)
- **Type**: happy_path
- **Priority**: P1
- **Requirement**: UC-013, FR-019
- **Preconditions**: Staff JWT; patient not in system
- **Source**: [SOURCE:INPUT] — Basis: UC-013 extension 2a; FR-019 Staff can create minimal profile

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as staff | Staff credentials | JWT |
| 2 | Navigate | Queue page → walk-in form | Form rendered |
| 3 | Search | Unique new name | No results found |
| 4 | Click | `getByRole('button', { name: /create new/i })` | Minimal patient form |
| 5 | Fill | Name, DOB, phone | Fields set |
| 6 | Click | `getByRole('button', { name: /add to queue/i })` | Submitted |
| 7 | Assert | Queue list | New patient appears |
| 8 | Assert API | `GET /patients/search?q={name}` | New patient profile exists |

#### Test Data
```yaml
new_patient:
  name: "Walk In Patient"
  dob: "1975-08-20"
  phone: "555-9999"
```

---

### FT-034 — Staff views and reorders queue
- **Type**: happy_path
- **Priority**: P0
- **Requirement**: UC-014, FR-020
- **Preconditions**: Staff JWT; 3 queue entries present
- **Source**: [SOURCE:INPUT] — Basis: UC-014 success scenario; FR-020 queue management

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as staff | Staff credentials | JWT |
| 2 | Navigate | `http://localhost:4200/staff/queue` | Queue displayed with 3 entries |
| 3 | Assert | Queue rows | `position`, `estimatedWait` visible per row |
| 4 | Drag | Row at position 3 → position 1 | Reorder action |
| 5 | Assert | Queue rows | New order reflected |
| 6 | Assert API | `GET /staff/queue` | Positions updated |
| 7 | Assert | Estimated wait times | Recalculated after reorder |

#### Test Data
```yaml
initial_queue_size: 3
reorder: {from_position: 3, to_position: 1}
```

---

### FT-035 — Concurrent queue reorder conflict → 409
- **Type**: edge_case
- **Priority**: P1
- **Requirement**: UC-014, FR-020
- **Preconditions**: Two Staff clients; same queue
- **Source**: [SOURCE:INPUT] — Basis: UC-014 extension 3a; optimistic locking on queue entity

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Both Staff obtain queue rowversion | `GET /staff/queue` | Both have same ETag/rowversion |
| 2 | Staff A sends reorder | `PATCH /staff/queue/reorder` | HTTP 200 — rowversion bumped |
| 3 | Staff B sends reorder (stale rowversion) | `PATCH /staff/queue/reorder` | HTTP 409 |
| 4 | Assert | Staff B response | Contains "conflict" or "refresh" message |

#### Test Data
```yaml
stale_rowversion: "{obtained_before_staff_a_update}"
expected_conflict_status: 409
```

---

### FT-036 — Staff checks in patient — status → Arrived
- **Type**: happy_path
- **Priority**: P0
- **Requirement**: UC-015, FR-021
- **Preconditions**: Staff JWT; appointment `status: "Scheduled"` on today's schedule
- **Source**: [SOURCE:INPUT] — Basis: UC-015 success scenario; FR-021 Staff-only check-in

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as staff | Staff credentials | Staff dashboard |
| 2 | Navigate | `http://localhost:4200/staff/schedule` | Daily schedule |
| 3 | Find appointment row | Patient name | Row found with `Scheduled` status |
| 4 | Click | `getByRole('button', { name: /check in/i })` within row | Confirmation or direct action |
| 5 | Assert | Row status badge | Shows `"Arrived"` |
| 6 | Assert API | `GET /appointments/{id}` | `status: "Arrived"`, `arrivedAt` set |
| 7 | Assert | AuditLog | Entry with `staffId` and `actionType: "UPDATE"` |

#### Test Data
```yaml
appointment_status_before: "Scheduled"
expected_status_after: "Arrived"
```

---

### FT-037 — Patient cannot self-check-in → 403
- **Type**: security
- **Priority**: P0
- **Requirement**: UC-015, FR-021
- **Preconditions**: Patient JWT; appointment exists
- **Source**: [SOURCE:INPUT] — Basis: FR-021 explicitly prohibits patient self-check-in; C-004 constraint

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as patient | Patient credentials | Patient JWT |
| 2 | Call API | `PATCH http://localhost:5153/api/appointments/{id}/checkin` with patient token | HTTP 403 |
| 3 | Assert | Response body | Access denied or no message |
| 4 | Assert | Appointment status in DB | `"Scheduled"` (unchanged) |

#### Test Data
```yaml
patient_jwt: "{patient_token}"
expected_status: 403
appointment_status_unchanged: "Scheduled"
```

---

### FT-038 — High-risk appointments flagged on Staff daily schedule
- **Type**: happy_path
- **Priority**: P1
- **Requirement**: UC-016, FR-025
- **Preconditions**: Staff JWT; appointment with `noShowRiskScore >= highRiskThreshold` on today's schedule
- **Source**: [SOURCE:INPUT] — Basis: FR-025 visual alert flag; UC-016 risk review

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as staff | Staff credentials | Staff dashboard |
| 2 | Navigate | `http://localhost:4200/staff/schedule` | Daily schedule |
| 3 | Locate high-risk appointment | Row with seeded high risk score | Row found |
| 4 | Assert | Row | Risk flag indicator visible (icon or badge) |
| 5 | Assert API | `GET /staff/schedule?date={today}` | `highRiskFlag: true` for that appointment |

#### Test Data
```yaml
high_risk_threshold: 7
seeded_risk_score: 9
expected_flag: true
```

---

### FT-039 — Patient uploads valid PDF — stored encrypted
- **Type**: happy_path
- **Priority**: P0
- **Requirement**: UC-017, FR-026, FR-027
- **Preconditions**: Patient JWT; valid PDF file ≤ size limit; virus scan mock returns Pass
- **Source**: [SOURCE:INPUT] — Basis: UC-017 success scenario; FR-026 upload; FR-027 AES-256 encryption

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as patient | Patient credentials | Patient dashboard |
| 2 | Navigate | `http://localhost:4200/patient/documents` | Documents page |
| 3 | Click | `getByRole('button', { name: /upload/i })` | Upload modal opens |
| 4 | Set input | `input[type="file"]` → `clinical-report.pdf` (512 KB) | File selected |
| 5 | Click | `getByRole('button', { name: /upload|confirm/i })` | Upload submitted |
| 6 | Assert | Document list | New document entry visible |
| 7 | Assert API | `GET /documents` | New `ClinicalDocument` with `virusScanResult: "Pass"` |
| 8 | Assert filesystem | `encryptedBlobPath` file | File does NOT start with `%PDF-` bytes |

#### Test Data
```yaml
test_file: "clinical-report.pdf"
file_size_kb: 512
expected_scan_result: "Pass"
expected_extraction_status: "Pending"
```

---

### FT-040 — Upload exceeds size limit → rejected
- **Type**: error
- **Priority**: P0
- **Requirement**: UC-017, FR-026
- **Preconditions**: Patient JWT; file > `MaxFileSizeBytes` config
- **Source**: [SOURCE:INPUT] — Basis: UC-017 extension 3a; FR-026 configurable size gate

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as patient | Patient credentials | JWT |
| 2 | Navigate | Documents page → Upload modal | Modal open |
| 3 | Set input | `input[type="file"]` → oversized file (26 MB) | File selected |
| 4 | Assert | File input or validation | Immediate rejection or warning |
| 5 | Click | Upload button | Upload attempted |
| 6 | Assert | Error message | Contains max size |
| 7 | Assert | Document list | No new entry |

#### Test Data
```yaml
oversized_file_mb: 26
max_allowed_mb: 25
expected_error: "File exceeds maximum size"
```

---

### FT-041 — Virus scan fails → file rejected and incident logged
- **Type**: error
- **Priority**: P0
- **Requirement**: UC-017, FR-026
- **Preconditions**: Patient JWT; virus scan mock returns Fail
- **Source**: [SOURCE:INPUT] — Basis: UC-017 extension 3b; FR-026 virus gate before storage

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Configure | Virus scan mock → return `Fail` | Mock active |
| 2 | Login as patient | Patient credentials | JWT |
| 3 | Navigate | Documents → Upload | Modal open |
| 4 | Upload | Valid-sized PDF | Submitted |
| 5 | Assert | Error message | "File failed security scan" |
| 6 | Assert API | `GET /documents` | Entry with `virusScanResult: "Fail"` |
| 7 | Assert | AuditLog | Virus detection incident entry |

#### Test Data
```yaml
virus_scan_mock_result: "Fail"
expected_document_status: "Fail"
audit_action: "VIRUS_DETECTED"
```

---

### FT-042 — Unauthenticated document download → 401
- **Type**: security
- **Priority**: P0
- **Requirement**: UC-017, FR-027
- **Preconditions**: Document ID known; no auth header
- **Source**: [SOURCE:INPUT] — Basis: FR-027 no unauthenticated endpoint for raw document bytes; HIPAA

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Call API | `GET http://localhost:5153/api/documents/{id}/download` — no Authorization header | HTTP 401 |
| 2 | Assert | Response | No binary content returned |
| 3 | Call API | Same endpoint with expired JWT | HTTP 401 |

#### Test Data
```yaml
document_id: "{any_existing_doc_id}"
expected_status: 401
```

---

### FT-043 — OCR extraction job processes uploaded document
- **Type**: happy_path
- **Priority**: P1
- **Requirement**: UC-018, FR-028, AIR-002
- **Preconditions**: `ClinicalDocument.extractionStatus = "Pending"`; Tesseract mock returns text
- **Source**: [SOURCE:INPUT] — Basis: UC-018 success scenario; FR-028 OCR extraction; AIR-002 confidence threshold

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Seed | `ClinicalDocument(extractionStatus="Pending")` for patient | State ready |
| 2 | Trigger | `OcrExtractionJob.ExecuteAsync(documentId, ct)` | Job runs |
| 3 | Assert API | `GET /documents/{id}` | `extractionStatus: "Extracted"` |
| 4 | Assert DB | `ExtractedClinicalFields` | Rows present for documentId |
| 5 | Assert | Field types | At least one each: ChiefComplaint, Medication, Allergy |

#### Test Data
```yaml
mock_ocr_text: "Chief complaint: Headache. Medications: Aspirin 81mg. Allergies: Sulfa drugs."
mock_confidence: 0.90
expected_status: "Extracted"
```

---

### FT-044 — Conflict detection flags contradictory allergy entries
- **Type**: happy_path
- **Priority**: P1
- **Requirement**: UC-019, FR-031
- **Preconditions**: Two documents for same patient with contradictory allergy values
- **Source**: [SOURCE:INPUT] — Basis: UC-019 step 3-4; FR-031 conflict detection and flagging

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Seed | doc1 `Allergy = "Penicillin"`; doc2 `Allergy = "No known drug allergies"` | Conflict state |
| 2 | Trigger | Conflict detection pipeline | Runs |
| 3 | Assert API | `GET /patients/{id}/conflicts` | `ConflictFlag` returned with both field refs |
| 4 | Assert API | `GET /patients/{id}/view360` | `360viewStatus: "RequiresReview"` |

#### Test Data
```yaml
doc1_allergy: "Penicillin"
doc2_allergy: "No known drug allergies"
expected_conflict_count: 1
expected_360_status: "RequiresReview"
```

---

### FT-045 — Staff cannot mark 360° view Verified with unresolved conflicts
- **Type**: error
- **Priority**: P0
- **Requirement**: UC-019, FR-031
- **Preconditions**: Staff JWT; unresolved `ConflictFlag` for patient
- **Source**: [SOURCE:INPUT] — Basis: UC-019 extension 5; FR-031 conflict resolution gates Verified status

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as staff | Staff credentials | JWT |
| 2 | Navigate | `http://localhost:4200/staff/patients/{id}/view360` | 360° view page |
| 3 | Assert | Conflict flags section | Unresolved conflict visible |
| 4 | Click | `getByRole('button', { name: /verify|mark verified/i })` | Attempt to verify |
| 5 | Assert | Error or button disabled | Cannot verify with open conflicts |
| 6 | Assert API | `PATCH /patients/{id}/360/verify` | HTTP 409 |

#### Test Data
```yaml
expected_status: 409
conflict_error_message: "Resolve all conflicts before verifying"
```

---

### FT-046 — Staff accesses 360° patient view — consolidated fields
- **Type**: happy_path
- **Priority**: P0
- **Requirement**: UC-020, FR-032
- **Preconditions**: Staff JWT; patient with extracted documents; conflicts resolved
- **Source**: [SOURCE:INPUT] — Basis: UC-020 success scenario; FR-032 360° unified view

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as staff | Staff credentials | Staff dashboard |
| 2 | Navigate | `http://localhost:4200/staff/patients/{id}/view360` | 360° view page |
| 3 | Assert | Page title | "360° Patient View" heading |
| 4 | Assert | Vitals section | At least one vital shown |
| 5 | Assert | Medical History section | Populated |
| 6 | Assert | Medications section | Populated |
| 7 | Assert | Allergies section | Populated |
| 8 | Assert | Verification status badge | `"Verified"` or `"Ready for Review"` |
| 9 | Assert API | `GET /patients/{id}/view360` | All sections present in JSON |

#### Test Data
```yaml
patient_id: 19
expected_sections: ["vitals", "medicalHistory", "medications", "allergies", "diagnosisNarratives"]
```

---

### FT-047 — ICD-10 code generation for verified patient
- **Type**: happy_path
- **Priority**: P0
- **Requirement**: UC-021, FR-033
- **Preconditions**: Staff JWT; patient `verificationStatus: "Verified"` with intake data
- **Source**: [SOURCE:INPUT] — Basis: UC-021 success scenario; FR-033 ICD-10 mapping

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as staff | Staff credentials | JWT |
| 2 | Navigate | `http://localhost:4200/staff/patients/{id}/view360` | 360° view |
| 3 | Assert | Verification status | `"Verified"` |
| 4 | Click | `getByRole('button', { name: /generate codes/i })` | Code generation triggered |
| 5 | Assert | Loading indicator | Job enqueued message |
| 6 | Wait | Hangfire job completes (poll `GET /coding?patientId={id}`) | Max 30 s |
| 7 | Navigate | `http://localhost:4200/staff/coding?patientId={id}` | Coding verification page |
| 8 | Assert | ICD-10 suggestions list | At least 1 suggestion with code, description, confidence |
| 9 | Assert | Suggestions status | All `"Pending"` initially |

#### Test Data
```yaml
patient_id: 19
verification_status: "Verified"
expected_min_suggestions: 1
expected_suggestion_status: "Pending"
code_type: "ICD10"
```

---

### FT-048 — Code generation blocked for unverified patient
- **Type**: error
- **Priority**: P0
- **Requirement**: UC-021, FR-033
- **Preconditions**: Staff JWT; patient `verificationStatus: "Unverified"` or `0`
- **Source**: [SOURCE:INPUT] — Basis: UC-021 precondition; code generation requires verified 360° view

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as staff | Staff credentials | JWT |
| 2 | Navigate | Patient 360° view (unverified patient) | Page loaded |
| 3 | Assert | "Generate codes" button | Disabled or shows tooltip "Verify patient first" |
| 4 | Call API | `POST /patients/{id}/generate-codes?type=ICD10` | HTTP 422 or 409 |
| 5 | Assert | No Hangfire job | No `GenerateIcd10CodesJob` enqueued |

#### Test Data
```yaml
unverified_patient_id: "{patient_with_verification_0}"
expected_button_state: "disabled"
expected_api_status: 422
```

---

### FT-049 — Trust-First: commit without verifiedBy → 422
- **Type**: security
- **Priority**: P0
- **Requirement**: UC-021, AIR-005, DR-007
- **Preconditions**: Staff JWT; `MedicalCodeSuggestion.status = "Pending"`
- **Source**: [SOURCE:INPUT] — Basis: AIR-005 Trust-First constraint; DR-007 non-null verifier FK required

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as staff | Staff credentials | JWT |
| 2 | Obtain suggestion ID | `GET /coding?patientId={id}` | Pending suggestion ID |
| 3 | Call API | `PATCH /coding/{suggestionId}` body: `{ action: "Accept" }` (no `verifiedBy`) | HTTP 422 |
| 4 | Assert | Response | Validation error on `verifiedBy` |
| 5 | Assert DB | Suggestion status | Still `"Pending"` |

#### Test Data
```yaml
missing_field: "verifiedBy"
expected_status: 422
expected_db_status: "Pending"
```

---

### FT-050 — Staff accepts ICD-10 suggestion
- **Type**: happy_path
- **Priority**: P0
- **Requirement**: UC-022, FR-035
- **Preconditions**: Staff JWT; `MedicalCodeSuggestion.status = "Pending"`
- **Source**: [SOURCE:INPUT] — Basis: UC-022 Accept path; FR-035 Trust-First code commit

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as staff | Staff credentials | JWT |
| 2 | Navigate | `http://localhost:4200/staff/coding?patientId={id}` | Coding verification page |
| 3 | Find suggestion | ICD-10 pending suggestion row | Row visible with code, confidence |
| 4 | Click | `getByRole('button', { name: /accept/i })` within row | Acceptance submitted |
| 5 | Assert | Row status | Changes to `"Accepted"` badge |
| 6 | Assert API | `GET /coding/{suggestionId}` | `status: "Accepted"`, `verifiedById: {staffId}`, `verifiedAt` set |

#### Test Data
```yaml
staff_id: 2
expected_status: "Accepted"
verified_by_required: true
```

---

### FT-051 — Staff modifies suggestion before accepting
- **Type**: happy_path
- **Priority**: P1
- **Requirement**: UC-022, FR-035
- **Preconditions**: Staff JWT; pending ICD-10 suggestion
- **Source**: [SOURCE:INPUT] — Basis: UC-022 Modify path; FR-035 Staff can edit AI suggestion

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as staff | Staff credentials | JWT |
| 2 | Navigate | Coding verification page | Pending suggestions shown |
| 3 | Click | `getByRole('button', { name: /edit|modify/i })` within row | Edit field opens |
| 4 | Clear and fill | Code input → `"J06.9"` | Modified code entered |
| 5 | Click | `getByRole('button', { name: /save|accept/i })` | Modification submitted |
| 6 | Assert | Row | Shows `"Modified"` badge with `"J06.9"` |
| 7 | Assert API | `GET /coding/{suggestionId}` | `status: "Modified"`, `committedCode: "J06.9"` |

#### Test Data
```yaml
original_code: "{ai_suggested_code}"
modified_code: "J06.9"
expected_status: "Modified"
```

---

### FT-052 — Staff rejects suggestion
- **Type**: happy_path
- **Priority**: P1
- **Requirement**: UC-022, FR-035
- **Preconditions**: Staff JWT; pending suggestion
- **Source**: [SOURCE:INPUT] — Basis: UC-022 Reject path; FR-035 Staff can reject any suggestion

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as staff | Staff credentials | JWT |
| 2 | Navigate | Coding verification page | Pending suggestions |
| 3 | Click | `getByRole('button', { name: /reject/i })` within row | Rejection submitted |
| 4 | Assert | Row | Shows `"Rejected"` badge |
| 5 | Assert API | `GET /coding/{suggestionId}` | `status: "Rejected"`, no `committedCode` |

#### Test Data
```yaml
expected_status: "Rejected"
committed_code: null
```

---

### FT-053 — Admin reviews audit log with filters
- **Type**: happy_path
- **Priority**: P1
- **Requirement**: UC-023, FR-005
- **Preconditions**: Admin JWT; audit log entries exist
- **Source**: [SOURCE:INPUT] — Basis: UC-023 success scenario; FR-005 immutable audit log with read access

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as admin | Admin credentials | Admin JWT |
| 2 | Navigate | `http://localhost:4200/admin/audit` | Audit log page |
| 3 | Assert | Entries table | At least one entry visible |
| 4 | Filter | `getByLabel('Date From')` → today | Filter applied |
| 5 | Filter | `getByLabel('Action Type')` → `"UPDATE"` | Filter applied |
| 6 | Click | `getByRole('button', { name: /filter|apply/i })` | Results filtered |
| 7 | Assert | Entries | All shown entries have `actionType: "UPDATE"` |
| 8 | Assert | No edit/delete buttons | Log is read-only |

#### Test Data
```yaml
filter_date: "{today}"
filter_action: "UPDATE"
```

---

### FT-054 — Attempt to modify audit entry → 405
- **Type**: security
- **Priority**: P0
- **Requirement**: UC-023, NFR-008
- **Preconditions**: Admin JWT; known audit log entry ID
- **Source**: [SOURCE:INPUT] — Basis: NFR-008 HTTP 405 on any audit modification; immutable append-only

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as admin | Admin credentials | Admin JWT |
| 2 | Get audit entry ID | `GET /admin/audit` | Any entry ID |
| 3 | Call API | `DELETE http://localhost:5153/api/admin/audit/{id}` | HTTP 405 |
| 4 | Call API | `PATCH http://localhost:5153/api/admin/audit/{id}` | HTTP 405 |
| 5 | Assert | Audit log | New entry for "blocked modification attempt" |

#### Test Data
```yaml
expected_status: 405
audit_action_for_attempt: "BLOCKED_AUDIT_MODIFICATION"
```

---

### FT-055 — Session timeout after 15 min inactivity → 401
- **Type**: security
- **Priority**: P0
- **Requirement**: UC-024, FR-004, NFR-007
- **Preconditions**: Valid JWT; Redis token entry set
- **Source**: [SOURCE:INPUT] — Basis: FR-004 15-minute server-side timeout; NFR-007 JWT enforcement

#### Steps
| Step | Action | Target | Expected Result |
|------|--------|--------|-----------------|
| 1 | Login as patient | Patient credentials | JWT obtained; Redis key set |
| 2 | Expire Redis key | `redis.del(token:{jti})` or advance TTL | Key removed server-side |
| 3 | Call API | `GET /patient/appointments` with stale JWT | HTTP 401 |
| 4 | Assert | Angular app | Redirects to `/login?reason=timeout` |
| 5 | Assert | Login page | Timeout notification message |

#### Test Data
```yaml
redis_ttl_seconds: 900
simulated_expiry: "manual_redis_del"
expected_status: 401
expected_redirect: "/login"
```

---

## Selector Strategy
| Element | Primary Selector | Fallback Selector | Screen |
|---------|-----------------|-------------------|--------|
| Email input | `getByLabel('Email')` | `getByTestId('input-email')` | Login, Register, Forgot Password |
| Password input | `getByLabel('Password')` | `getByTestId('input-password')` | Login, Reset Password |
| Sign In button | `getByRole('button', { name: 'Sign In' })` | `getByTestId('btn-signin')` | Login |
| Create Account button | `getByRole('button', { name: 'Create Account' })` | `getByTestId('btn-register')` | Register |
| Alert/notification | `getByRole('alert')` | `getByTestId('app-alert')` | All pages |
| Staff nav | `getByRole('navigation')` | `getByTestId('nav-staff')` | Staff layout |
| Book appointment button | `getByRole('button', { name: 'Confirm Booking' })` | `getByTestId('btn-confirm-booking')` | Booking calendar |
| Cancel appointment button | `getByRole('button', { name: /cancel/i })` | `getByTestId('btn-cancel-appt')` | Appointments list |
| Upload trigger button | `getByRole('button', { name: /upload/i })` | `getByTestId('btn-upload-doc')` | Documents page |
| File input | `locator('input[type="file"]')` | `getByTestId('file-input')` | Upload modal |
| Generate codes button | `getByRole('button', { name: /generate codes/i })` | `getByTestId('btn-generate-codes')` | Patient 360° view |
| Accept code button | `getByRole('button', { name: /accept/i })` | `getByTestId('btn-accept-code')` | Code verification table |
| Check In button | `getByRole('button', { name: /check in/i })` | `getByTestId('btn-checkin')` | Staff schedule |
| Walk-in button | `getByRole('button', { name: /walk-in|register/i })` | `getByTestId('btn-walkin')` | Staff queue |
| Queue row | `getByRole('row')` | `getByTestId('queue-row')` | Staff queue |
| Chief complaint field | `getByLabel('Chief Complaint')` | `getByTestId('input-chief-complaint')` | Intake form |
| Conflict flag | `getByRole('alert', { name: /conflict/i })` | `getByTestId('conflict-flag')` | 360° view |
| 360° verify button | `getByRole('button', { name: /verify|mark verified/i })` | `getByTestId('btn-verify-360')` | 360° view |
| Audit log table | `getByRole('table', { name: /audit/i })` | `getByTestId('audit-table')` | Admin audit page |
| Role selector | `getByRole('combobox', { name: 'Role' })` | `getByTestId('select-role')` | Admin create user |

## Validation Commands
| Check | Command | Expected |
|-------|---------|----------|
| All feature tests | `npx playwright test tests/e2e/features/ --project=chromium` | Exit 0 |
| Auth tests only | `npx playwright test tests/e2e/features/ --grep "FT-00[1-9]\|FT-01[0-2]"` | Exit 0 |
| Security tests | `npx playwright test tests/e2e/features/ --grep "security"` | Exit 0 |
| P0 tests only | `npx playwright test tests/e2e/features/ --grep "@P0"` | Exit 0 |
| With trace | `npx playwright test --trace on` | Exit 0; traces in `test-results/` |
| CI mode | `npx playwright test --reporter=junit --output-file=results.xml` | Exit 0 |
