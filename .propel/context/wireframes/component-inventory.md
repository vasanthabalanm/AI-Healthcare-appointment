# Component Inventory — ClinicalHub

> **Component Specification** | Unified Patient Access & Clinical Intelligence Platform  
> **Framework:** Angular 17.x (standalone components, no third-party UI kit)  
> **Version:** 1.0 · **Date:** May 2026

---

## Component Summary

| # | Component ID | Name | Category | Priority | Used in screens |
|---|-------------|------|----------|----------|-----------------|
| 1 | C-001 | LoginForm | Form | P0 | SCR-001 |
| 2 | C-002 | RegistrationForm | Form | P0 | SCR-002 |
| 3 | C-003 | PasswordStrengthMeter | Indicator | P1 | SCR-002 |
| 4 | C-004 | PasswordResetFlow | Form | P0 | SCR-003 |
| 5 | C-005 | SessionTimeoutOverlay | Overlay/Modal | P0 | SCR-004 (all screens) |
| 6 | C-006 | PatientDashboard | Layout | P0 | SCR-005 |
| 7 | C-007 | BookingCalendar | Calendar | P0 | SCR-006 |
| 8 | C-008 | SlotPicker | Selection | P0 | SCR-006 |
| 9 | C-009 | AppointmentConfirmCard | Card | P1 | SCR-007 |
| 10 | C-010 | InsuranceValidationIndicator | Indicator | P1 | SCR-012 |
| 11 | C-011 | DocumentList | List | P1 | SCR-013 |
| 12 | C-012 | VirusScanBadge | Badge | P1 | SCR-013 |
| 13 | C-013 | CalendarSyncButton | Button | P2 | SCR-007, SCR-008, SCR-015 |
| 14 | C-014 | WaitlistForm | Form | P1 | SCR-009 |
| 15 | C-015 | SlotSwapOfferCard | Card | P1 | SCR-010 |
| 16 | C-016 | CountdownTimer | Indicator | P1 | SCR-010 |
| 17 | C-017 | AIChatPanel | Chat | P0 | SCR-011 |
| 18 | C-018 | IntakeSummaryPanel | Panel | P1 | SCR-011 |
| 19 | C-019 | ManualIntakeForm | Form | P0 | SCR-012 |
| 20 | C-020 | ConfidenceMeter | Indicator | P0 | SCR-021 |
| 21 | C-021 | StaffScheduleTable | Table | P0 | SCR-016 |
| 22 | C-022 | NoShowRiskBadge | Badge | P1 | SCR-016 |
| 23 | C-023 | WalkInSearch | Search | P1 | SCR-017 |
| 24 | C-024 | QueueTable | Table | P0 | SCR-018 |
| 25 | C-025 | Patient360View | Layout | P0 | SCR-019 |
| 26 | C-026 | ConflictResolutionPanel | Panel | P0 | SCR-019/020 |
| 27 | C-027 | AuditLogTable | Table | P0 | SCR-023 |
| 28 | C-028 | UserManagementTable | Table | P0 | SCR-022 |

---

## Detailed Component Specifications

### Authentication Components

#### C-001 `LoginForm`
```
Selector: app-login-form
Inputs:
  - role: 'patient' | 'staff' | 'admin' (demo switcher)
Outputs:
  - loginSuccess: { userId, role, redirectPath }
  - loginError: string
States:
  - idle | submitting | error
Accessibility:
  - form role, aria-label, aria-describedby for error
Security:
  - Generic error message (no credential enumeration)
  - Rate limiting enforced server-side
  - Redirect path validated against allowlist
```

#### C-002 `RegistrationForm`
```
Selector: app-registration-form
States: idle | submitting | email-exists-error | success
Sections: personal info, credentials, terms consent
Outputs: registrationSuccess, registrationError
```

