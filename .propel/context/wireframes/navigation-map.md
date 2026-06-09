# Navigation Map — ClinicalHub Wireframes

> **Version:** 1.0 · **Date:** May 2026  
> All 23 screens (SCR-001–SCR-023) · 8 modals (MOD-001–MOD-008) · 15 prototype flows (FL-001–FL-015)

---

## Flow Index

| Flow ID | Name | Entry | Exit |
|---------|------|-------|------|
| FL-001 | New patient registration | SCR-001 | SCR-005 (success) |
| FL-002 | Password reset | SCR-001 | SCR-001 (step 3 complete) |
| FL-003 | Session timeout | Any authenticated screen | SCR-001 |
| FL-004 | Book appointment | SCR-005 | SCR-007 |
| FL-005 | Join waitlist | SCR-006 (no-slots state) | SCR-009 success |
| FL-006 | Slot swap response | Notification link | SCR-007 or SCR-008 |
| FL-007 | Cancel / reschedule | SCR-008 | SCR-006 |
| FL-008 | AI intake | SCR-007 | SCR-005 |
| FL-009 | Manual intake | SCR-012 | SCR-005 |
| FL-010 | Document upload | SCR-013 | SCR-013 (refreshed) |
| FL-011 | Staff daily schedule | SCR-016 | SCR-019 (patient link) |
| FL-012 | Walk-in registration + queue | SCR-016 | SCR-018 |
| FL-013 | 360° view + conflict resolution | SCR-016 | SCR-021 |
| FL-014 | Medical code verification | SCR-019 | SCR-021 (complete) |
| FL-015 | Audit log + export | SCR-022 | SCR-023 (export) |

---

## Screen-to-Screen Links

### SCR-001 Login
```
→ SCR-002  "Create account" link
→ SCR-003  "Forgot password?" link
→ SCR-005  Patient login (role: patient) — on auth success
→ SCR-016  Staff login (role: staff) — on auth success
→ SCR-022  Admin login (role: admin) — on auth success
```

### SCR-002 Patient Registration
```
← SCR-001  "Sign in" link / back button
→ SCR-001  On registration success (show success overlay, then redirect)
→ SCR-001  "Already have an account? Sign in"
ERROR: Email already registered — stays on SCR-002 with field error
```

### SCR-003 Password Reset
```
← SCR-001  Back to login
Step 1: email entry → Step 2 (code/link sent notice) — always shown
Step 2: token check → Step 3 (new password entry) | EXPIRED state
Step 3: password saved → SCR-001 (success, redirect)
EXPIRED: "Request new link" → Step 1
```

### SCR-004 / MOD-001 Session Timeout Overlay
```
Embedded in: all authenticated screens (SCR-005 through SCR-023)
Actions:
  [Extend session] → stay on current screen (refreshed TTL)
  [Sign out]       → SCR-001
Auto-logout: 30-second countdown; fires → SCR-001
```

### SCR-005 Patient Dashboard
```
← SCR-001  (auth redirect)
→ SCR-006  "Book appointment" quick action / sidebar item
→ SCR-008  "My appointments" quick action / sidebar item
→ SCR-011  "Complete intake" button on appointment card
→ SCR-013  "My documents" sidebar item
```

### SCR-006 Booking Calendar
```
← SCR-005  Sidebar / Back
→ SCR-007  After slot selected + "Confirm booking" button
→ SCR-009  "Join waitlist" CTA in no-slots empty state
ERROR: Slot taken (concurrent booking) → stays on SCR-006, aria-live error banner
STATE: No slots this month → Join Waitlist CTA shown
```

### SCR-007 Appointment Confirmation
```
← SCR-006  (back link to calendar)
→ SCR-015  "Sync to Google Calendar" / "Sync to Outlook" button
→ SCR-008  "View all my appointments" link
→ SCR-011  "Complete intake now" button
```

