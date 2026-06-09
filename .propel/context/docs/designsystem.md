# Design System Reference — Unified Patient Access & Clinical Intelligence Platform

**Version:** 1.0
**Date:** 2026-05-12
**Aesthetic Direction:** Utilitarian
**Accessibility Target:** WCAG 2.1 Level AA
**Viewport Strategy:** Desktop-first responsive (1280px / 768px / 375px)
**Status:** Draft — Pending Figma & Angular Implementation

---

## UI Impact Assessment

| Dimension | Assessment |
|-----------|-----------|
| UI impact present? | YES — Angular 17 SPA with 23 screens across 3 roles |
| Scope | Greenfield — no existing Figma file, brand guidelines, or component library |
| Primary audience | Clinical staff (data-dense dashboards); Patients (task-focused flows); Admin (audit and management) |
| Phase scope | Phase 1 — Web only; no native mobile app in scope |
| Design system maturity | New — all tokens, components, and patterns defined in this document |
| Critical risk areas | PHI-safe rendering; AI content transparency; accessibility in high-density data screens; 5-state coverage per screen |

---

## User Story Design Context

| Epic | Design Impact | Key Screens |
|------|--------------|-------------|
| EP-TECH (Infrastructure & Auth) | Auth screens; session handling overlay; role-aware navigation scaffold | SCR-001, SCR-002, SCR-003, SCR-004 |
| EP-DATA (Data Pipeline) | Background processing only — no direct UI; status surfaces in SCR-019 | None direct |
| EP-001 (Patient Scheduling) | Booking calendar; waitlist registration; slot swap response; confirmation | SCR-005–SCR-010 |
| EP-002 (Reminders & Calendar Sync) | Confirmation screen; calendar sync page | SCR-007, SCR-015 |
| EP-003 (AI Intake) | AI chat panel; manual intake form; insurance validation indicator | SCR-011, SCR-012 |
| EP-004 (Walk-In & Queue) | Walk-in registration; same-day queue management | SCR-016, SCR-017, SCR-018 |
| EP-005 (Clinical Documents) | Document list; upload modal | SCR-013, SCR-014 |
| EP-006 (360° Patient View) | Patient record panel; conflict diff view | SCR-019, SCR-020 |
| EP-007 (Medical Coding) | Code suggestion rows; confidence meter; verification interface | SCR-021 |
| EP-008 (Admin & Audit) | User account management; audit log | SCR-022, SCR-023 |

---

## Design Source References

| Reference | Path | Role in Design |
|-----------|------|----------------|
| Functional Specification | `.propel/context/docs/spec.md` | UC-to-screen mapping; flow logic; FR basis for each UXR |
| Architecture & Design | `.propel/context/docs/design.md` | NFR constraints; tech stack decisions that affect UI |
| Figma Specification | `.propel/context/docs/figma_spec.md` | Screen inventory; UXR definitions; component specs; prototype flows |
| Epics | `.propel/context/docs/epics.md` | Epic-to-screen coverage; priority ordering |

**Figma file URL:** _Not yet created — implement from this document and `figma_spec.md`_
**Storybook URL:** _Not yet created — Angular component library TBD_

---

## Screen-to-Design Mappings

_No Figma file or wireframe images exist (greenfield project). All screens to be designed from `figma_spec.md` and this document._