#### C-003 `PasswordStrengthMeter`
```
Selector: app-password-strength
Inputs: password: string
States:
  - 0 bars / none (empty)
  - 1 bar / Weak (red #C0392B)
  - 2 bars / Fair (amber #D4820A)
  - 3 bars / Good (yellow #D4BF00)
  - 4 bars / Strong (green #1A7A4A)
Rules: min 8 chars + uppercase + number + symbol = Strong
Accessibility: aria-label="Password strength: [level]"
```

#### C-004 `PasswordResetFlow`
```
Selector: app-password-reset
Steps: email-entry → token-check → new-password → success
States: idle | submitting | token-expired | success
Security: Always shows "sent" regardless of email existence
```

---

### Session & Navigation Components

#### C-005 `SessionTimeoutOverlay`
```
Selector: app-session-timeout
Trigger: idle > 15 minutes (JWT TTL 900s)
States: warning (countdown) | expired
countdown: 30s
Outputs: extend | logout
Accessibility:
  - role="dialog", aria-modal="true"
  - role="timer" on countdown
  - Focus trapped in modal
```

---

### Patient Components

#### C-006 `PatientDashboard`
```
Selector: app-patient-dashboard
Sections:
  - Summary row (next appt, intake status, doc count)
  - Upcoming appointment card
  - Quick actions (Book, Intake, Upload, My Appointments)
Data: patient profile, upcoming appointments, intake records
Empty states: each section has its own empty state with CTA
```

#### C-007 `BookingCalendar`
```
Selector: app-booking-calendar
Inputs:
  - month: Date
  - availableDates: Date[]
  - bookedDates: Date[]
Outputs:
  - dateSelected: Date
Day states: available | booked-full | past | selected | today
Accessibility:
  - role="grid", aria-label, aria-pressed on days
  - Keyboard: Arrow keys to navigate, Enter to select
```

#### C-008 `SlotPicker`
```
Selector: app-slot-picker
Inputs: selectedDate: Date, slots: Slot[]
Slot states: available | booked
Outputs: slotSelected: Slot
Displays: booking summary on selection
```

#### C-009 `AppointmentConfirmCard`
```
Selector: app-appointment-confirm
Inputs: appointment: Appointment
Outputs: pdfDownload, calendarSync, viewAppointments, startIntake
Shows: date, time, provider, type, location, reference number, email notice
```

#### C-010 `InsuranceValidationIndicator`
```
Selector: app-insurance-indicator
States:
  - verified: green badge "Insurance verified — [provider] ([memberId])"
  - pending: amber "Insurance details incomplete"
  - none: grey "No insurance information entered"
Trigger: onblur on provider + memberId fields
Role: status, aria-live="polite"
```

#### C-011 `DocumentList`
```
Selector: app-document-list
Columns: Name, Upload date, Virus scan (C-012), OCR confidence, Actions
Inputs: documents: ClinicalDocument[]
Outputs: viewDocument, uploadRequested
Features: Low confidence warning icon (61% or lower)
```

#### C-012 `VirusScanBadge`
```
Selector: app-virus-scan-badge
States:
  - passed: green "Passed"
  - scanning: amber "Scanning…"
  - failed: red "Failed — virus detected"
  - unavailable: red "Scan service unavailable"
Colour + text always (never colour-only)
```

#### C-013 `CalendarSyncButton`
```
Selector: app-calendar-sync-btn
Providers: google | outlook
States: idle | loading | success | declined
OAuth: redirect to SCR-015 (calendar-sync)
Note: No credential storage; event-only OAuth scope
```

#### C-014 `WaitlistForm`
```
Selector: app-waitlist-form
Fields: preferred-date, preferred-time-window, provider-preference, notify-method
States: idle | existing-entry-warning | replacing | success
MOD-008: Replace existing entry confirmation dialog (embedded)
```

#### C-015 `SlotSwapOfferCard`
```
Selector: app-slot-swap-offer
Inputs: offer: SlotSwapOffer (current + offered slot)
States: active | expired
Actions: accept → SCR-007, decline → SCR-008 (toast)
Expired: full-page state with waitlist CTA
```

