# Figma Specification — Unified Patient Access & Clinical Intelligence Platform

**Version:** 1.0
**Date:** 2026-05-12
**Aesthetic Direction:** Utilitarian
**Viewport Strategy:** Desktop-first responsive (1280px / 768px / 375px)
**Accessibility Target:** WCAG 2.1 Level AA
**Status:** Draft — Pending Figma Implementation

---

## Source References

| Type | Source | Path |
|------|--------|------|
| Primary | Functional Specification | `.propel/context/docs/spec.md` |
| Primary | Architecture & Design | `.propel/context/docs/design.md` |
| Related | Epics | `.propel/context/docs/epics.md` |
| Related | Design System | `.propel/context/docs/designsystem.md` |

**Figma File URL:** _Not yet created — greenfield project_
**Design System URL:** _Not yet created — see `designsystem.md`_

---

## UX Requirements

### Global Requirements (UXR-001 – UXR-010)

| ID | Category | Requirement | Acceptance Criteria | Screens | Basis |
|----|----------|-------------|---------------------|---------|-------|
| UXR-001 | Global | Every screen implements 5 states | Default, Loading, Empty, Error, Validation states exist per screen | All | Design standard |
| UXR-002 | Global | PHI excluded from browser context | Patient name / appointment ID never in tab title or URL query string | All | FR-005 |
| UXR-003 | Global | Session timeout warning at T−2 min | Modal with countdown + "Extend Session" + "Log Out" shown 2 min before expiry | SCR-004 | FR-004, UC-024 |
| UXR-004 | Global | Role-based navigation enforced | Staff/Admin never see Patient-only routes; Patient never sees Staff/Admin routes | All | FR-003 |
| UXR-005 | Global | Async loading feedback ≤200ms | Loading skeleton or spinner shown within 200ms of any async operation | All | NFR-003 |
| UXR-006 | Global | Destructive actions require confirmation | Confirmation dialog before cancel appointment, delete user, reject code, dismiss conflict | Multi | Design standard |
| UXR-007 | Global | Inline validation on field blur | Field error messages appear on blur — not only on form submit | All forms | FR-016 |
| UXR-008 | Global | AI output labelled "AI Suggested" | Every AI-generated field shows "AI Suggested" label + confidence score | SCR-011, SCR-021 | FR-033, FR-034 |
| UXR-009 | Global | Trust-First: no auto-commit of AI output | No AI code or extraction committed to patient record without explicit Staff action | SCR-019–SCR-021 | FR-035 |
| UXR-010 | Global | Audit log entries immutable in UI | No edit, delete, or override action exposed for audit entries | SCR-023 | FR-005 |

### Usability (UXR-1XX)

| ID | Category | Requirement | Acceptance Criteria | Screens | Basis |
|----|----------|-------------|---------------------|---------|-------|
| UXR-101 | Usability | Patient booking in ≤4 steps | Dashboard → Calendar → Slot → Confirm in 4 pages or fewer | SCR-005–007 | FR-007 |
| UXR-102 | Usability | Check-in reachable in ≤2 clicks from schedule | "Check In" action available directly from Daily Schedule row without navigation | SCR-016 | FR-021 |
| UXR-103 | Usability | Queue reorder: drag-drop and keyboard | Drag-drop for mouse users; position number input for keyboard/accessibility | SCR-018 | FR-020 |
| UXR-104 | Usability | Code rows visible at 1280px without scroll | Full ICD-10/CPT row (code + description + confidence + actions) visible without horizontal scroll | SCR-021 | FR-033 |
| UXR-105 | Usability | 360° view sections individually collapsible | Vitals, medications, allergies, history, diagnoses each independently collapsible | SCR-019 | FR-032 |
| UXR-106 | Usability | Intake field errors on blur | Required field error appears on individual field blur, not only on submit | SCR-012 | FR-016 |
| UXR-107 | Usability | Conflict diff shows source document reference | Source document name + upload date visible inline in side-by-side diff view | SCR-020 | FR-031 |
| UXR-108 | Usability | Audit log filters persist across pagination | Applied date/actor/action filters preserved when navigating between pages | SCR-023 | FR-005 |
| UXR-109 | Usability | Walk-in patient search results ≤500ms | Patient search results returned within 500ms of query input | SCR-017 | FR-019 |
| UXR-110 | Usability | Calendar slot availability updates in-place | Month change updates slot availability without full page reload | SCR-006 | FR-007 |

### Accessibility — WCAG 2.1 AA (UXR-2XX)

| ID | Category | Requirement | Acceptance Criteria | Screens | Basis |
|----|----------|-------------|---------------------|---------|-------|
| UXR-201 | Accessibility | Colour contrast ≥4.5:1 | All text/background pairs pass 4.5:1 contrast ratio check | All | WCAG 1.4.3 |
| UXR-202 | Accessibility | Full keyboard navigation | All interactive elements reachable via Tab/Shift+Tab; activated via Enter/Space | All | WCAG 2.1.1 |
| UXR-203 | Accessibility | Alt text / aria-label on all icons | All non-decorative images and icon-only buttons have descriptive labels | All | WCAG 1.1.1 |
| UXR-204 | Accessibility | Visible labels on all form inputs | No input relies solely on placeholder text for its label | All forms | WCAG 1.3.1 |
| UXR-205 | Accessibility | Error messages linked to inputs via aria | `aria-describedby` associates error message element with invalid input | All forms | WCAG 1.3.1 |
| UXR-206 | Accessibility | Dynamic content announced via aria-live | Route changes, toast messages, queue updates announced via `aria-live` regions | All | WCAG 4.1.3 |
| UXR-207 | Accessibility | Logical focus order | Tab order follows visual reading order top-to-bottom, left-to-right | All | WCAG 2.4.3 |
| UXR-208 | Accessibility | Keyboard alternative for drag-drop | Queue reorder exposes position number input as full keyboard-accessible alternative | SCR-018 | WCAG 2.1.1 |
| UXR-209 | Accessibility | Modal focus trap | Focus constrained within modal; returns to trigger element on close | SCR-004, MOD-002–MOD-006 | WCAG 2.4.3 |
| UXR-210 | Accessibility | Countdown announced via aria-live assertive | Session timeout countdown updates announced to screen readers at minimum every 30 seconds | SCR-004 | WCAG 4.1.3 |