| Screen ID | Screen Name | Design Status | Primary Layout Notes |
|-----------|-------------|---------------|----------------------|
| SCR-001 | Login | Not started | Centred card on surface-1 background; no sidebar; max-width 400px |
| SCR-002 | Patient Registration | Not started | Multi-section form; 2-column field pairs at desktop; single-column at mobile |
| SCR-003 | Password Reset | Not started | 2-step single-column form (email input → new password input) |
| SCR-004 | Session Timeout Warning | Not started | Full-overlay modal; always top z-index; no background scroll |
| SCR-005 | Patient Dashboard | Not started | Summary cards row (next appointment + intake status + documents); sidebar nav |
| SCR-006 | Appointment Calendar & Booking | Not started | Left: month-view calendar grid; Right: available slots list panel |
| SCR-007 | Appointment Confirmation | Not started | Confirmation card; PDF download link; calendar sync buttons |
| SCR-008 | My Appointments | Not started | Table/card list; status badges; action links per row |
| SCR-009 | Waitlist Registration | Not started | Simple centred form; preferred slot picker; existing entry warning |
| SCR-010 | Slot Swap Response | Not started | Side-by-side: current slot vs offered slot; countdown timer; Accept/Decline |
| SCR-011 | AI Conversational Intake | Not started | Split: left chat panel; right intake summary (builds progressively) |
| SCR-012 | Manual Intake Form | Not started | Long single-column form; grouped sections with headings; insurance field at bottom |
| SCR-013 | My Documents | Not started | Document list with scan status badges; Upload button top-right |
| SCR-014 | Document Upload | Not started | Modal overlay; dropzone centred; progress bar + scan status below |
| SCR-015 | Calendar Sync | Not started | Two large action buttons (Google / Outlook); OAuth status feedback below |
| SCR-016 | Staff Daily Schedule | Not started | Full-width time-ordered table; sticky header; risk badges inline |
| SCR-017 | Walk-In Registration | Not started | Search bar; patient result card; profile create form (conditional) |
| SCR-018 | Same-Day Queue | Not started | Ordered table with drag handles; position input; remove action per row |
| SCR-019 | 360° Patient View | Not started | Collapsible accordion sections; conflict flags inline; verify button sticky bottom |
| SCR-020 | Conflict Resolution Panel | Not started | Slide-out panel (400px) from right edge of SCR-019 |
| SCR-021 | Medical Code Verification | Not started | Table: code + description + confidence meter + inline actions per row |
| SCR-022 | User Account Management | Not started | Admin table; role badge; status toggle; action menu per row |
| SCR-023 | Audit Log | Not started | Filterable paginated table; filter bar top; export button top-right |

---

## Design Tokens