### SCR-008 My Appointments
```
← SCR-005  Sidebar
→ SCR-006  "Book new appointment" button
→ SCR-010  Slot swap notification link (if offer pending)
[MOD-003 Cancel dialog] — stays on SCR-008; removes row on confirm
[MOD-004 Reschedule dialog] → SCR-006 on confirm
```

### MOD-003 Cancel Appointment
```
Embedded in: SCR-008
Trigger: "Cancel" button per appointment row
[Confirm cancel] → closes modal; appointment row updates to Cancelled
[Keep] → closes modal; no change
BLOCKED: if appointment < 24h away → cutoff message, Cancel hidden
```

### MOD-004 Reschedule Appointment
```
Embedded in: SCR-008
Trigger: "Reschedule" button per appointment row
[Confirm reschedule] → SCR-006 (with reschedule context)
[Cancel] → closes modal; no change
```

### SCR-009 Waitlist Registration
```
← SCR-006  "Join waitlist" link
STATE: Existing active entry → MOD-008 "Replace entry" dialog shown on submit
[MOD-008 Replace entry] → confirms; new entry recorded; SCR-009 success state
SUCCESS state → link back to SCR-005 (dashboard)
```

### SCR-010 Slot Swap Response
```
Entry: notification email link (deep link)
[Accept offered slot] → SCR-007 (new confirmation for offered slot)
[Decline] → SCR-008 (current apt retained); toast "You kept your current appointment"
EXPIRED state: offer elapsed; waitlist entry cleared; link → SCR-005
Countdown: 2-hour offer window
```

### SCR-011 AI Intake
```
← SCR-007  "Complete intake" button
→ SCR-005  After submit ("Back to dashboard")
[Switch to manual form] → SCR-012
Progress bar gates submit: all sections complete required
Trust-First: summary panel visible; final submit explicit
```

### SCR-012 Manual Intake Form
```
← SCR-011  "Switch to AI" button (reverse; pre-populates with current data)
→ SCR-005  After successful submit
[Save draft] → stays on SCR-012 (draft persisted)
Insurance validation (C-010) inline on blur → no navigation
```

### SCR-013 My Documents
```
← SCR-005  Sidebar
[Upload button] → MOD-002 Document Upload modal (embedded, no navigation)
[View document] → PDF preview in new browser tab
```

### MOD-002 / SCR-014 Document Upload Modal
```
Embedded in: SCR-013
Trigger: "Upload document" button
States: dropzone → uploading → scanning (ClamAV) → passed | failed
[Passed] → modal closes; SCR-013 table refreshes with new doc
[Virus detected] → error state in modal; doc discarded; close
[Too large / wrong format] → validation error in modal; retry
```

### SCR-015 Calendar Sync
```
← SCR-007  Calendar sync button
→ SCR-007  "Back" link after sync (success or decline)
[Sync to Google Calendar] → OAuth redirect (external) → return to SCR-015 success state
[Sync to Outlook] → OAuth redirect (external) → return to SCR-015 success state
DECLINED state: permission denied → SCR-015 shows retry message
Note: No credential stored; event-only OAuth scope
```

---

### Staff Navigation

### SCR-016 Staff Daily Schedule
```
← SCR-001  (auth redirect, role: staff)
→ SCR-019  "Patient name" link on appointment row → 360° view
→ SCR-017  "Register walk-in" button in page actions
→ SCR-018  "View same-day queue" button in page actions
[Check In inline] → stays on SCR-016; row status updates to Arrived
[High Risk only toggle] → filters table; stays on SCR-016
```

### SCR-017 Walk-In Registration
```
← SCR-016  Back button / sidebar
[Search → found] → patient card shown; "Add to queue" button
[Add to queue] → SCR-018 (queue updated with new entry)
[Search → not found] → "Create minimal profile" form shown
[Create profile + add] → SCR-018
[MOD-006 Capacity override] → triggered when queue ≥ 10 entries; confirm → SCR-018; action logged
```