#### C-016 `CountdownTimer`
```
Selector: app-countdown-timer
Inputs: seconds: number, label: string
Output: expired EventEmitter
Accessibility: role="timer", aria-live="polite"
Visual: amber text when <5 minutes; red when <60 seconds
```

---

### Intake Components

#### C-017 `AIChatPanel`
```
Selector: app-ai-chat
Integration: Rasa Open Source 3.x (localhost:5005)
Features:
  - Typewriter effect for AI responses
  - Typing indicator (3-dot bounce)
  - "AI Suggested" label on pre-filled answers
  - Progressive summary panel update (C-018)
  - "Switch to manual form" escape hatch
Inputs: appointmentId, patientId
Outputs: intakeComplete, switchToManual
Accessibility: role="log", aria-live="polite"
Trust-First: No auto-submit; patient reviews before final submit
```

#### C-018 `IntakeSummaryPanel`
```
Selector: app-intake-summary
Built progressively during AI chat
Sections: chief-complaint, medications, allergies, insurance
Progress bar: 0–100% as sections complete
AI label on AI-suggested items
Submit disabled until all sections complete
```

#### C-019 `ManualIntakeForm`
```
Selector: app-manual-intake
Sections: Demographics, Chief Complaint, Current Medications, Allergies, Insurance
Medication rows: dynamic add/remove
Insurance validation: C-010 inline
Auto-save: draft persisted on blur
Validation: onBlur per field + final submit validation
```

---

### Staff Components

#### C-020 `ConfidenceMeter`
```
Selector: app-confidence-meter
Inputs: confidence: number (0–100)
Visual:
  - Numeric percentage + coloured progress bar
  - ≥80%: green (#1A7A4A)
  - 50–79%: amber (#D4820A)
  - <50%: red (#C0392B) + "low confidence" flag
Accessibility: aria-label="Confidence: [n]%"
Trust-First: <80% shows "manual review recommended"
```

#### C-021 `StaffScheduleTable`
```
Selector: app-staff-schedule
Columns: Time, Patient, Type, Status, No-show risk, Notes, Actions
Features:
  - High-risk row highlight (amber background)
  - "High Risk only" filter toggle (C-022)
  - Inline Check In action → status update
Inputs: appointments: Appointment[], date: Date
```

#### C-022 `NoShowRiskBadge`
```
Selector: app-noshow-risk-badge
Inputs: riskLevel: 'high' | 'medium' | 'low'
States:
  - high: amber ⚠ badge + "High Risk" text (never icon-only)
  - low: green "Low" badge
  - medium: amber "Medium" badge
Used in: staff schedule row, 360° view header
```

#### C-023 `WalkInSearch`
```
Selector: app-walkin-search
Inputs: searchQuery: string
States: idle | found | not-found
Found: result card with "Add to queue" action
Not found: "Create minimal profile" form toggle
MOD-006: Capacity override dialog (if queue full)
```

#### C-024 `QueueTable`
```
Selector: app-queue-table
Columns: Drag handle, Position, Patient, Type, Arrived, Status, Walk-in flag, Actions
Features:
  - Drag-drop (CDK DragDrop — Angular CDK)
  - Position number inputs (keyboard alternative per UXR-208)
  - Concurrent edit conflict detection → aria-live assertive toast
  - Walk-in badge (amber)
```

#### C-025 `Patient360View`
```
Selector: app-patient-360
Sections (collapsible): Vitals, Medications, Allergies, Medical History, Diagnoses
Conflict banner: shown if unresolved conflicts exist
"Mark as verified" button: disabled until conflicts resolved (Trust-First)
Inputs: patientId: string
Outputs: openConflictPanel, generateCodes
```

#### C-026 `ConflictResolutionPanel`
```
Selector: app-conflict-panel
Trigger: conflict flag click in C-025
Presentation: slide-out from right (400px)
Inputs: conflict: DataConflict
Shows: side-by-side diff (source + date per side)
Actions: "Use this value" (either side), dismiss (with audit log note)
Outputs: conflictResolved, conflictDismissed
```

---

### Admin Components

