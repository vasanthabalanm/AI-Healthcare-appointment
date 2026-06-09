# Wireframe Reference — Information Architecture

> **Wireframe Specification** | ClinicalHub · Unified Patient Access & Clinical Intelligence Platform  
> **Version:** 1.0 · **Fidelity:** Hi-Fi · **Date:** May 2026  
> **Template:** wireframe-reference-template.md

---

## 1. Wireframe Specification

| Attribute | Value |
|-----------|-------|
| Platform | Web (desktop-first SPA) |
| Primary viewport | 1280 px |
| Responsive breakpoints | 768 px (tablet), 375 px (mobile) |
| Accessibility standard | WCAG 2.1 Level AA |
| Aesthetic direction | Utilitarian |
| Fidelity | Hi-Fi (full colour, real data, embedded interactivity) |
| Wireframe count | 23 screens (SCR-001 – SCR-023) |
| Modal count | 8 embedded modals (MOD-001 – MOD-008) |
| Primary colour | `#0F6B6B` (teal) |
| Font stack | `system-ui, "IBM Plex Sans", -apple-system, sans-serif` |
| Grid base | 4 px |

---

## 2. System Overview

ClinicalHub is a greenfield, OSS-only healthcare platform serving three user roles:

| Role | Colour accent | Primary workflows |
|------|--------------|-------------------|
| **Patient** | Teal (`#0F6B6B`) | Book → Intake → Documents → My Appointments |
| **Staff** (Jordan Chen) | Purple (`#5A0A7A`) | Schedule → Check-In → Queue → 360° View → Code Verification |
| **Admin** (Morgan Blake) | Slate (`#2C3E50`) | User Accounts → Audit Log |

### Key domain constraints
- **Trust-First**: No AI output committed without explicit staff `verified_by` action
- **PHI-safe rendering**: No patient data exposed in browser history URLs
- **Immutable audit log**: All actions recorded; no entries can be edited or deleted
- **Virus scan before OCR**: ClamAV scan gates all document processing
- **No-show risk**: Staff schedule highlights high-risk patients with amber badge + text label

---

## 3. Wireframe References

| Screen ID | File | UC Basis | Status |
|-----------|------|----------|--------|
| SCR-001 | `wireframe-SCR-001-login.html` | UC-001 | ✅ |
| SCR-002 | `wireframe-SCR-002-patient-registration.html` | UC-002 | ✅ |
| SCR-003 | `wireframe-SCR-003-password-reset.html` | UC-003 | ✅ |
| SCR-004 | Embedded in SCR-005 (session timeout overlay MOD-001) | — | ✅ |
| SCR-005 | `wireframe-SCR-005-patient-dashboard.html` | UC-001 | ✅ |
| SCR-006 | `wireframe-SCR-006-booking-calendar.html` | UC-004, UC-005 | ✅ |
| SCR-007 | `wireframe-SCR-007-appointment-confirmation.html` | UC-004, UC-011 | ✅ |
| SCR-008 | `wireframe-SCR-008-my-appointments.html` | UC-007 | ✅ |
| SCR-009 | `wireframe-SCR-009-waitlist-registration.html` | UC-005 | ✅ |
| SCR-010 | `wireframe-SCR-010-slot-swap-response.html` | UC-006 | ✅ |
| SCR-011 | `wireframe-SCR-011-ai-intake.html` | UC-008 | ✅ |
| SCR-012 | `wireframe-SCR-012-manual-intake-form.html` | UC-009, UC-010 | ✅ |
| SCR-013 | `wireframe-SCR-013-my-documents.html` | UC-017 | ✅ |
| SCR-014 | Embedded as MOD-002 in SCR-013 | UC-017 | ✅ |
| SCR-015 | `wireframe-SCR-015-calendar-sync.html` | UC-012 | ✅ |
| SCR-016 | `wireframe-SCR-016-staff-daily-schedule.html` | UC-015, UC-016 | ✅ |
| SCR-017 | `wireframe-SCR-017-walkin-registration.html` | UC-013 | ✅ |
| SCR-018 | `wireframe-SCR-018-same-day-queue.html` | UC-014 | ✅ |
| SCR-019 | `wireframe-SCR-019-360-patient-view.html` | UC-019, UC-020 | ✅ |
| SCR-020 | Embedded as slide-out panel in SCR-019 | UC-019 | ✅ |
| SCR-021 | `wireframe-SCR-021-code-verification.html` | UC-021, UC-022 | ✅ |
| SCR-022 | `wireframe-SCR-022-user-management.html` | UC-003 | ✅ |
| SCR-023 | `wireframe-SCR-023-audit-log.html` | UC-023 | ✅ |