### Responsiveness (UXR-3XX)

| ID | Category | Requirement | Acceptance Criteria | Screens | Basis |
|----|----------|-------------|---------------------|---------|-------|
| UXR-301 | Responsiveness | Desktop-primary at 1280px | All screens fully functional and unclipped at 1280px viewport | All | Gap-001 |
| UXR-302 | Responsiveness | Tablet support at 768px | All screens functional at 768px; layout adapts to 2-column or single-column | All | Gap-001 |
| UXR-303 | Responsiveness | Mobile at 375px for patient screens | Patient-facing screens (SCR-001–SCR-015) functional at 375px | SCR-001–015 | Gap-001 |
| UXR-304 | Responsiveness | Staff screens degrade gracefully at 768px | Staff screens readable at 768px; not optimised for 375px | SCR-016–021 | Gap-001 |
| UXR-305 | Responsiveness | Touch targets ≥44×44px at mobile and tablet | All interactive elements meet minimum 44×44px at 375px and 768px viewports | SCR-001–015 | WCAG 2.5.5 |
| UXR-306 | Responsiveness | Tables reflow to card layout below 768px | DailyScheduleTable and AuditLogTable convert to card/list layout below 768px | SCR-016, SCR-023 | Gap-001 |
| UXR-307 | Responsiveness | Navigation collapses to hamburger | SideNav collapses to hamburger menu at 768px and below | All | Gap-001 |

### Visual Design (UXR-4XX)