#### C-027 `AuditLogTable`
```
Selector: app-audit-log
Columns: Timestamp, Actor, Action (badge), Details, Entity ID
Filters: date-range, actor name, action type
Pagination: 20 rows per page; async for >10k rows
Export: CSV download (async for large datasets)
Immutable: no edit/delete actions exposed
```

#### C-028 `UserManagementTable`
```
Selector: app-user-management
Columns: Name, Role, Email, Status, Last active, Actions
Actions: Edit (modal), Deactivate → MOD confirm, Reactivate
Create user: modal form (C-028-modal)
Role badges: Staff (teal), Admin (purple)
Status badges: Active (green), Inactive (grey)
Cannot self-deactivate (current user row)
```

---

## Modals Summary

| Modal ID | Name | Trigger screen | Trigger | Confirms |
|----------|------|----------------|---------|----------|
| MOD-001 | Session Timeout | All screens | 15min idle | Extend session / Sign out |
| MOD-002 | Document Upload | SCR-013 | Upload button | — (dropzone modal) |
| MOD-003 | Confirm Cancel | SCR-008 | Cancel button | Cancel appointment |
| MOD-004 | Confirm Reschedule | SCR-008 | Reschedule button | Navigate to SCR-006 |
| MOD-005 | Accept All Codes | SCR-021 | "Accept all" button | Accept all 4 pending codes |
| MOD-006 | Capacity Override | SCR-017 | Add to queue (at cap) | Override + add; action logged |
| MOD-007 | — | (Conflict panel is slide-out, not modal) | — | — |
| MOD-008 | Replace Waitlist Entry | SCR-009 | Submit waitlist form | Replace existing entry |

---

## States Matrix

| Component | Default | Loading | Empty | Error | Disabled |
|-----------|---------|---------|-------|-------|----------|
| C-001 LoginForm | ✅ | ✅ | — | ✅ | — |
| C-002 RegistrationForm | ✅ | ✅ | — | ✅ | — |
| C-003 PasswordStrengthMeter | ✅ (empty) | — | ✅ | — | — |
| C-007 BookingCalendar | ✅ | — | ✅ (no slots) | ✅ (slot taken) | ✅ (past days) |
| C-008 SlotPicker | ✅ | — | ✅ | ✅ (slot taken) | ✅ (booked slot) |
| C-010 InsuranceValidationIndicator | ✅ | ✅ (checking) | ✅ (none) | ✅ (incomplete) | — |
| C-012 VirusScanBadge | — | ✅ (scanning) | — | ✅ (failed) | ✅ (unavailable) |
| C-015 SlotSwapOfferCard | ✅ (active) | — | — | ✅ (expired) | — |
| C-016 CountdownTimer | ✅ | — | — | ✅ (expired) | — |
| C-017 AIChatPanel | ✅ | ✅ (typing) | — | ✅ (Rasa offline) | — |
| C-020 ConfidenceMeter | ✅ (high) | — | — | ✅ (low conf) | — |
| C-024 QueueTable | ✅ | — | ✅ | ✅ (conflict) | — |
| C-026 ConflictResolutionPanel | ✅ (closed) | — | — | — | ✅ (resolved) |

---

## Reusability Analysis

### Highly reusable (used in 3+ screens)
| Component | Reuse instances |
|-----------|----------------|
| `StatusBadge` (inline) | All 23 screens — appointment, scan, role badges |
| Navigation sidebar | SCR-005 through SCR-023 (role-scoped variants: patient/staff/admin) |
| Navbar | All authenticated screens |
| Button group (primary/secondary/danger) | All screens |
| Toast notification | SCR-008, SCR-010, SCR-016, SCR-018, SCR-022 |
| Alert banner | SCR-001 through SCR-013 |
| Form field (label + input + error) | SCR-002, SCR-003, SCR-009, SCR-012, SCR-017, SCR-022 |

### Screen-specific (1 instance)
- C-007 BookingCalendar (SCR-006 only)
- C-017 AIChatPanel (SCR-011 only)
- C-024 QueueTable (SCR-018 only)
- C-026 ConflictResolutionPanel (SCR-019/020 only)
- C-027 AuditLogTable (SCR-023 only)

