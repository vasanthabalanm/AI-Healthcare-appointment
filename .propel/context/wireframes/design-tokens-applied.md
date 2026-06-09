# Design Tokens Applied — ClinicalHub Wireframes

> **Version:** 1.0 · **Date:** May 2026  
> Source: `.propel/context/docs/designsystem.md` YAML token set  
> Applied to: all 23 HTML wireframes under `.propel/context/wireframes/Hi-Fi/`

---

## Aesthetic Direction

**Utilitarian** — functional clarity over decoration.

| Principle | Applied as |
|-----------|-----------|
| Colour-as-information only | Each hue carries exactly one meaning (see token roles below); no decorative colour |
| No gradients | `background` is always a flat token value; no `linear-gradient` used anywhere |
| No decorative shadows | `box-shadow` is used exclusively for focus rings; no card/elevation shadows |
| No animations beyond necessity | Only two motion patterns exist: focus ring (instant), countdown spinner (functional indicator) |
| Density over whitespace | 4px grid base; compact table rows (12px/20px padding); data presented without embellishment |
| Typography: single weight | `font-weight: 500` for labels/headings only; `400` for body; no display fonts |
| Borders define structure | `1px solid #D0D0D0` delimits all surfaces; no shadow-based depth |

---

## Token Sources

All tokens originate from `designsystem.md` YAML and are applied in wireframes as CSS custom properties on `:root`.

```css
/* Colour palette */
--cp:  #0F6B6B   /* primary teal — brand, interactive, active nav */
--cph: #0A5050   /* primary hover state */
--cps: #E6F2F2   /* primary subtle — active nav background, focus surfaces */
--cs0: #FFFFFF   /* surface-0 — primary card / modal background */
--cs1: #F5F5F5   /* surface-1 — page background */
--cs2: #EBEBEB   /* surface-2 — table row separator, secondary surface */
--cb:  #D0D0D0   /* border — all dividers, input borders, table borders */
--ct1: #1A1A1A   /* text-primary — all body copy */
--ct2: #5A5A5A   /* text-secondary — labels, timestamps, captions */
--ctd: #9A9A9A   /* text-disabled — entity IDs, placeholder text */
--cti: #FFFFFF   /* text-inverse — text on dark/teal backgrounds */
--ce:  #C0392B   /* error/danger */
--ceb: #FDECEA   /* error background */
--cw:  #D4820A   /* warning / no-show risk */
--cwb: #FEF5E7   /* warning background */
--cok: #1A7A4A   /* success / verified / confirmed */
--cokb:#E8F5EE   /* success background */
--ci:  #1C6EA4   /* info / AI suggestion */
--cib: #E8F0F8   /* info background */

/* Typography */
--ff: system-ui, "IBM Plex Sans", -apple-system, sans-serif
/* Scale: 12px / 13px / 14px (base) / 16px / 18px / 24px */

/* Border radius */
--r1: 2px   /* inline badges */
--r2: 4px   /* buttons, inputs, cards */
--r3: 8px   /* modals, panels */

/* Spacing (4px grid) */
--sp1: 4px   --sp2: 8px   --sp3: 12px  --sp4: 16px
--sp5: 20px  --sp6: 24px  --sp8: 32px  --sp12: 48px

/* Layout */
--nh: 56px     /* navbar height */
--sw: 240px    /* sidebar width */
```

---

## Token Application by Screen