| ID | Category | Requirement | Acceptance Criteria | Screens | Basis |
|----|----------|-------------|---------------------|---------|-------|
| UXR-401 | Visual Design | Utilitarian aesthetic enforced | No backdrop-filter blur, no purple-to-blue gradients, no background textures or decorative patterns | All | Gap-003 |
| UXR-402 | Visual Design | Primary teal reserved for primary actions | #0F6B6B used only for primary buttons, active nav indicator, and link text | All | Gap-003 |
| UXR-403 | Visual Design | Surface hierarchy via background shading | Depth expressed via #FFFFFF → #F5F5F5 → #EBEBEB — not box-shadow layering | All | Gap-003 |
| UXR-404 | Visual Design | Typography: system-ui with IBM Plex Sans | Font stack: `system-ui, "IBM Plex Sans", sans-serif`; minimum body text 14px | All | Gap-003 |
| UXR-405 | Visual Design | Status indicators use colour + text label | All status badges and alerts show both colour and text — never colour-only | All | WCAG 1.4.1 |
| UXR-406 | Visual Design | Cards: 4px radius, 1px border, no shadow | Card components use 4px border-radius, 1px solid #D0D0D0 border, no drop shadow | All cards | Gap-003 |
| UXR-407 | Visual Design | 4px spacing grid throughout | All spacing values are multiples of 4px | All | Gap-003 |
| UXR-408 | Visual Design | PHI-safe browser tab titles | Page titles use role + section name only (e.g., "Daily Schedule — Staff \| ClinicalHub") | All | FR-005 |
| UXR-409 | Visual Design | No-show risk: amber badge + warning icon | High risk shown as amber (#D4820A) badge + ⚠ icon + "High Risk" text — not colour-only | SCR-016 | FR-025 |
| UXR-410 | Visual Design | AI confidence: numeric % + visual bar | Confidence shown as percentage number + horizontal filled bar — not colour-only | SCR-021 | FR-033, FR-034 |

### Interaction (UXR-5XX)

| ID | Category | Requirement | Acceptance Criteria | Screens | Basis |
|----|----------|-------------|---------------------|---------|-------|
| UXR-501 | Interaction | Calendar month navigation + keyboard day nav | Prev/Next month buttons; arrow keys navigate days within displayed month | SCR-006 | FR-007 |
| UXR-502 | Interaction | Session timeout modal with live countdown | Modal appears at T−2min with live countdown; "Extend" resets timer; "Log Out" ends session | SCR-004 | FR-004, UC-024 |
| UXR-503 | Interaction | AI chat streaming typewriter output | Assistant messages render character-by-character; patient can type reply before completion | SCR-011 | FR-015 |
| UXR-504 | Interaction | Upload: real-time progress bar + scan status | Upload % progress bar visible; scan status transitions Scanning → Passed/Failed inline | SCR-014 | FR-026 |
| UXR-505 | Interaction | Slot swap email deep-links with pre-loaded context | Email Accept/Decline links open SCR-010 with swap offer context pre-loaded | SCR-010 | FR-009 |
| UXR-506 | Interaction | Conflict resolution: side-by-side diff | Diff view shows Document A value ↔ Document B value + source document name + upload date | SCR-020 | FR-031 |
| UXR-507 | Interaction | Inline code verification actions | Accept / Modify / Reject actions available per row inline — no navigation to separate page | SCR-021 | FR-035 |
| UXR-508 | Interaction | Async audit export with progress notification | "Export CSV" triggers async job; progress banner shown; download link provided on completion | SCR-023 | FR-005 |
| UXR-509 | Interaction | Insurance validation updates inline on blur | Insurance ID field validates against dummy records after blur — no page reload required | SCR-012 | FR-018 |
| UXR-510 | Interaction | "Accept All" codes requires confirmation | "Accept All" button opens dialog listing all codes before any commit occurs | SCR-021 | FR-035 |

### Error Handling (UXR-6XX)

| ID | Category | Requirement | Acceptance Criteria | Screens | Basis |
|----|----------|-------------|---------------------|---------|-------|
| UXR-601 | Error Handling | 5 states designed for every screen | Default / Loading / Empty / Error / Validation state explicitly designed per screen | All | UXR-001 |
| UXR-602 | Error Handling | Upload rejection states specific reason | Error message names exact cause: file too large / unsupported format / virus detected / storage failure | SCR-014 | FR-026 |
| UXR-603 | Error Handling | Slot unavailable shown inline not as modal | "This slot was just taken — please choose another" shown inline on booking step | SCR-006–007 | FR-007 |
| UXR-604 | Error Handling | AI engine unavailable: non-blocking banner | Banner scoped to affected panel only — does not block rest of screen content | SCR-011, SCR-021 | FR-015 |
| UXR-605 | Error Handling | HTTP 401 redirects to login with message | All 401 responses redirect to SCR-001 with "Your session has expired" message | All | FR-004 |
| UXR-606 | Error Handling | HTTP 503 ClamAV shows specific message + retry | "Document scan unavailable — please try again later" with retry button shown | SCR-014 | FR-026 |
| UXR-607 | Error Handling | Form errors: top summary + inline per field | Submission error shows condensed summary at form top AND inline message per invalid field | All forms | UXR-007 |
| UXR-608 | Error Handling | Queue concurrent edit: toast + auto-refresh | "Schedule updated by another user — refreshing" toast; queue auto-reloads after conflict | SCR-018 | FR-020 |
| UXR-609 | Error Handling | Expired reset token shown as form error | Reset form shows "This link has expired. Request a new password reset." inline — not 404 | SCR-003 | FR-006 |
| UXR-610 | Error Handling | All empty states include contextual CTA | Every empty state includes a relevant next action (e.g., "No appointments — Book now") | All | [SOURCE:INFERRED — ACCEPTED] |

---

## Personas Summary

| Persona | Role | Primary Goals | Key Screens |
|---------|------|---------------|-------------|
| Alex (Patient) | Self-managing patient | Book / reschedule appointments; complete intake; upload documents; track slot swap | SCR-005–015 |
| Jordan (Clinical Staff) | Front-desk / care coordinator | Manage daily schedule; check in patients; review 360° view; verify AI codes | SCR-016–021 |
| Morgan (Admin) | Clinic administrator | Manage Staff and Admin user accounts; review audit log for compliance | SCR-022–023 |

---

## Information Architecture

### Site Map

```
Root /
├── /auth
│   ├── /login                    → SCR-001
│   ├── /register                 → SCR-002
│   └── /reset-password           → SCR-003
│
├── /patient                      (role: Patient)
│   ├── /dashboard                → SCR-005
│   ├── /appointments
│   │   ├── /book                 → SCR-006
│   │   ├── /confirm              → SCR-007
│   │   ├── /my-appointments      → SCR-008
│   │   ├── /waitlist             → SCR-009
│   │   ├── /slot-swap            → SCR-010
│   │   └── /calendar-sync        → SCR-015
│   ├── /intake
│   │   ├── /ai                   → SCR-011
│   │   └── /manual               → SCR-012
│   └── /documents
│       └── /upload               → SCR-014 (modal over SCR-013)
│           (list)                → SCR-013
│
├── /staff                        (role: Staff)
│   ├── /schedule                 → SCR-016
│   ├── /walk-in                  → SCR-017
│   ├── /queue                    → SCR-018
│   └── /patient/:id
│       ├── /360-view             → SCR-019
│       ├── /conflicts            → SCR-020 (slide-out panel on SCR-019)
│       └── /codes                → SCR-021
│
└── /admin                        (role: Admin)
    ├── /users                    → SCR-022
    └── /audit-log                → SCR-023

Global overlay (all authenticated routes):
    Session Timeout Warning       → SCR-004
```

### Navigation Patterns

| Role | Primary Nav | Secondary Nav | Mobile Pattern |
|------|-------------|---------------|----------------|
| Patient | Top navbar + contextual side links | Breadcrumb trail | Hamburger menu (hamburger at 768px) |
| Staff | Left sidebar — collapsible (240px expanded, 56px icon-only) | Tab bar within /patient/:id record | Hamburger at 768px |
| Admin | Left sidebar — collapsible | — | Hamburger at 768px |

---

## Screen Inventory

### Screen List

| ID | Screen Name | Role | Route | UC Basis | Desktop | Tablet | Mobile |
|----|-------------|------|-------|----------|---------|--------|--------|
| SCR-001 | Login | All | /auth/login | UC-002 | ✓ | ✓ | ✓ |
| SCR-002 | Patient Registration | Patient | /auth/register | UC-001 | ✓ | ✓ | ✓ |
| SCR-003 | Password Reset | All | /auth/reset-password | UC-002 (FR-006) | ✓ | ✓ | ✓ |
| SCR-004 | Session Timeout Warning | All | Overlay — global | UC-024 | ✓ | ✓ | ✓ |
| SCR-005 | Patient Dashboard | Patient | /patient/dashboard | Hub — [SOURCE:INFERRED — ACCEPTED] | ✓ | ✓ | ✓ |
| SCR-006 | Appointment Calendar & Booking | Patient | /patient/appointments/book | UC-004, UC-005 | ✓ | ✓ | ✓ |
| SCR-007 | Appointment Confirmation | Patient | /patient/appointments/confirm | UC-004, UC-011 | ✓ | ✓ | ✓ |
| SCR-008 | My Appointments | Patient | /patient/appointments/my-appointments | UC-007 | ✓ | ✓ | ✓ |
| SCR-009 | Waitlist Registration | Patient | /patient/appointments/waitlist | UC-005 | ✓ | ✓ | ✓ |
| SCR-010 | Slot Swap Response | Patient | /patient/appointments/slot-swap | UC-006 | ✓ | ✓ | ✓ |
| SCR-011 | AI Conversational Intake | Patient | /patient/intake/ai | UC-008 | ✓ | ✓ | ✓ |
| SCR-012 | Manual Intake Form | Patient | /patient/intake/manual | UC-009, UC-010 | ✓ | ✓ | ✓ |
| SCR-013 | My Documents | Patient | /patient/documents | UC-017 | ✓ | ✓ | ✓ |
| SCR-014 | Document Upload | Patient | /patient/documents/upload (modal) | UC-017 | ✓ | ✓ | ✓ |
| SCR-015 | Calendar Sync | Patient | /patient/appointments/calendar-sync | UC-012 | ✓ | ✓ | ✓ |
| SCR-016 | Staff Daily Schedule | Staff | /staff/schedule | UC-015, UC-016 | ✓ | ✓ | ✗ |
| SCR-017 | Walk-In Registration | Staff | /staff/walk-in | UC-013 | ✓ | ✓ | ✗ |
| SCR-018 | Same-Day Queue | Staff | /staff/queue | UC-014 | ✓ | ✓ | ✗ |
| SCR-019 | 360° Patient View | Staff | /staff/patient/:id/360-view | UC-019, UC-020 | ✓ | ✓ | ✗ |
| SCR-020 | Conflict Resolution Panel | Staff | /staff/patient/:id/conflicts (panel) | UC-019 | ✓ | ✓ | ✗ |
| SCR-021 | Medical Code Verification | Staff | /staff/patient/:id/codes | UC-021, UC-022 | ✓ | ✓ | ✗ |
| SCR-022 | User Account Management | Admin | /admin/users | UC-003 | ✓ | ✓ | ✗ |
| SCR-023 | Audit Log | Admin | /admin/audit-log | UC-023 | ✓ | ✓ | ✗ |

### Screen-to-Persona Coverage Matrix

| Screen | Alex (Patient) | Jordan (Staff) | Morgan (Admin) |
|--------|:--------------:|:--------------:|:--------------:|
| SCR-001 Login | ✓ | ✓ | ✓ |
| SCR-002 Registration | ✓ | — | — |
| SCR-003 Password Reset | ✓ | ✓ | ✓ |
| SCR-004 Session Timeout | ✓ | ✓ | ✓ |
| SCR-005 Patient Dashboard | ✓ | — | — |
| SCR-006 Booking Calendar | ✓ | — | — |
| SCR-007 Appointment Confirmation | ✓ | — | — |
| SCR-008 My Appointments | ✓ | — | — |
| SCR-009 Waitlist Registration | ✓ | — | — |
| SCR-010 Slot Swap Response | ✓ | — | — |
| SCR-011 AI Intake | ✓ | — | — |
| SCR-012 Manual Intake Form | ✓ | — | — |
| SCR-013 My Documents | ✓ | — | — |
| SCR-014 Document Upload | ✓ | — | — |
| SCR-015 Calendar Sync | ✓ | — | — |
| SCR-016 Daily Schedule | — | ✓ | — |
| SCR-017 Walk-In Registration | — | ✓ | — |
| SCR-018 Same-Day Queue | — | ✓ | — |
| SCR-019 360° Patient View | — | ✓ | — |
| SCR-020 Conflict Resolution Panel | — | ✓ | — |
| SCR-021 Medical Code Verification | — | ✓ | — |
| SCR-022 User Account Management | — | — | ✓ |
| SCR-023 Audit Log | — | — | ✓ |

### Modal / Overlay / Panel Inventory

| ID | Name | Type | Trigger | Parent Screen |
|----|------|------|---------|---------------|
| MOD-001 | Session Timeout Warning | Full-overlay modal | T−2min inactivity timer | Global (SCR-004) |
| MOD-002 | Document Upload | Modal | "Upload Document" button | SCR-013 |
| MOD-003 | Confirm Cancel Appointment | Dialog | "Cancel" action on appointment | SCR-008 |
| MOD-004 | Confirm Reschedule | Dialog | "Reschedule" action on appointment | SCR-008 |
| MOD-005 | Accept All Codes Confirmation | Dialog | "Accept All" button | SCR-021 |
| MOD-006 | Override Queue Capacity | Dialog | Same-day queue capacity limit reached | SCR-017 |
| MOD-007 | Conflict Resolution Panel | Slide-out panel | Conflict flag click on section | SCR-019 → SCR-020 |
| MOD-008 | Replace Existing Waitlist Entry | Dialog | Submit when active waitlist entry exists | SCR-009 |

---

## Content & Tone

### Voice & Tone

**Overall direction:** Clinical, direct, and reassuring. Plain English for patients; precise vocabulary for Staff and Admin. Never verbose or jargon-heavy for patient-facing content.

| Audience | Tone | Example |
|----------|------|---------|
| Patient | Calm, instructive, plain English | "Your appointment is confirmed for Monday, 14 April 2026 at 10:00 AM." |
| Staff | Terse, action-oriented, professional | "Check In · Reschedule · Mark No-Show" |
| Admin | Neutral, procedural | "User account deactivated. Reactivation requires Admin confirmation." |
| AI content | Transparent, hedged | "AI Suggested (87% confidence) — Review before committing." |

### Content Guidelines

- **Labels:** Sentence case for all UI labels ("Book appointment", not "Book Appointment")
- **Dates:** `DD Month YYYY` for display (e.g., 14 April 2026); ISO 8601 for data transfer
- **Times:** 12-hour format with AM/PM for patient-facing; 24-hour format for Staff/Admin
- **Error messages:** State what happened + what the user can do next — never "Error 422" alone
- **Empty states:** Always include a contextual CTA — never render only "No data"
- **AI labels:** Always prefix AI-generated content with "AI Suggested" — never present as authoritative fact
- **PHI:** Patient name, DOB, and appointment ID must never appear in page titles, breadcrumbs, or URL paths visible in the browser tab or address bar
- **Placeholders:** No Lorem ipsum in any screen — use realistic example data at all times

---

## Data & Edge Cases

### Data Scenarios

| ID | Scenario | Screen | Handling |
|----|----------|--------|---------|
| DS-001 | Patient has 0 appointments | SCR-005, SCR-008 | Empty state: "No upcoming appointments — Book your first appointment" + CTA |
| DS-002 | Patient has >10 upcoming appointments | SCR-008 | Paginated list (10 per page); upcoming appointments sorted first |
| DS-003 | All slots for selected month fully booked | SCR-006 | Calendar shows "No availability this month — try next month" with next-month nav |
| DS-004 | Selected slot becomes unavailable during booking | SCR-006, SCR-007 | Inline error: "This slot was just taken — please choose another" |
| DS-005 | Patient already has an active waitlist entry | SCR-009 | Replace dialog (MOD-008) before overwriting; one active entry limit enforced |
| DS-006 | Intake already submitted for appointment | SCR-011, SCR-012 | Edit mode; prior values pre-populated; audit delta logged on save |
| DS-007 | Insurance ID not found in dummy records | SCR-012 | Non-blocking warning badge: "Insurance details not verified" — submission not blocked |
| DS-008 | Uploaded document exceeds size limit | SCR-014 | Immediate rejection with specific size limit stated (e.g., "File exceeds 10 MB limit") |
| DS-009 | Document fails ClamAV virus scan | SCR-014 | "Virus detected — this file cannot be uploaded" inline error |
| DS-010 | ClamAV daemon unreachable (HTTP 503) | SCR-014 | "Document scan unavailable — please try again later" + retry button |
| DS-011 | OCR confidence score below 0.75 threshold | SCR-019 | Extracted fields marked "Low confidence — manual review required" with amber indicator |
| DS-012 | No clinical documents on file for patient | SCR-019 | Empty state: "No clinical documents — Upload a document to begin" + link to SCR-013 |
| DS-013 | Critical data conflicts detected | SCR-019 | "Requires Review" banner; "Mark as Verified" button disabled until all conflicts resolved |
| DS-014 | AI coding engine unavailable | SCR-021 | Non-blocking banner: "Code generation queued — results will appear automatically" |
| DS-015 | No CPT-mappable procedures identified | SCR-021 | CPT section shows "No procedures identified — enter codes manually if required" |
| DS-016 | Audit export exceeds 10,000 rows | SCR-023 | Async generation; banner: "Export is being prepared — you will be notified when ready" |
| DS-017 | Password reset token is expired | SCR-003 | Inline error: "This link has expired. Request a new password reset." + CTA |
| DS-018 | Two Staff users reorder queue simultaneously | SCR-018 | Conflict toast + auto-refresh; optimistic locking rejects second conflicting write |
| DS-019 | Slot swap response window has expired | SCR-010 | Page shows "This offer has expired — your waitlist entry has been cleared" |
| DS-020 | Admin attempts to modify or delete audit entry | SCR-023 | Action not available in UI; API returns HTTP 405; attempt itself logged |

### Edge Cases

| ID | Edge Case | Screen | Behaviour |
|----|-----------|--------|-----------|
| EC-001 | Patient logged in across two browser tabs | Global | Session valid in both; inactivity timer resets on activity in either tab |
| EC-002 | Confirmation email bounces after booking | SCR-007 | Patient sees success screen; Staff dashboard flags patient communication failure |
| EC-003 | OAuth consent denied for calendar sync | SCR-015 | Redirect to confirmation page with "Calendar sync declined" toast; no event created |
| EC-004 | OCR background job exhausts max retries | SCR-019 | Document flagged "Extraction failed — manual data entry required" |
| EC-005 | Staff tries to mark coding complete with unreviewed rows | SCR-021 | Action blocked; unreviewed rows highlighted |
| EC-006 | Walk-in queue at configured capacity | SCR-017 | MOD-006 override dialog shown; Staff must explicitly confirm override |
| EC-007 | Preferred slot booked by someone else before swap accepted | SCR-010 | Swap offer invalidated with message: "This slot is no longer available" |
| EC-008 | AI intake clarification loop — 3 attempts reached | SCR-011 | After 3 failed clarifications, system offers switch to manual form |
| EC-009 | Calendar API update fails on reschedule | SCR-015 | User notified to manually update their calendar event |

---

## Branding & Visual Direction

### Aesthetic Direction: Utilitarian

**Rationale:** The platform serves clinical staff operating in data-dense workflows (scheduling, medical coding, 360° patient records) and patients in high-stakes interactions (appointment booking, clinical intake). Decorative elements create cognitive noise in clinical environments. Utilitarian design prioritises legibility, information density, and fast pattern recognition over visual flair.

**Anti-patterns explicitly banned:**

- `backdrop-filter: blur()` on any element
- Purple-to-blue gradient backgrounds or fills
- Sole use of Inter or DM Sans as the only typeface (use system-ui stack)
- Lorem ipsum placeholder content in any specification or design file
- `transition` on layout properties (width, height, padding)
- Decorative background textures or patterns
- `box-shadow` card elevation (use border + surface background differentiation instead)

**Defining characteristics:**

- High information density — ≥3 data points visible per card at desktop breakpoint
- Neutral surface palette with single teal primary accent reserved for action affordances
- Border-based hierarchy instead of shadow-based elevation
- Strict 4px spacing grid with no ad-hoc spacing values
- Typography: system-ui stack with IBM Plex Sans fallback; IBM Plex Mono for code/medical codes

### Branding Assets

| Asset | Status | Notes |
|-------|--------|-------|
| Logo | Not yet created | Placeholder text: "ClinicalHub" in primary teal #0F6B6B, 20px IBM Plex Sans SemiBold |
| Favicon | Not yet created | Derive from logo mark once logo is finalised |
| Brand primary colour | #0F6B6B (deep teal) | Passes 5.9:1 contrast ratio on white (#FFFFFF) |
| Custom illustrations | Not required | Empty states use inline SVG icons only — no decorative illustrations |

---

## Component Specifications

### Component Library Reference

All components implemented as Angular 17 standalone components (`standalone: true`). No third-party UI kit dependency (no Angular Material, PrimeNG). Icon library: Heroicons (MIT licence) via `@heroicons/angular`.

### Required Components per Screen

| Screen | Required Components |
|--------|---------------------|
| SCR-001 | AppNavbar, InlineErrorMessage, EmptyState |
| SCR-002 | AppNavbar, IntakeFormFields, InlineErrorMessage |
| SCR-003 | AppNavbar, InlineErrorMessage |
| SCR-004 | SessionTimeoutModal |
| SCR-005 | AppNavbar, SideNav, AppointmentCard, StatusBadge, EmptyState |
| SCR-006 | AppNavbar, SideNav, SlotCalendarPicker, StatusBadge, EmptyState, InlineErrorMessage |
| SCR-007 | AppNavbar, SideNav, AppointmentCard, CalendarSyncButtons, PDFPreviewLink, StatusBadge |
| SCR-008 | AppNavbar, SideNav, AppointmentCard, StatusBadge, ConfirmationDialog, EmptyState |
| SCR-009 | AppNavbar, SideNav, WaitlistEntryForm, ConfirmationDialog, InlineErrorMessage |
| SCR-010 | AppNavbar, SideNav, SlotSwapResponseBanner, AppointmentCard |
| SCR-011 | AppNavbar, SideNav, ConversationalChatPanel, PageLoadSkeleton |
| SCR-012 | AppNavbar, SideNav, IntakeFormFields, InsuranceValidationIndicator, InlineErrorMessage |
| SCR-013 | AppNavbar, SideNav, VirusScanStatusBadge, EmptyState |
| SCR-014 | DocumentDropzone, VirusScanStatusBadge, InlineErrorMessage, ConfirmationDialog |
| SCR-015 | AppNavbar, SideNav, CalendarSyncButtons, InlineErrorMessage |
| SCR-016 | AppNavbar, SideNav, DailyScheduleTable, NoShowRiskBadge, StatusBadge, EmptyState |
| SCR-017 | AppNavbar, SideNav, IntakeFormFields, ConfirmationDialog, InlineErrorMessage, EmptyState |
| SCR-018 | AppNavbar, SideNav, QueueTable, StatusBadge, EmptyState |
| SCR-019 | AppNavbar, SideNav, PatientRecord360Panel, StatusBadge, PageLoadSkeleton, EmptyState |
| SCR-020 | ConflictDiffView, ConfirmationDialog, InlineErrorMessage |
| SCR-021 | AppNavbar, SideNav, CodeSuggestionRow, ConfidenceMeter, ConfirmationDialog, EmptyState |
| SCR-022 | AppNavbar, SideNav, UserManagementTable, StatusBadge, ConfirmationDialog, EmptyState |
| SCR-023 | AppNavbar, SideNav, AuditLogTable, EmptyState |

### Component Summary

| ID | Component | Type | Description |
|----|-----------|------|-------------|
| C-001 | AppNavbar | Layout | Top navigation bar; logo; role-aware links; user menu with logout |
| C-002 | SideNav | Layout | Collapsible left sidebar (240px / 56px icon-only); role-filtered nav items; active state = teal left border |
| C-003 | StatusBadge | Display | Colour + text label badge: Scheduled / Confirmed / Arrived / No-Show / Cancelled / Verified / Pending Review |
| C-004 | AppointmentCard | Display | Date + time + provider name + status badge; action links (Cancel / Reschedule / Calendar Sync) |
| C-005 | SlotCalendarPicker | Input | Month-view calendar grid; available / unavailable / selected slot states; keyboard arrow key day navigation |
| C-006 | WaitlistEntryForm | Input | Preferred slot date/time picker; existing active entry warning; Submit / Replace actions |
| C-007 | SlotSwapResponseBanner | Display | Old slot → New slot comparison; countdown timer; Accept and Decline action buttons |
| C-008 | ConversationalChatPanel | Composite | Chat bubble stream; streaming typewriter assistant output; patient input field; "Switch to manual form" link |
| C-009 | IntakeFormFields | Input | Demographics + chief complaint + PMH + medications + allergies field groups; section headings |
| C-010 | InsuranceValidationIndicator | Display | Inline badge with three states: Validated / Not Verified / Not Entered; updates on field blur |
| C-011 | DocumentDropzone | Input | Drag-and-drop + click-to-browse; file size limit displayed; accepted format list |
| C-012 | VirusScanStatusBadge | Display | Four states: Scanning… / Scan Passed / Scan Failed / Scan Unavailable — with icon |
| C-013 | CalendarSyncButtons | Action | "Add to Google Calendar" + "Add to Outlook Calendar" buttons with OAuth redirect handling |
| C-014 | DailyScheduleTable | Display | Time-sorted appointment rows; patient name; appointment type; status badge; Check In action; NoShowRiskBadge |
| C-015 | NoShowRiskBadge | Display | Amber (#D4820A) badge + ⚠ icon + "High Risk" text label; shown only when risk score = High |
| C-016 | QueueTable | Input/Display | Ordered patient queue; walk-in flag indicator; drag handle; position number input; Remove action per row |
| C-017 | PatientRecord360Panel | Composite | Collapsible sections: Vitals / Medications / Allergies / PMH / Diagnoses; conflict flag indicators per section |
| C-018 | ConflictDiffView | Display | Side-by-side field comparison: Document A value ↔ Document B value; source document name + date; "Use this value" action |
| C-019 | CodeSuggestionRow | Composite | Medical code + description + ConfidenceMeter + inline Accept / Modify / Reject actions; "AI Suggested" label |
| C-020 | ConfidenceMeter | Display | Numeric percentage + horizontal bar fill; colour thresholds: ≥80% success green, 50–79% amber, <50% error red |
| C-021 | AuditLogTable | Display | Paginated table; filterable by date range / actor / action type; Export CSV button |
| C-022 | UserManagementTable | Display | User rows; role badge; active/inactive status; Deactivate / Reactivate / Edit actions |
| C-023 | ConfirmationDialog | Overlay | Title + body text + Confirm + Cancel; destructive variant uses error red confirm button |
| C-024 | SessionTimeoutModal | Overlay | Live countdown with aria-live=assertive; Extend Session + Log Out; focus trapped; aria-modal=true |
| C-025 | PDFPreviewLink | Action | "Download PDF" link; opens confirmation PDF in new tab |
| C-026 | EmptyState | Display | SVG icon + heading + optional subtext + optional CTA button; content is contextual per screen |
| C-027 | InlineErrorMessage | Display | Field-level error text in error red (#C0392B) with icon; associated to input via aria-describedby |
| C-028 | PageLoadSkeleton | Display | Grey shimmer placeholder that matches content layout dimensions; shown during async data load |

### Component Constraints

- **No third-party UI component library** (no Angular Material, PrimeNG, or similar)
- All components: `standalone: true` (Angular 17 standalone component pattern)
- All interactive components: keyboard navigable (Tab, Enter, Space, Arrow keys as applicable)
- All status components: colour + text label — never colour-only
- `ConfidenceMeter`: three colour threshold zones — must also show numeric % for colour-blind accessibility
- `QueueTable`: drag-drop via Angular CDK DragDrop; position number input as WCAG 2.1.1 keyboard alternative
- `ConversationalChatPanel`: streaming via SSE or WebSocket; input field must remain interactive during stream
- `SessionTimeoutModal`: `aria-modal="true"`, Angular CDK `FocusTrap`, `aria-live="assertive"` on countdown element
- `DailyScheduleTable`: transitions to card list layout at 768px breakpoint

---

## Prototype Flows

### FL-001: Patient Self-Registration

**UC Basis:** UC-001
**Entry point:** SCR-001 "Create account" link
**Happy path:**

```
SCR-001 (Login)
  └─[Create account]→ SCR-002 (Patient Registration)
      ├─[Fill form → Submit]→ Email verification sent (system)
      │   └─[Email link clicked]→ SCR-001 with success toast: "Account created — please log in"
      └─[Validation errors]→ SCR-002 (inline field errors + form-top summary)
```

**States required — SCR-002:** Default · Loading (on submit) · Validation · Error (email already registered) · Success redirect

---

### FL-002: Login & Role-Based Redirect

**UC Basis:** UC-002
**Entry point:** SCR-001

```
SCR-001 (Login)
  ├─[Patient credentials valid]→ SCR-005 (Patient Dashboard)
  ├─[Staff credentials valid]→ SCR-016 (Staff Daily Schedule)
  ├─[Admin credentials valid]→ SCR-022 (User Account Management)
  ├─[Invalid credentials]→ SCR-001 (inline error — do not specify which field is wrong)
  └─[Forgot password]→ SCR-003 (Password Reset)
```

**States required — SCR-001:** Default · Loading (on submit) · Error · Validation

---

### FL-003: Password Reset

**UC Basis:** UC-002 (FR-006)
**Entry point:** SCR-001 "Forgot password?" link

```
SCR-003 Step 1 (Enter email address)
  └─[Submit email]→ "Reset link sent to your email" (inline — always shown regardless of email existence)
      └─[Email link clicked — within 60 minutes]→ SCR-003 Step 2 (Enter new password)
          ├─[Submit new password]→ SCR-001 with toast: "Password updated — please log in"
          └─[Token expired]→ SCR-003 Step 1 with inline error: "This link has expired. Request a new reset."
```

---

### FL-004: Book Available Appointment

**UC Basis:** UC-004, UC-011
**Entry point:** SCR-005 "Book Appointment" CTA

```
SCR-005 (Patient Dashboard)
  └─[Book Appointment]→ SCR-006 (Appointment Calendar & Booking)
      └─[Select available slot → Confirm]→ SCR-007 (Appointment Confirmation)
          ├─[PDF generated]→ Confirmation email sent automatically (system)
          ├─[Add to Google Calendar]→ SCR-015 → OAuth → return to SCR-007
          ├─[Add to Outlook Calendar]→ SCR-015 → OAuth → return to SCR-007
          └─[Slot taken concurrently]→ SCR-006 (inline: "This slot was just taken — please choose another")
```

**States required — SCR-006:** Default · Loading (slot load) · Empty (no slots this month) · Error (slot taken) · Validation

---

### FL-005: Join Waitlist

**UC Basis:** UC-005
**Entry point:** SCR-006 (slot selected when none available, or "Join Waitlist" link)

```
SCR-006 (Calendar — no available slots)
  └─[Join Waitlist]→ SCR-009 (Waitlist Registration)
      ├─[No existing waitlist entry → Submit]→ Success toast: "You're on the waitlist"
      └─[Existing entry exists]→ MOD-008 (Replace dialog) → [Confirm]→ Entry replaced → Success toast
```

---

### FL-006: Slot Swap Accept / Decline

**UC Basis:** UC-006
**Entry point:** Email notification deep-link → SCR-010

```
Email notification (system-generated)
  └─[Accept link OR Decline link]→ SCR-010 (Slot Swap Response) [swap context pre-loaded from token]
      ├─[Accept within window]→ SCR-007 (updated Appointment Confirmation — new slot)
      ├─[Decline]→ SCR-008 (My Appointments) with toast: "Waitlist entry cleared"
      └─[Window already expired]→ SCR-010 shows "This offer has expired — your waitlist entry has been cleared"
```

---

### FL-007: Cancel / Reschedule Appointment

**UC Basis:** UC-007
**Entry point:** SCR-008 "My Appointments"

```
SCR-008 (My Appointments)
  ├─[Cancel appointment — outside cutoff]→ MOD-003 (Confirm Cancel dialog)
  │   └─[Confirm]→ SCR-008 (appointment shows Cancelled; reminder jobs removed)
  ├─[Reschedule — outside cutoff]→ MOD-004 (Confirm Reschedule dialog)
  │   └─[Confirm]→ SCR-006 (Calendar — pick new slot)
  │       └─[Select slot]→ SCR-007 (new Appointment Confirmation)
  └─[Within cutoff window]→ Action blocked inline: "Changes are no longer accepted for this appointment — contact staff"
```

---

### FL-008: AI Conversational Intake

**UC Basis:** UC-008
**Entry point:** Patient appointment detail → "Complete Intake" → "AI-Assisted"

```
SCR-011 (AI Conversational Intake)
  ├─[Dialogue turns complete]→ Structured summary displayed inline
  │   ├─[Confirm summary]→ Intake stored → success toast
  │   └─[Edit a field]→ Re-present corrected summary → Confirm
  ├─[Ambiguous input]→ AI clarification prompt (max 3 attempts per field)
  │   └─[3rd failed clarification]→ "Shall we switch to the manual form?" offer
  └─[Switch to manual]→ SCR-012 (Manual Intake Form — entered data preserved)
```

---

### FL-009: Manual Intake Form

**UC Basis:** UC-009, UC-010
**Entry point:** Appointment detail → "Complete Intake" → "Fill in manually" (or from SCR-011 switch)

```
SCR-012 (Manual Intake Form)
  ├─[Fill all fields → Submit]→ Insurance pre-check runs inline on blur (UC-010)
  │   ├─[Insurance validated]→ "Insurance details verified" badge; intake stored → success toast
  │   ├─[Insurance not verified]→ Non-blocking warning badge; intake stored regardless
  │   └─[Insurance fields blank]→ No check run; intake stored
  └─[Required fields missing on submit]→ SCR-012 (field errors inline + summary at form top)
```

---

### FL-010: Patient Document Upload

**UC Basis:** UC-017
**Entry point:** SCR-013 "My Documents" → "Upload Document"

```
SCR-013 (My Documents)
  └─[Upload Document]→ MOD-002 (Document Upload Modal — SCR-014)
      ├─[Select / drop file → Upload initiated]→ Progress bar shown
      │   └─[Upload complete]→ "Scanning…" status shown
      │       ├─[Scan passed → Encrypted + stored]→ Modal closes; SCR-013 updated with new document
      │       ├─[Scan failed]→ Inline: "Virus detected — this file cannot be uploaded"
      │       └─[ClamAV unavailable (503)]→ "Scan service unavailable — please try again later" + Retry
      ├─[File exceeds size limit]→ Immediate inline rejection with specific limit stated
      ├─[Unsupported file format]→ Immediate inline rejection with accepted formats listed
      └─[Cancel]→ Modal closes; no upload initiated
```

---

### FL-011: Staff Schedule & Patient Check-In

**UC Basis:** UC-015, UC-016
**Entry point:** SCR-016 (Staff Daily Schedule — default after login)

```
SCR-016 (Daily Schedule)
  ├─[View appointments]→ High no-show risk rows show amber NoShowRiskBadge + "High Risk"
  ├─[Filter: High Risk only]→ Filtered schedule showing only high-risk appointments
  ├─[Check In patient]→ Status updated to "Arrived" inline; audit log entry created (no navigation)
  ├─[Mark No-Show]→ MOD-003 variant (Confirm No-Show dialog) → status updated to "No-Show"
  └─[Record outreach note]→ Inline notes field on appointment row
```

---

### FL-012: Walk-In Registration & Queue Management

**UC Basis:** UC-013, UC-014
**Entry point:** SCR-016 → "Register Walk-In" link

```
SCR-017 (Walk-In Registration)
  ├─[Search existing patient → Found]→ Patient card shown → [Confirm add]→ Added to queue
  ├─[Search → Not found]→ Empty state with "Create minimal profile" CTA
  │   └─[Create minimal profile (name + DOB + contact) → Confirm]→ Added to queue
  └─[Queue at capacity]→ MOD-006 (Override dialog) → [Confirm]→ Added to queue with override flag

SCR-018 (Same-Day Queue)
  ├─[Reorder row via drag-drop]→ New order persisted; estimated wait times recalculated
  ├─[Reorder row via position input]→ Same as drag-drop; keyboard-accessible path
  ├─[Remove patient]→ Confirmation → Queue positions compressed
  └─[Concurrent edit conflict]→ Toast: "Queue updated by another user — refreshing" + auto-reload
```

---

### FL-013: 360° View → Conflict Resolution → Verified

**UC Basis:** UC-019, UC-020
**Entry point:** SCR-016 → patient name → 360° View

```
SCR-019 (360° Patient View)
  ├─[Status: "Requires Review"]→ Conflict flag indicators visible in relevant sections
  │   └─[Click conflict flag]→ MOD-007 / SCR-020 (Conflict Resolution Panel — slide-out)
  │       ├─[Select authoritative value]→ Conflict cleared; SCR-019 section updated
  │       └─[Dismiss without resolving]→ Conflict flag retained; dismissal logged to audit
  ├─[All conflicts resolved or dismissed]→ "Mark as Verified" button enabled
  └─[Mark as Verified]→ View status = Verified; "Generate Codes" action becomes available (→ FL-014)
```

---

### FL-014: Code Generation & Verification

**UC Basis:** UC-021, UC-022
**Entry point:** SCR-019 (Verified state) → "Generate Codes"

```
SCR-021 (Medical Code Verification)
  ├─[Generate Codes triggered]→ Loading skeleton → ICD-10 rows + CPT rows appear
  │   ├─[Accept row]→ Code committed to record; row shows "Accepted" state
  │   ├─[Modify row]→ Inline edit mode → save → committed with modified value
  │   ├─[Reject row]→ Row discarded; "Enter manual code" option offered
  │   └─[Accept All]→ MOD-005 (list all codes → Confirm) → all codes committed
  ├─[Mark Coding Complete]→ Blocked if unreviewed rows remain — rows highlighted
  └─[AI engine unavailable]→ Non-blocking banner: "Code generation queued — results will appear automatically"
```

---

### FL-015: Admin Audit Log Filter & Export

**UC Basis:** UC-023
**Entry point:** SCR-023 (Audit Log — default after Admin login)

```
SCR-023 (Audit Log)
  ├─[Apply filters (date range / actor / action type)]→ Paginated results updated; filters persist across pages
  ├─[Export CSV — ≤10,000 rows]→ Immediate file download
  ├─[Export CSV — >10,000 rows]→ Async job; banner: "Export being prepared"; download link when complete
  └─[No matching entries]→ Empty state: "No records match your filters — adjust your filter criteria"
```