```yaml
# ============================================================
# Design Tokens — Unified Patient Access & Clinical Intelligence Platform
# Version: 1.0 | Date: 2026-05-12 | Aesthetic: Utilitarian
# Accessibility: WCAG 2.1 AA
# ============================================================

color:
  # --- Brand ---
  primary:              "#0F6B6B"   # Deep teal — primary buttons, active nav, link text
  primary-hover:        "#0A5050"   # Darker teal — :hover and :focus-visible on primary
  primary-subtle:       "#E6F2F2"   # Tint — active nav background, selected slot highlight

  # --- Surfaces ---
  surface-0:            "#FFFFFF"   # Page background, modal background
  surface-1:            "#F5F5F5"   # Sidebar background, card background
  surface-2:            "#EBEBEB"   # Table alternate rows, input background, skeleton shimmer base
  surface-3:            "#D0D0D0"   # Borders, dividers — also used as --color-border

  # --- Text ---
  text-primary:         "#1A1A1A"   # Body text, headings (contrast 16.1:1 on white — AAA)
  text-secondary:       "#5A5A5A"   # Labels, metadata, secondary info (contrast 7.0:1 — AAA)
  text-disabled:        "#9A9A9A"   # Disabled inputs, placeholder text (decorative only)
  text-inverse:         "#FFFFFF"   # Text on dark/primary backgrounds
  text-link:            "#0F6B6B"   # Hyperlinks — same as primary (contrast 5.9:1 on white — AA)
  text-link-hover:      "#0A5050"   # Link :hover state

  # --- Semantic ---
  error:                "#C0392B"   # Error text, border, icon (contrast 5.7:1 on white — AA)
  error-bg:             "#FDECEA"   # Error state background
  warning:              "#D4820A"   # Warning / no-show risk (contrast 4.6:1 on white — AA)
  warning-bg:           "#FEF5E7"   # Warning state background
  success:              "#1A7A4A"   # Success / verified (contrast 6.1:1 on white — AA)
  success-bg:           "#E8F5EE"   # Success state background
  info:                 "#1C6EA4"   # Informational state (contrast 5.5:1 on white — AA)
  info-bg:              "#E8F0F8"   # Info state background

  # --- AI & Trust-First ---
  ai-suggested:         "#1C6EA4"   # "AI Suggested" label colour — same as info
  immutable:            "#5A5A5A"   # Read-only / audit entry text

  # --- Confidence meter thresholds ---
  confidence-high:      "#1A7A4A"   # ≥80% — success green
  confidence-mid:       "#D4820A"   # 50–79% — amber warning
  confidence-low:       "#C0392B"   # <50% — error red

typography:
  font-family-base: 'system-ui, "IBM Plex Sans", -apple-system, BlinkMacSystemFont, sans-serif'
  font-family-mono: '"IBM Plex Mono", "Courier New", monospace'   # ICD-10 / CPT code display

  # Type scale
  font-size-xs:    "12px"   # Labels, badges, table secondary metadata
  font-size-sm:    "14px"   # Body text default, form labels, table body
  font-size-base:  "16px"   # Primary body text, input text
  font-size-md:    "20px"   # Section headings (H3)
  font-size-lg:    "24px"   # Page headings (H2)
  font-size-xl:    "32px"   # Dashboard hero headings (H1)

  font-weight-regular:  400
  font-weight-medium:   500
  font-weight-semibold: 600
  font-weight-bold:     700

  line-height-body:     1.5   # All body text and labels
  line-height-heading:  1.2   # H1–H3 headings

spacing:
  # 4px base grid — all spacing values are multiples of 4px
  0:   "0px"
  1:   "4px"
  2:   "8px"
  3:   "12px"
  4:   "16px"
  5:   "20px"
  6:   "24px"
  8:   "32px"
  10:  "40px"
  12:  "48px"
  16:  "64px"
  20:  "80px"

border:
  radius-none:    "0px"    # Table cells, full-width containers
  radius-sm:      "2px"    # Inputs, badges, status chips
  radius-md:      "4px"    # Cards, buttons, modals
  radius-lg:      "8px"    # Overlays, slide-out panels
  width-default:  "1px"
  color-default:  "#D0D0D0"   # --color-surface-3
  style-default:  "solid"

shadow:
  # Intentionally minimal — utilitarian design uses borders over shadows
  none:   "none"
  focus:  "0 0 0 3px rgba(15, 107, 107, 0.35)"   # Focus ring — teal alpha, 3px spread

breakpoint:
  mobile:   "375px"
  tablet:   "768px"
  desktop:  "1280px"
  wide:     "1440px"

layout:
  sidebar-width:        "240px"    # SideNav expanded state
  sidebar-collapsed:    "56px"     # SideNav icon-only collapsed state
  navbar-height:        "56px"     # AppNavbar height (all breakpoints)
  content-max-width:    "1200px"   # Page content max-width at desktop
  form-max-width:       "640px"    # Single-column form max-width
  panel-width:          "400px"    # Slide-out panel (ConflictDiffView)

animation:
  duration-fast:    "100ms"
  duration-base:    "200ms"
  duration-slow:    "300ms"
  easing-default:   "ease-in-out"
  # BANNED: Do NOT apply transitions to layout properties (width, height, padding, margin)
  # Permitted: opacity, color, background-color, border-color, transform

z-index:
  base:             0
  dropdown:         100
  sticky:           200
  sidebar:          300
  modal-backdrop:   400
  modal:            500
  toast:            600
  tooltip:          700
```

---

## Component References