**Supporting files:**
- `data/sample-data.json` — all realistic sample entities
- `Hi-Fi/` — all 23 HTML wireframe files (self-contained, no external deps)

---

## 4. User Personas & Flows

### Persona 1: Alex Rivera (Patient)
- DOB: 14 Jul 1985 · ID: usr-001
- Insurance: BlueCross BlueShield BCB-0042891 (verified)
- Allergies: Penicillin (severe), Sulfa drugs (rash)
- Medications: Lisinopril 10mg, Metformin 500mg BID, Ibuprofen 400mg PRN
- Primary journey: Login → Dashboard → Book → Confirm → Intake → Documents

### Persona 2: Jordan Chen (Staff)
- ID: usr-010 · Role: Staff
- Primary journey: Login → Daily Schedule → Check-In → 360° Patient View → Code Verification

### Persona 3: Morgan Blake (Admin)
- ID: usr-020 · Role: Admin
- Primary journey: Login → User Accounts → Audit Log

### Prototype Flows
| Flow ID | Name | Start | End | Key branching |
|---------|------|-------|-----|---------------|
| FL-001 | New patient registration | SCR-001 | SCR-002 → SCR-005 | Email already exists → error |
| FL-002 | Password reset | SCR-001 | SCR-003 → Step 3 | Token expired → error |
| FL-003 | Session timeout | Any screen | SCR-004 overlay → SCR-001 | Extend or logout |
| FL-004 | Book appointment | SCR-005 | SCR-006 → SCR-007 → SCR-015 | No slots → FL-005; slot taken → error |
| FL-005 | Join waitlist | SCR-006 | SCR-009 → success | Existing entry → MOD-008 replace |
| FL-006 | Slot swap response | Notification | SCR-010 → SCR-007 or SCR-008 | Offer expired → clear waitlist |
| FL-007 | Cancel/reschedule | SCR-008 | MOD-003/MOD-004 → SCR-006 | Within cutoff → blocked |
| FL-008 | AI intake | SCR-007 | SCR-011 → SCR-005 | Switch to manual → FL-009 |
| FL-009 | Manual intake | SCR-012 | SCR-005 | Insurance not verified → warning |
| FL-010 | Document upload | SCR-013 | MOD-002 → SCR-013 updated | Virus detected → error; too large → error |
| FL-011 | Staff daily schedule | SCR-016 | Inline check-in → status update | High-risk filter toggle |
| FL-012 | Walk-in + queue | SCR-017 | SCR-018 | Patient not found → minimal profile; at capacity → MOD-006 |
| FL-013 | 360° view + conflict | SCR-019 | SCR-020 slide-out → resolved | Dismissed → audit logged |
| FL-014 | Code verification | SCR-021 | Mark complete → return | Low confidence → manual review flag |
| FL-015 | Audit log export | SCR-023 | CSV download | >10k rows → async |

---

## 5. Screen Hierarchy