### SCR-018 Same-Day Queue
```
← SCR-016  Sidebar / page action
← SCR-017  After add-to-queue
→ SCR-017  "+ Add walk-in" button
[Drag row] → reorder; position numbers update
[Position input] → reorder (keyboard alt per UXR-208)
[Remove] → confirmation inline; row removed
CONFLICT toast: concurrent edit detected → aria-live="assertive" banner; page auto-refreshes
```

### SCR-019 360° Patient View
```
← SCR-016  Patient name link in schedule
[Conflict banner → resolve] → SCR-020 slide-out panel (embedded)
[Generate codes] → SCR-021
[Mark as verified] → enabled only after conflict resolved; stays on SCR-019 (verified state)
Collapsible sections: toggle expand/collapse inline
```

### SCR-020 Conflict Resolution Panel (slide-out)
```
Embedded in: SCR-019
Trigger: "Resolve conflict" in conflict banner
[Use this value (left)] → conflict resolved; panel closes; SCR-019 banner removed; Verify enabled
[Use this value (right)] → same outcome, other source selected
[Dismiss] → panel closes; SCR-019 banner remains with "flagged" note; action audit-logged
```

### SCR-021 Medical Code Verification
```
← SCR-019  "Generate codes" button
[Accept per row] → row updates to Accepted; pending count decrements
[Modify per row] → inline editable code/description (no navigation)
[Reject per row] → row greyed out; pending count decrements
[Accept All] → MOD-005 confirmation modal (embedded)
[MOD-005 confirm] → all accepted; pending count = 0; Mark complete enabled
[Mark coding complete] → enabled when pendingCount === 0 (Trust-First)
→ SCR-016  After completing coding (back to schedule)
```

---

### Admin Navigation

### SCR-022 User Account Management
```
← SCR-001  (auth redirect, role: admin)
→ SCR-023  "Audit Log" sidebar item
[Create user button] → modal (embedded); on save → row added to table
[Edit per row] → modal (embedded); on save → row updated
[Deactivate per row] → deactivation confirm modal → status updates to Inactive
[Reactivate] → confirm → status updates to Active
Note: Current user (Morgan Blake) cannot self-deactivate — action hidden for own row
```

### SCR-023 Audit Log
```
← SCR-022  Sidebar
[Apply filters] → table filters in place; pagination updates
[Clear filters] → reset all filters; full table shown
[Export CSV] → async export; spinner shown; "download" simulated
Pagination: 20 per page; next/prev buttons
EMPTY state: no filter matches → empty state illustration + "Clear filters" CTA
Immutable: no edit/delete actions anywhere in table
```

---

## Dead Ends and Exceptions

| Screen | Why a "dead end" | What to do |
|--------|-----------------|------------|
| SCR-003 Step 3 (success) | Password changed; no forward nav | Show "Return to login" link → SCR-001 |
| SCR-009 success state | Waitlist registered | Show "Back to dashboard" → SCR-005 |
| SCR-010 expired state | Swap offer elapsed; entry cleared | Show "View dashboard" → SCR-005; optional re-join waitlist → SCR-009 |
| SCR-015 declined state | OAuth permission denied | "Retry" stays on SCR-015; "Back" → SCR-007 |
| MOD-002 virus detected | Document discarded | Error state in modal; "Try another file" or close modal |
| SCR-021 "Mark complete" | End of code verification flow | Return → SCR-016 (schedule) |
| SCR-023 export complete | Download done | No auto-redirect; user stays on page |

### Back navigation conventions
- Every screen provides explicit **Back** link or **sidebar active item**
- Browser back button behaviour is acceptable but not relied upon for navigation
- Modals restore focus to the trigger element on close
- Slide-out panel restores focus to "Resolve conflict" trigger on close

### Cross-role boundary rules
- No patient can access SCR-016 through SCR-023
- No staff can access SCR-022 or SCR-023 (admin-only)
- No admin can access patient-facing booking or intake screens in the same session
- Role determined from JWT claim; route guards enforce access