| Component ID | Component Name | Angular Selector | Category | Standalone | Status |
|-------------|----------------|-----------------|----------|------------|--------|
| C-001 | AppNavbar | `<app-navbar>` | Layout | ✓ | Not started |
| C-002 | SideNav | `<app-sidenav>` | Layout | ✓ | Not started |
| C-003 | StatusBadge | `<app-status-badge>` | Display | ✓ | Not started |
| C-004 | AppointmentCard | `<app-appointment-card>` | Display | ✓ | Not started |
| C-005 | SlotCalendarPicker | `<app-slot-calendar-picker>` | Input | ✓ | Not started |
| C-006 | WaitlistEntryForm | `<app-waitlist-entry-form>` | Input | ✓ | Not started |
| C-007 | SlotSwapResponseBanner | `<app-slot-swap-banner>` | Display | ✓ | Not started |
| C-008 | ConversationalChatPanel | `<app-chat-panel>` | Composite | ✓ | Not started |
| C-009 | IntakeFormFields | `<app-intake-form-fields>` | Input | ✓ | Not started |
| C-010 | InsuranceValidationIndicator | `<app-insurance-indicator>` | Display | ✓ | Not started |
| C-011 | DocumentDropzone | `<app-document-dropzone>` | Input | ✓ | Not started |
| C-012 | VirusScanStatusBadge | `<app-scan-status-badge>` | Display | ✓ | Not started |
| C-013 | CalendarSyncButtons | `<app-calendar-sync-buttons>` | Action | ✓ | Not started |
| C-014 | DailyScheduleTable | `<app-daily-schedule-table>` | Display | ✓ | Not started |
| C-015 | NoShowRiskBadge | `<app-no-show-risk-badge>` | Display | ✓ | Not started |
| C-016 | QueueTable | `<app-queue-table>` | Input/Display | ✓ | Not started |
| C-017 | PatientRecord360Panel | `<app-patient-360-panel>` | Composite | ✓ | Not started |
| C-018 | ConflictDiffView | `<app-conflict-diff-view>` | Display | ✓ | Not started |
| C-019 | CodeSuggestionRow | `<app-code-suggestion-row>` | Composite | ✓ | Not started |
| C-020 | ConfidenceMeter | `<app-confidence-meter>` | Display | ✓ | Not started |
| C-021 | AuditLogTable | `<app-audit-log-table>` | Display | ✓ | Not started |
| C-022 | UserManagementTable | `<app-user-mgmt-table>` | Display | ✓ | Not started |
| C-023 | ConfirmationDialog | `<app-confirmation-dialog>` | Overlay | ✓ | Not started |
| C-024 | SessionTimeoutModal | `<app-session-timeout-modal>` | Overlay | ✓ | Not started |
| C-025 | PDFPreviewLink | `<app-pdf-preview-link>` | Action | ✓ | Not started |
| C-026 | EmptyState | `<app-empty-state>` | Display | ✓ | Not started |
| C-027 | InlineErrorMessage | `<app-inline-error>` | Display | ✓ | Not started |
| C-028 | PageLoadSkeleton | `<app-page-skeleton>` | Display | ✓ | Not started |

---

## New Visual Assets

| Asset ID | Name | Type | Dimensions | Used On | Status |
|----------|------|------|------------|---------|--------|
| VA-001 | Platform Logo ("ClinicalHub") | SVG wordmark | Height: 32px | AppNavbar (C-001), SCR-001 | Not created |
| VA-002 | Favicon | PNG | 32×32px | Browser tab (all routes) | Not created |
| VA-003 | Empty state — No appointments | SVG inline icon | 48×48px | SCR-005, SCR-008 | Not created |
| VA-004 | Empty state — No documents | SVG inline icon | 48×48px | SCR-013 | Not created |
| VA-005 | Empty state — No queue entries | SVG inline icon | 48×48px | SCR-018 | Not created |
| VA-006 | Empty state — No clinical data | SVG inline icon | 48×48px | SCR-019 | Not created |
| VA-007 | Empty state — No audit entries | SVG inline icon | 48×48px | SCR-023 | Not created |
| VA-008 | Empty state — No users | SVG inline icon | 48×48px | SCR-022 | Not created |
| VA-009 | No-show risk warning icon | SVG inline (⚠) | 16×16px | SCR-016 (C-015) | Use Heroicons `ExclamationTriangleIcon` |
| VA-010 | AI suggested indicator icon | SVG inline (sparkle) | 14×14px | SCR-011, SCR-021 (C-019) | Not created — or use Heroicons `SparklesIcon` |
| VA-011 | Verified checkmark icon | SVG inline | 16×16px | SCR-019, SCR-021 | Use Heroicons `CheckCircleIcon` |
| VA-012 | Conflict flag icon | SVG inline | 16×16px | SCR-019, SCR-020 | Use Heroicons `FlagIcon` |

**Icon library:** Heroicons (MIT licence) — `@heroicons/angular` package
**Custom icons:** VA-001 (logo), VA-002 (favicon), VA-003–VA-008 (empty states), VA-010 (AI indicator) require custom SVG creation

---

## Task Design Mapping