```
ClinicalHub
├── Public (unauthenticated)
│   ├── SCR-001  Login
│   ├── SCR-002  Patient registration
│   └── SCR-003  Password reset
│
├── Patient (role: patient)
│   ├── SCR-005  Patient dashboard  ◀ hub
│   ├── SCR-006  Booking calendar
│   ├── SCR-007  Appointment confirmation
│   ├── SCR-008  My appointments
│   ├── SCR-009  Waitlist registration
│   ├── SCR-010  Slot swap response
│   ├── SCR-011  AI conversational intake
│   ├── SCR-012  Manual intake form
│   ├── SCR-013  My documents
│   └── SCR-015  Calendar sync
│
├── Staff (role: staff)
│   ├── SCR-016  Daily schedule  ◀ hub
│   ├── SCR-017  Walk-in registration
│   ├── SCR-018  Same-day queue
│   ├── SCR-019  360° patient view
│   │   └── SCR-020  Conflict resolution panel (slide-out)
│   └── SCR-021  Medical code verification
│
└── Admin (role: admin)
    ├── SCR-022  User account management  ◀ hub
    └── SCR-023  Audit log
```

---

## 6. Navigation Architecture

### Shared chrome
- **Navbar** (56px, fixed): Logo, user chip, role badge, sign-out
- **Sidebar** (240px, fixed, role-scoped): Role-appropriate nav items with active state
- **Main content** (fluid, `margin-left: 240px`, `margin-top: 56px`)

### Patient sidebar items
1. Dashboard (SCR-005)
2. Book Appointment (SCR-006) — active on SCR-006, SCR-007, SCR-009
3. My Appointments (SCR-008)
4. My Documents (SCR-013)

### Staff sidebar items
1. Daily Schedule (SCR-016)
2. Walk-In Registration (SCR-017)
3. Same-Day Queue (SCR-018)
4. 360° Patient View / Code Verification (SCR-019, SCR-021)

### Admin sidebar items
1. User Accounts (SCR-022)
2. Audit Log (SCR-023)

### Active state rules
- `border-left: 3px solid #0F6B6B` + `background: #E6F2F2` + `color: #0F6B6B` + `font-weight: 500`
- `aria-current="page"` on active anchor

---

## 7. Interaction Patterns

| Pattern | Screens | Implementation |
|---------|---------|----------------|
| Date picker / calendar | SCR-006 | Custom CSS grid, `aria-pressed` on day cells |
| Slot selection | SCR-006 | Button group with `selected` class toggle |
| Modal overlay | SCR-008, SCR-009, SCR-013, SCR-017, SCR-021, SCR-022 | `display:none` → `display:flex`; `aria-modal="true"` |
| Slide-out panel | SCR-019/020 | `transform: translateX(100%)` → `translateX(0)`, `margin-right: 420px` on main content |
| Countdown timer | SCR-010 | `setInterval` decrements seconds, auto-triggers expired state |
| Typewriter / streaming | SCR-011 | JS-driven `appendMsg()` with typing indicator |
| Inline check-in | SCR-016 | Button replaced with "Arrived" badge after click |
| Drag-drop queue | SCR-018 | `draggable="true"` + `position` number inputs (keyboard alternative) |
| Conflict diff | SCR-020 | Side-by-side diff cards; "Use this value" buttons resolve and update parent |
| Insurance validation | SCR-012 | `onblur` validates both fields; indicator cycles Verified/Pending/None |
| File upload progress | SCR-013, MOD-002 | `setInterval` simulates upload + scan; ClamAV pass/fail states |
| Password strength | SCR-002 | 4-bar meter; colours Red/Amber/Yellow/Green; text label |
| Toggle switch | SCR-016 | Custom CSS toggle; `role="switch"`, `aria-checked` |

---

## 8. Error Handling