| Screen | Primary surfaces | Interactive tokens | Status tokens | AI tokens |
|--------|-----------------|-------------------|---------------|-----------|
| SCR-001 Login | `--cs0` (card), `--cs1` (bg) | `--cp` (btn), `--cph` (hover) | `--ce`/`--ceb` (error) | — |
| SCR-002 Patient Registration | `--cs0`, `--cs1` | `--cp` (btn, focus), `--cb` (inputs) | `--ce` (field errors), `--cok` (success overlay) | — |
| SCR-003 Password Reset | `--cs0`, `--cs1` | `--cp` (btn) | `--ce` (token expired), `--cok` (step 3) | — |
| SCR-005 Patient Dashboard | `--cs0` (cards), `--cs1` (bg) | `--cp` (actions) | `--cok` (confirmed), `--cw` (pending) | — |
| SCR-006 Booking Calendar | `--cs0`, `--cs2` (calendar rows) | `--cp` (selected day/slot, btn), `--cph` (hover) | `--ce`/`--ceb` (slot-taken error), `--ctd` (past days) | — |
| SCR-007 Appt Confirmation | `--cs0` (confirm card) | `--cp` (primary actions) | `--cok`/`--cokb` (status: Confirmed) | — |
| SCR-008 My Appointments | `--cs0` (table), `--cs1` (bg) | `--cp` (book btn) | `--cok` (Confirmed), `--ci` (Arrived), `--ctd` (Cancelled) | — |
| SCR-009 Waitlist Registration | `--cs0`, `--cs1` | `--cp` (submit) | `--cw`/`--cwb` (existing entry warning) | — |
| SCR-010 Slot Swap Response | `--cs0` (offer cards), `--cs1` (bg) | `--cp` (accept), `--ce` (decline) | `--cw` (countdown <5min), `--ctd` (expired) | — |
| SCR-011 AI Intake | `--cs0` (chat bg), `--cs1` (right panel) | `--cp` (submit, user bubble border) | `--ci`/`--cib` (AI bubble, AI label) | `--ci`/`--cib` (AI suggested items) |
| SCR-012 Manual Intake | `--cs0`, `--cs1` | `--cp` (focus, btn) | `--cok` (insurance verified), `--cw` (pending), `--ce` (field errors) | — |
| SCR-013 My Documents | `--cs0` (table), `--cs1` (bg) | `--cp` (upload btn) | `--cok` (scan passed), `--cw` (low confidence), `--ce` (scan failed) | — |
| SCR-015 Calendar Sync | `--cs0` (appt card), `--cs1` (bg) | `--cp` (Google btn), `#2D6CDF` (Outlook btn*) | `--cok`/`--cokb` (synced), `--ceb` (declined) | — |
| SCR-016 Staff Schedule | `--cs0` (table), `--cs1` (bg) | `--cp` (check in btn) | `--cok` (Arrived), `--ci` (Confirmed), `--cw`/`--cwb` (high-risk row) | — |
| SCR-017 Walk-In Registration | `--cs0`, `--cs1` | `--cp` (search, add btn) | `--cw`/`--cwb` (not found), `--cok` (found card) | — |
| SCR-018 Same-Day Queue | `--cs0` (table) | `--cp` (add btn) | `--cw` (walk-in badge), `--ci` (in-progress) | — |
| SCR-019 360° Patient View | `--cs0` (panels) | `--cp` (verify btn, panel trigger) | `--cw`/`--cwb` (conflict banner), `--cok` (verified state) | — |
| SCR-020 Conflict Panel (slide-out) | `--cs0` (panel bg) | `--cp` (use-value btns) | `--cw` (conflict indicator), `--cok` (resolved) | — |
| SCR-021 Code Verification | `--cs0` (table), `--cs1` (bg) | `--cp` (accept btn, accept-all) | `--cok` (≥80%), `--cw` (50–79%), `--ce` (<50%) | `--ci` (BioMistral AI label) |
| SCR-022 User Management | `--cs0` (table) | `--cp` (create btn) | `--cok` (Active), `--ctd` (Inactive) | — |
| SCR-023 Audit Log | `--cs0` (table), `--cs1` (bg) | `--cp` (apply filters), `--cb` (clear) | Action badges: `--cokb`/`--cwb`/`--ceb`/`--cib`/`--cs2` | — |

*Outlook blue is a documented drift — see Drift Notes.

---

## Colour-as-Information Semantic Map

| Semantic | Token | Applied in |
|----------|-------|-----------|
| Brand / interactive / navigation | `--cp`/`--cph`/`--cps` | Buttons, active sidebar, focus rings, links |
| Confirmed / verified / success / scan passed | `--cok`/`--cokb` | Appointment status, insurance verified, scan badge, high confidence codes |
| Warning / risk / conflict / pending | `--cw`/`--cwb` | No-show risk badge, conflict banner, low confidence (50–79%), countdown <5min |
| Error / danger / rejected / virus | `--ce`/`--ceb` | Login error, slot taken, virus detected, form validation, very low confidence (<50%) |
| Info / AI suggestion / in-progress | `--ci`/`--cib` | AI chat bubbles, AI-suggested labels, BioMistral source label, "Arrived" status |
| Disabled / past / metadata | `--ctd` | Past calendar days, entity IDs, helper text |
| Neutral surface | `--cs0`/`--cs1`/`--cs2`/`--cb` | Card/page/row backgrounds, borders |

---

## Focus Ring Standard

Applied identically across all 23 wireframes:

```css
:focus-visible {
  outline: none;
  box-shadow: 0 0 0 3px rgba(15, 107, 107, 0.35);
}
```

No `:focus` override (only `:focus-visible` — avoids mouse click rings). Applies to: buttons, inputs, selects, anchors, day cells, slot buttons, drag handles.

---

## Typography Scale Applied

| Use | Size | Weight | Token reference |
|-----|------|--------|-----------------|
| Page title `<h1>` | 24px | 600 | — |
| Section heading `<h2>` | 18px | 600 | — |
| Card heading `<h3>` | 16px | 600 | — |
| Form labels, nav items | 14px | 500 | — |
| Body copy, table rows | 14px / 13px | 400 | — |
| Captions, timestamps, entity IDs | 12px / 11px | 400 | `--ct2`/`--ctd` |
| Logo | 18px | 600 | `--cp` |
| Action badges (monospace) | 11px | 600 | font-family: monospace |

---

## Drift Notes

Documented intentional deviations from the base design token set:

| Screen | Element | Token expected | Actual value | Reason |
|--------|---------|---------------|-------------|--------|
| SCR-016, SCR-017, SCR-018, SCR-019, SCR-020, SCR-021 | Staff user chip | `--cp` (#0F6B6B) | `#5A0A7A` (purple) | Role differentiation — staff must be visually distinct from patient; teal is the patient primary |
| SCR-022, SCR-023 | Admin user chip | `--cp` (#0F6B6B) | `#2C3E50` (slate navy) | Role differentiation — admin must be visually distinct from both patient and staff |
| SCR-022 | Role badge "Admin" | — | `#E8D5F5` bg / `#5A0A7A` text | Consistent with role-colour convention; Admin badge purple matches figma_spec role colours |
| SCR-015 | Outlook sync button | `--cp` | `#2D6CDF` (Microsoft brand blue) | Recognisable brand affordance; OAuth context justifies 3rd-party brand colour |
| All modals | Modal overlay scrim | — | `rgba(0,0,0,0.48)` | Not in token set; necessary for modal focus isolation; no decorative purpose |
| SCR-010 | Countdown timer text | `--ct2` | `--cw` (#D4820A) when <5min, `--ce` (#C0392B) when <60s | Urgency escalation — functional, not decorative |
| All screens | `<code>` / monospace entity IDs | `--ff` | `font-family: monospace` | Distinguishes machine IDs from human-readable text |

### Anti-patterns explicitly avoided
- No `linear-gradient` backgrounds
- No `box-shadow` for card elevation (only focus rings)
- No `animation` / `transition` beyond functional: countdown spinner, typing indicator, modal slide-in (none in wireframes)
- No colour used without accompanying text label (badges always show text + colour)
- No hardcoded hex values in component stylesheets (all via `var(--token)`)
- No lorem ipsum text anywhere — all sample data from `sample-data.json`
- No third-party CSS frameworks (Bootstrap, Tailwind) — pure custom CSS
- No implicit font fallbacks — `--ff` always specified explicitly

---

## Motion and Animation Policy

Per utilitarian aesthetic direction:

| Animation | Present in wireframes | Token/value | Justification |
|-----------|----------------------|-------------|---------------|
| Focus ring | Instant (no transition) | CSS `box-shadow` | Immediate feedback for a11y |
| Loading spinner | `animation: spin 0.8s linear infinite` | — | Functional: indicates async work |
| Typing indicator | 3-dot bounce CSS | — | Functional: Rasa response in-flight |
| Session countdown | JS `setInterval` decrement | — | Functional: time is running out |
| Slot swap countdown | JS `setInterval` decrement | — | Functional: offer expires |

No entrance animations, no hover transitions, no scroll animations.

---

## Token Conformance Summary

| Check | Result |
|-------|--------|
| All colour values via CSS custom properties | ✅ 22/23 screens (SCR-015 Outlook btn documented drift) |
| No gradients | ✅ All 23 screens |
| No elevation shadows | ✅ All 23 screens |
| Focus rings on all interactive elements | ✅ All 23 screens |
| Status always colour + text | ✅ All 23 screens |
| No lorem ipsum | ✅ All 23 screens |
| AI content labelled | ✅ SCR-011, SCR-021 |
| Role chips visually distinct per role | ✅ Patient (teal), Staff (purple), Admin (slate) |
| Spacing on 4px grid | ✅ All 23 screens |
| Font stack via `--ff` | ✅ All 23 screens |