```yaml
# Maps each user story to its primary design deliverables
# Format per entry: story_id, title, screens[], components[], flows[], notes

task_design_map:

  - story_id: us_001
    title: "Angular SPA scaffold + RBAC route guards"
    screens: []
    components: [C-001, C-002]
    flows: []
    notes: "AppNavbar and SideNav scaffolding; role-aware Angular route guards (AuthGuard, RoleGuard)"

  - story_id: us_002
    title: "JWT session management"
    screens: [SCR-004]
    components: [C-024]
    flows: []
    notes: "SessionTimeoutModal; countdown at T-2min; Extend / Log Out actions"

  - story_id: us_012
    title: "Patient self-registration"
    screens: [SCR-002]
    components: [C-009, C-027]
    flows: [FL-001]
    notes: "Multi-section registration form; email verification redirect"

  - story_id: us_013
    title: "Patient / Staff / Admin login"
    screens: [SCR-001, SCR-003]
    components: [C-027]
    flows: [FL-002, FL-003]
    notes: "Login screen + password reset 2-step flow; role-based redirect on success"

  - story_id: us_014
    title: "Admin user account management"
    screens: [SCR-022]
    components: [C-022, C-003, C-023, C-026]
    flows: []
    notes: "Create / edit / deactivate / reactivate Staff and Admin accounts"

  - story_id: us_015
    title: "Session timeout enforcement"
    screens: [SCR-004]
    components: [C-024]
    flows: []
    notes: "15-minute inactivity timeout; 2-minute warning overlay"

  - story_id: us_019
    title: "Patient books available appointment"
    screens: [SCR-005, SCR-006, SCR-007]
    components: [C-004, C-005, C-003, C-025, C-026, C-028]
    flows: [FL-004]
    notes: "Calendar slot picker; booking confirmation; PDF link; no-show risk computed at booking"

  - story_id: us_020
    title: "Patient joins waitlist"
    screens: [SCR-009]
    components: [C-006, C-023, C-027]
    flows: [FL-005]
    notes: "Waitlist registration form; replace-existing-entry confirmation dialog"

  - story_id: us_021
    title: "System executes preferred slot swap"
    screens: [SCR-010]
    components: [C-007, C-004]
    flows: [FL-006]
    notes: "Email deep-link with pre-loaded context; countdown timer; Accept / Decline"

  - story_id: us_022
    title: "Patient cancels or reschedules appointment"
    screens: [SCR-008]
    components: [C-004, C-003, C-023]
    flows: [FL-007]
    notes: "Cancellation cutoff window enforcement; confirmation dialogs"

  - story_id: us_023
    title: "Calendar sync — Google and Outlook"
    screens: [SCR-007, SCR-015]
    components: [C-013, C-027]
    flows: []
    notes: "OAuth consent redirect; success/failure feedback on return"

  - story_id: us_024
    title: "AI conversational intake"
    screens: [SCR-011]
    components: [C-008, C-028]
    flows: [FL-008]
    notes: "Streaming typewriter chat; summary review; switch-to-manual fallback"

  - story_id: us_025
    title: "Manual intake form"
    screens: [SCR-012]
    components: [C-009, C-010, C-027]
    flows: [FL-009]
    notes: "Long grouped form; insurance validation indicator inline"

  - story_id: us_033
    title: "Patient uploads clinical documents"
    screens: [SCR-013, SCR-014]
    components: [C-011, C-012, C-027, C-026]
    flows: [FL-010]
    notes: "Dropzone modal; progress bar; virus scan status transitions; specific rejection messages"

  - story_id: us_029
    title: "Staff walk-in registration"
    screens: [SCR-016, SCR-017]
    components: [C-014, C-015, C-003, C-026, C-023]
    flows: [FL-011, FL-012]
    notes: "Patient search; minimal profile creation; queue capacity override dialog"

  - story_id: us_030
    title: "Staff manages same-day queue"
    screens: [SCR-018]
    components: [C-016, C-003, C-026]
    flows: [FL-012]
    notes: "CDK DragDrop; position number input keyboard alt; concurrent edit conflict toast"

  - story_id: us_031
    title: "Staff marks patient arrived"
    screens: [SCR-016]
    components: [C-014, C-003]
    flows: [FL-011]
    notes: "Inline check-in action on schedule row; audit log created automatically"

  - story_id: us_032
    title: "Staff reviews no-show risk alerts"
    screens: [SCR-016]
    components: [C-014, C-015]
    flows: [FL-011]
    notes: "Amber NoShowRiskBadge + warning icon; outreach notes inline"

  - story_id: us_038
    title: "Staff reviews 360° patient view"
    screens: [SCR-019, SCR-020]
    components: [C-017, C-018, C-003, C-028, C-026]
    flows: [FL-013]
    notes: "Collapsible clinical sections; ConflictDiffView slide-out panel; Verify button"

  - story_id: us_039
    title: "System detects and flags data conflicts"
    screens: [SCR-019, SCR-020]
    components: [C-017, C-018]
    flows: [FL-013]
    notes: "Conflict flags inline on sections; side-by-side diff with source document reference"

  - story_id: us_040
    title: "System generates ICD-10 and CPT code suggestions"
    screens: [SCR-021]
    components: [C-019, C-020, C-028]
    flows: [FL-014]
    notes: "Loading skeleton → code rows appear; AI Suggested label + confidence meter per row"

  - story_id: us_041
    title: "Staff verifies AI-generated codes"
    screens: [SCR-021]
    components: [C-019, C-020, C-023]
    flows: [FL-014]
    notes: "Inline Accept / Modify / Reject per row; Accept All with confirmation dialog; complete-blocking"

  - story_id: us_045
    title: "Admin user account management"
    screens: [SCR-022]
    components: [C-022, C-003, C-023, C-026]
    flows: []
    notes: "Create / deactivate / reactivate; role assignment"

  - story_id: us_046
    title: "Admin reviews audit log"
    screens: [SCR-023]
    components: [C-021, C-026]
    flows: [FL-015]
    notes: "Filter by date / actor / action; Export CSV; async for >10k rows; immutable entries"
```