| Error type | Screen | Pattern |
|------------|--------|---------|
| Login failed | SCR-001 | Inline `role="alert"` banner (generic — no credential leak) |
| Email exists | SCR-002 | Field-level error + banner |
| Token expired | SCR-003 | Full-page error state with re-request link |
| Slot taken | SCR-006 | `aria-live="assertive"` banner at top |
| No slots this month | SCR-006 | Slot panel replaced with empty state + waitlist CTA |
| Document too large | MOD-002 | `role="alert"` inside modal; max 10 MB stated upfront |
| Unsupported format | MOD-002 | Same pattern; PDF-only stated upfront |
| Virus detected | MOD-002 | Error state; document discarded; no processing |
| ClamAV unavailable | MOD-002 | 503 error state; upload blocked |
| Slot swap expired | SCR-010 | Full-page expired state with waitlist CTA |
| Queue concurrent edit | SCR-018 | `aria-live="assertive"` amber toast at top |
| Capacity override | SCR-017 | MOD-006 warning; action logged |
| Coding incomplete | SCR-021 | "Mark complete" button disabled; pending count indicator |
| Session timeout | SCR-004 | Overlay with 30-second countdown; "Extend" or "Sign out" |

---

## 9. Responsive Strategy

### Breakpoint rules
| Breakpoint | Width | Changes |
|------------|-------|---------|
| Desktop (primary) | 1280 px | Full sidebar (240px) + calendar grid + split layouts |
| Tablet | 768 px | Sidebar hidden; single-column layouts; touch targets ≥44px |
| Mobile | 375 px | Full-width stacked layout; no horizontal scroll |

### Staff/Admin screen policy
SCR-016 through SCR-023 are desktop-only (clinical workflow tools). Mobile layout not required per UXR-304.

### Sidebar on mobile
`@media (max-width: 768px) { .sidebar { display: none; } }` — replaced by hamburger menu in implementation.

---

## 10. Accessibility

| Requirement | Implementation |
|-------------|---------------|
| Focus rings | `box-shadow: 0 0 0 3px rgba(15,107,107,0.35)` on `:focus-visible` |
| ARIA live regions | `aria-live="polite"` for status updates; `aria-live="assertive"` for errors |
| Modal | `role="dialog"`, `aria-modal="true"`, `aria-labelledby` |
| Tables | `role="table/row/cell/columnheader"` on styled grid layouts |
| Status badges | Colour + text always (never colour-only, per UXR-405) |
| AI labels | "AI Suggested" text + icon on all AI-generated content (UXR-008) |
| Calendar | `role="grid"`, `aria-label`, `aria-pressed` on day cells |
| Forms | `aria-required="true"`, `aria-describedby` for errors |
| Toggle switches | `role="switch"`, `aria-checked` |
| Session timeout | `role="timer"`, countdown read by screen readers |
| Skip links | `#main-content` target on all pages |
| Immutable notice | `role="note"` on audit log footer |
| Touch targets | Minimum 44×44 px at 375px breakpoint |
| Colour contrast | All text ≥ 4.5:1 against surface (WCAG AA verified) |

---

## 11. Content Strategy

### Text principles
- **No lorem ipsum** — all content uses realistic sample data from `data/sample-data.json`
- **Action-oriented labels** — "Confirm booking", "Check In", "Accept offered slot"
- **Error messages explain cause and remedy** — "This slot was just taken — please choose another"
- **Trust-First messaging** — "No AI output is committed without your explicit action"

### Sample data entities used
- Patients: Alex Rivera, Maria Santos, Derek Okafor, Priya Nair, James Whitmore
- Staff: Jordan Chen (usr-010), Samira Haddad (usr-011)
- Admin: Morgan Blake (usr-020), Taylor Kim (usr-021, inactive)
- Appointments: apt-001 (Confirmed 20 May), apt-002 (Arrived 10 Mar), apt-003/004/005 (staff schedule), apt-006 (Cancelled Jan)
- Conflict: cnf-001 — Lisinopril 10mg (doc-001) vs 20mg (doc-002)
- Codes: M54.5 (91%), E11.65 (87%), I10 (95%), 99214 (72%), 97110 (48%)
- Insurance: BlueCross BlueShield BCB-0042891 (verified)

### Tone
Clinical, precise, non-alarming. Avoid jargon for patient-facing screens. Clinical shorthand acceptable for staff/admin screens.