---

## Responsive Breakpoints Summary

| Component | 1280px | 768px | 375px |
|-----------|--------|-------|-------|
| Sidebar | 240px fixed | Hidden | Hidden |
| Calendar grid | 7-col CSS grid | 7-col (smaller cells) | 4-col (two-week view) |
| Slot panel | Right sidebar | Below calendar | Full-width stacked |
| Confirmation card | 560px max-width | Full-width | Full-width |
| Intake form | 700px max-width | Full-width | Full-width |
| Form field row | 2-column | 2-column | 1-column |
| Staff tables | Full columns | Scroll-x | Column visibility reduced |
| Queue table | All columns | Core cols only | Minimal: pos + name + actions |
| Chat panel | Side-by-side split | Chat only | Chat only (summary hidden) |
| Modal | 440px | 90vw | 90vw |

---

## Implementation Priority Matrix

| Priority | Components | Sprint target |
|----------|-----------|---------------|
| **P0 — Critical path** | C-001, C-002, C-004, C-005, C-006, C-007, C-008, C-017, C-019, C-020, C-021, C-024, C-025, C-026, C-027, C-028 | Sprint 1–3 |
| **P1 — Core features** | C-003, C-009, C-010, C-011, C-012, C-014, C-015, C-016, C-018, C-022, C-023 | Sprint 4–5 |
| **P2 — Enhancements** | C-013 (Calendar sync) | Sprint 6 |

---

## Framework-Specific Notes (Angular 17 Standalone)

```typescript
// All components use standalone: true
@Component({
  selector: 'app-booking-calendar',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, HeroiconsModule],
  changeDetection: ChangeDetectionStrategy.OnPush
})

// State management: Angular Signals (no NgRx for Phase 1)
// No third-party UI kit — all components hand-crafted per wireframe tokens
// CDK: @angular/cdk for DragDrop (SCR-018 queue), OverlayContainer (modals)
// Heroicons: @heroicons/angular for all icons
// HTTP client: HttpClient with JWT interceptor
// Forms: ReactiveFormsModule (AbstractControl, FormGroup, Validators)
```

---

## Accessibility

| Component | ARIA role | Live region | Focus management |
|-----------|-----------|-------------|-----------------|
| C-001 LoginForm | `form` | `aria-live="assertive"` on error | Auto-focus email field |
| C-005 SessionTimeout | `dialog`, `aria-modal` | — | Focus trapped; restore on close |
| C-007 BookingCalendar | `grid` | `aria-live="polite"` on month change | Arrow key navigation |
| C-008 SlotPicker | `list`, `listitem` | — | Tab through slots |
| C-010 InsuranceIndicator | `status` | `aria-live="polite"` | — |
| C-012 VirusScanBadge | `status` | `aria-live="polite"` | — |
| C-017 AIChatPanel | `log` | `aria-live="polite"` | Auto-scroll to bottom |
| C-024 QueueTable | `table` | `aria-live="assertive"` (conflict) | Focus position input on drag end |
| C-026 ConflictPanel | `complementary` | — | Focus first "Use this value" on open |
| All modals | `dialog`, `aria-modal` | — | Focus first interactive element; restore on close |

---

## Design System Integration

All components consume CSS custom properties defined in `designsystem.md`:

```css
/* Token usage by component category */
/* Navigation */
--cp (border-left active), --cps (active background)

/* Forms */
--cp (focus ring), --cb (border default), --ce (error state)

/* Buttons */
--cp (primary background), --cph (primary hover)
--cb (secondary border), --ce (danger)

/* Badges */
--cokb/--cok (success/verified)
--cwb/--cw (warning/risk)
--ceb/--ce (error/danger)
--cib/--ci (info/AI)

/* Indicators */
--cok (confidence ≥80%), --cw (50–79%), --ce (<50%)
```

All design tokens are applied as CSS custom properties scoped to `:root`. No hardcoded hex values in component stylesheets.