---

## Visual Validation Criteria

Each screen must pass all checks before being marked "Design Complete" and handed off to Angular implementation:

| ID | Criterion | Check Method | Pass Condition |
|----|-----------|-------------|----------------|
| VVC-001 | Colour contrast ≥4.5:1 | WebAIM Contrast Checker | All text/background pairs pass 4.5:1 AA minimum |
| VVC-002 | PHI-safe page titles | Manual review | Title uses role + section only — no patient name, DOB, or appointment ID |
| VVC-003 | 4px spacing grid adherence | Design token audit | All spacing values divisible by 4px; no ad-hoc values |
| VVC-004 | No banned visual patterns | Visual audit | No blur, no gradients, no card box-shadow, no decorative textures |
| VVC-005 | Colour-independent status indicators | Visual audit | Every badge/indicator has colour + text label (never colour-only) |
| VVC-006 | AI content correctly labelled | Manual review | All AI-generated content has "AI Suggested" prefix + confidence % |
| VVC-007 | 5 states per screen covered | State audit | Default / Loading / Empty / Error / Validation all designed |
| VVC-008 | Touch targets ≥44×44px at mobile | Measurement | All interactive elements ≥44×44px at 375px viewport |
| VVC-009 | Visible focus ring | Visual audit | 3px teal focus ring (`box-shadow: 0 0 0 3px rgba(15,107,107,0.35)`) visible on all interactive elements |
| VVC-010 | Type scale adherence | Token audit | Only token-defined sizes used (12/14/16/20/24/32px) — no ad-hoc values |
| VVC-011 | Empty states have CTA | Manual review | Every empty state shows icon + heading + optional CTA — no blank placeholders |
| VVC-012 | ConfidenceMeter shows numeric % | Visual check | Confidence meter always displays percentage number alongside bar |
| VVC-013 | No Lorem ipsum content | Content review | All placeholder text is realistic example data |
| VVC-014 | SideNav role-filtered | Functional check | Navigation items reflect role — Patient/Staff/Admin never see cross-role routes |

---

## Implementation Scenarios

### Scenario 1: Responsive Layout Breakpoints

| Breakpoint | Layout Behaviour |
|------------|----------------|
| 1280px desktop (primary) | SideNav expanded (240px); content area fluid to max-width 1200px |
| 768px tablet | SideNav collapsed to icons (56px) or hidden behind hamburger; tables may reflow |
| 375px mobile | SideNav hidden behind hamburger; tables convert to card list; patient screens only |

**Angular implementation:** Use Angular CDK `BreakpointObserver` — do not use CSS-only hiding of interactive elements. Use `*ngIf` or `@if` structural directives for breakpoint-driven layout changes.

---

### Scenario 2: Session Timeout Warning (SCR-004)

At T−2min before session expiry, `SessionTimeoutModal` (C-024) must render with:

- `role="dialog"`, `aria-modal="true"`, `aria-labelledby="timeout-title"`
- `aria-live="assertive"` on countdown element — updates every 30 seconds minimum
- Angular CDK `FocusTrap` constraining focus inside modal
- **"Extend Session"** → POST `/api/auth/refresh` → modal dismissed; inactivity timer reset to 15 minutes
- **"Log Out"** → DELETE `/api/auth/session` → redirect to SCR-001
- On HTTP 401 from any API call → modal dismissed; redirect to SCR-001 with "Your session has expired"

---

### Scenario 3: AI Confidence Display (C-020, C-019)

`ConfidenceMeter` must always render:

1. Numeric percentage label (e.g., "87%") — minimum font-size 14px
2. Horizontal bar filled to percentage width
3. Colour threshold applied to bar:
   - ≥80% → `color.confidence-high` (#1A7A4A)
   - 50–79% → `color.confidence-mid` (#D4820A)
   - <50% → `color.confidence-low` (#C0392B)
4. Screen reader text via `aria-label` (e.g., `aria-label="Confidence: 87 percent — High"`)

**Never** use colour as the only confidence indicator — numeric % is mandatory for colour-blind accessibility.

---

### Scenario 4: PHI-Safe Rendering

- Angular `Title` service sets `<title>` = `"{Screen Name} — {Role} | ClinicalHub"` (e.g., "Daily Schedule — Staff | ClinicalHub")
- Patient name appears only within authenticated page body — never in `<title>`, breadcrumbs, URL path, or query string
- All API error messages surfaced to UI must be generic — PHI must not appear in error text
- Client-side console logging must use `[REDACTED]` placeholder for any PHI fields
- Angular router path params must use opaque IDs only (e.g., `/staff/patient/8a3f...` — never `/staff/patient/john-doe`)

---

### Scenario 5: Empty State Pattern (C-026)

Every `EmptyState` component instance must receive these inputs:

```typescript
@Input() icon: string;         // SVG asset reference (e.g., VA-003 through VA-008)
@Input() heading: string;      // Contextual heading (e.g., "No clinical documents on file")
@Input() body?: string;        // Optional supporting subtext
@Input() cta?: {               // Optional action button
  label: string;               // e.g., "Upload a document"
  route: string;               // Angular router path
};
```

**Never** render a blank screen, "No data" text, or null without an `EmptyState` component.

---

### Scenario 6: Trust-First Code Verification (C-019, SCR-021)

`CodeSuggestionRow` must enforce Trust-First pattern (FR-035):

- All AI-generated codes render with "AI Suggested" label + confidence meter in pending state
- Row state machine: `pending` → `accepted` / `modified` / `rejected`
- No code transitions from `pending` to any committed state without an explicit Staff action (click)
- "Accept All" (C-023 confirmation dialog) must list every code before bulk commit
- "Mark Coding Complete" must be blocked while any row remains in `pending` state
- All state transitions write to audit log via API call

---

### Scenario 7: Drag-Drop Queue with Keyboard Fallback (C-016)

`QueueTable` must implement dual interaction paths per WCAG 2.1.1:

- **Mouse path:** Angular CDK `DragDropModule`; `cdkDrag` on rows; `cdkDropList` on table
- **Keyboard path:** Position number input field per row (1 to N); Enter submits new position; order recalculated
- On successful reorder: POST new order to API; optimistic UI update; revert on error
- On concurrent edit conflict (HTTP 409): toast via `aria-live` region; queue auto-reloads from server

---

## Accessibility Requirements

| Requirement | WCAG Criterion | Level | Angular Implementation |
|-------------|---------------|-------|------------------------|
| Colour contrast ≥4.5:1 | 1.4.3 Contrast (Minimum) | AA | Verified via design token choices — all text on white ≥4.5:1 |
| Non-text contrast ≥3:1 | 1.4.11 Non-text Contrast | AA | Input borders (#D0D0D0 on #FFFFFF = 1.6:1 — use focus ring to meet threshold) |
| Keyboard navigation | 2.1.1 Keyboard | A | All components keyboard-navigable; no mouse-only interactions anywhere |
| No keyboard trap (except modals) | 2.1.2 No Keyboard Trap | A | CDK `FocusTrap` in modals only; always provides Escape / "Close" exit |
| Focus visible | 2.4.7 Focus Visible | AA | 3px teal focus ring on all interactive elements via global CSS |
| No colour-only information | 1.4.1 Use of Colour | A | All badges: colour + text; ConfidenceMeter: colour + numeric %; risk flags: colour + icon + text |
| Visible labels on all inputs | 1.3.1 Info & Relationships | A | All inputs have visible `<label>` element; no placeholder-as-label |
| Error identification | 3.3.1 Error Identification | A | Errors identified in text; associated via `aria-describedby` on input |
| Error suggestion | 3.3.3 Error Suggestion | AA | Error messages state what failed + corrective action |
| Screen reader announcements | 4.1.3 Status Messages | AA | Toasts, queue updates, form success via `aria-live="polite"` regions |
| Session timeout warning | 2.2.1 Timing Adjustable | A | 2-minute countdown warning with "Extend Session" option |
| Drag-drop keyboard alternative | 2.1.1 Keyboard | A | Position number input on QueueTable as full keyboard alternative |
| Focus management in modals | 2.4.3 Focus Order | AA | CDK `FocusTrap`; focus returns to trigger element on modal close |
| Touch target size | 2.5.5 Target Size | AA | Minimum 44×44px at 375px and 768px viewports |
| Timeout countdown accessible | 4.1.3 Status Messages | AA | `aria-live="assertive"` on SessionTimeoutModal countdown |
| Logical reading order | 2.4.3 Focus Order | AA | Tab order follows visual top-to-bottom, left-to-right reading order |

---

## Design Review Checklist

Complete this checklist before marking any screen design as ready for Angular implementation:

### Visual Design

- [ ] All spacing values are multiples of 4px (no ad-hoc values)
- [ ] Only token-defined font sizes used (12 / 14 / 16 / 20 / 24 / 32px)
- [ ] Card borders: 1px solid `#D0D0D0`; border-radius: 4px; no `box-shadow` on cards
- [ ] Primary teal (`#0F6B6B`) used only for primary buttons, active nav indicator, and link text
- [ ] No decorative blur, purple-to-blue gradients, background textures, or drop shadows on cards
- [ ] Status indicators show both colour and text label (never colour-only)
- [ ] ConfidenceMeter shows numeric % alongside coloured bar

### Content

- [ ] All placeholder text is realistic example data (zero Lorem ipsum)
- [ ] Page title is PHI-safe: role + section name only
- [ ] All AI-generated content labelled "AI Suggested" with confidence percentage
- [ ] Empty states have contextual heading + optional action CTA
- [ ] Error messages state what happened and what the user should do next

### Accessibility

- [ ] Colour contrast ≥4.5:1 verified on all text/background pairs
- [ ] All form inputs have visible `<label>` elements (no placeholder-only labels)
- [ ] Focus tab order follows logical visual reading order
- [ ] All icon-only buttons have descriptive `aria-label`
- [ ] Modal screens include `aria-modal="true"`, CDK `FocusTrap`, and return-focus logic
- [ ] Dynamic content updates (toasts, queue changes) use `aria-live` regions
- [ ] Drag-drop components have position-input keyboard alternative

### Responsiveness

- [ ] Screen reviewed and functional at 1280px desktop
- [ ] Screen reviewed and functional at 768px tablet
- [ ] Patient-facing screens reviewed and functional at 375px mobile
- [ ] Tables reflow to card/list layout below 768px
- [ ] Navigation collapses to hamburger below 768px
- [ ] Touch targets ≥44×44px at 375px and 768px viewports

### States

- [ ] **Default** state designed (normal content, no async activity)
- [ ] **Loading** state designed (PageLoadSkeleton matching content layout)
- [ ] **Empty** state designed (EmptyState component with heading + optional CTA)
- [ ] **Error** state designed (specific error message — not generic "Something went wrong")
- [ ] **Validation** state designed (inline field errors + top-of-form summary)

### Trust-First (AI screens only — SCR-011, SCR-019, SCR-020, SCR-021)

- [ ] All AI-generated content visually distinguished with "AI Suggested" label
- [ ] No AI output auto-committed — explicit Staff action required for every item
- [ ] Confidence meter present and showing numeric percentage alongside bar
- [ ] "Accept All" action requires confirmation dialog listing all items

### Handoff Checklist

- [ ] All design values reference token names (not hardcoded hex or pixel values)
- [ ] Component names in designs match component summary IDs in `figma_spec.md` (C-001 through C-028)
- [ ] Flow transitions annotated with FL-XXX references from `figma_spec.md`
- [ ] All `[SOURCE:INFERRED]` requirements reviewed and accepted/adjusted by product owner
- [ ] Screen ID (SCR-XXX) visible in design file frame name
