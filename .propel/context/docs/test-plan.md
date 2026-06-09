# System Test Plan — Unified Patient Access & Clinical Intelligence Platform

## 1. Document Control

| Field | Value |
|-------|-------|
| **Scope** | Full (all epics: EP-TECH, EP-DATA, EP-001 – EP-008) |
| **Version** | 1.0 |
| **Status** | Draft |
| **Owner** | QA Engineering |
| **Prepared Date** | 2026-05-27 |
| **Applicable Builds** | All builds targeting `main` branch |

---

## 2. Test Objectives

1. Verify every functional requirement (FR-001 – FR-035) behaves as specified across all three roles (Patient, Staff, Admin).
2. Verify every non-functional requirement (NFR-001 – NFR-012) threshold is met under defined load and failure conditions.
3. Verify all AI requirements (AIR-001 – AIR-006) honour the Trust-First constraint and OSS-only restriction.
4. Verify all data requirements (DR-001 – DR-010) through schema, constraint, retention, and cache-invalidation checks.
5. Provide a traceable, role-gated test case for every use case (UC-001 – UC-024) defined in the specification.
6. Confirm HIPAA §164.312 technical safeguards are in place at every PHI boundary.

---

## 3. Scope

### In Scope
- All eight feature epics (EP-001 through EP-008) and both infrastructure epics (EP-TECH, EP-DATA).
- Unit, integration, end-to-end (E2E), security, performance, and data-integrity test layers.
- Backend API endpoints (`http://localhost:5153`) and Angular SPA (`http://localhost:4200`).
- Hangfire background jobs: `SendConfirmationEmailJob`, `Send48hReminderJob`, `Send2hReminderJob`, `SwapMonitorJob`, `OcrExtractionJob`, `GenerateIcd10CodesJob`, `GenerateCptCodesJob`.
- PostgreSQL 16 (`clinical_dev`) and SQL Server / ApplicationDbContext (EF Core in-memory for unit tests).
- AI services: `FallbackCodeGenerationService` → `RuleCodeGenerationService` (Ollama absent), `OllamaCodeGenerationService` (Ollama present).

### Out of Scope
- Provider-facing workflows (out of scope per BRD §6).
- Payment processing, family member profiles, EHR integration, claims submission.
- Multi-tenancy.
- Paid cloud infrastructure (AWS, Azure).
- Browser compatibility matrix beyond Chromium-based browsers for Phase 1.

---

## 4. Test Strategy

| Test Layer | Tooling | Coverage Target | Ownership |
|------------|---------|-----------------|-----------|
| Unit | xUnit + Moq + NullLogger; EF Core InMemory | ≥ 90% line, ≥ 85% branch per test class | Backend Dev |
| Angular Unit | Karma / Jest + Angular TestBed | ≥ 80% statement per service/component | Frontend Dev |
| Integration (API) | xUnit + `WebApplicationFactory<Program>` + Testcontainers (MSSQL + PG) | All happy path + critical error paths | Backend Dev |
| End-to-End | Playwright (TypeScript) — `tests/e2e/` | All use cases UC-001 – UC-024 | QA Automation |
| Performance | k6 OSS — `tests/perf/` | API P95 ≤ 2 s at 50 VU; PDF job ≤ 60 s | Performance Engineer |
| Security | OWASP ZAP (OSS) passive + active scan + manual JWT/RBAC checks | OWASP Top 10 zero critical findings | Security Reviewer |
| Data Integrity | psql scripts + EF Core migration smoke tests (Testcontainers) | All DR-001 – DR-010 constraints verified | Backend Dev |
| AI Accuracy | Offline evaluation harness — 100 labeled patient narratives | AI-Human Agreement Rate ≥ 98% (NFR-012) | ML Engineer |

---

## 5. Test Environment

| Component | Configuration |
|-----------|---------------|
| API | `dotnet run --project src/ClinicalHealthcare.Api` → `http://localhost:5153` |
| SPA | `ng serve` (esbuild) → `http://localhost:4200` |
| PostgreSQL | `localhost:5432`, DB=`clinical_dev`, user=`postgres`, password=`admin` |
| SQL Server / AppDb | EF Core InMemory (unit), Testcontainers MSSQL 2022 (integration) |
| Redis | Upstash local mock or `REDIS_URL` env var (unit mocked via `IConnectionMultiplexer` stub) |
| Hangfire | Test server with `UseHangfireServer()` in integration tests |
| Ollama | **Not installed** — `FallbackCodeGenerationService` routes to `RuleCodeGenerationService` |
| SMTP | MailKit with mock `SmtpClient` stub in test DI scope |
| SMS | `ISmsGateway` mock |
| OCR | Tesseract 5.x on host; mocked via `IOcrService` in unit tests |

### Seed Accounts

| Role | Email | Password | ID |
|------|-------|----------|----|
| Admin | `admin@clinicalhub.dev` | `Admin@1234` | 1 |
| Staff | `staff@clinicalhub.dev` | `Staff@1234` | 2 |
| Patient (default) | `patient@clinicalhub.dev` | `Patient@1234` | 3 |
| Patient (test) | `vasanthabalan.murugesan@kanini.com` | — | 15 |
| Patient (vasant veera) | — | — | 19 |

---

## 6. Requirement Traceability Matrix

| Req ID | Description (condensed) | Test-Case IDs |
|--------|--------------------------|---------------|
| FR-001 | Patient self-registration + email verification | TC-001, TC-002, TC-003, TC-004 |
| FR-002 | Admin creates/edits/deactivates Staff/Admin accounts | TC-005, TC-006, TC-007 |
| FR-003 | RBAC enforcement — cross-role → HTTP 403 | TC-008, TC-009 |
| FR-004 | 15-min session inactivity timeout | TC-010, TC-011 |
| FR-005 | Immutable append-only audit log | TC-012, TC-013, TC-014 |
| FR-006 | Password reset via email token | TC-015, TC-016 |
| FR-007 | Patient views calendar and books a slot | TC-017, TC-018 |
| FR-008 | Patient joins waitlist for preferred slot | TC-019, TC-020 |
| FR-009 | System auto-swaps preferred slot, patient opt-out window | TC-021, TC-022, TC-023 |
| FR-010 | No-show risk score calculated at booking, displayed to Staff | TC-024, TC-025 |
| FR-011 | Patient cancels / reschedules before cutoff | TC-026, TC-027, TC-028 |
| FR-012 | Google Calendar OAuth + event creation | TC-029 |
| FR-013 | Outlook Calendar OAuth + event creation | TC-030 |
| FR-014 | Confirmation email with PDF within 60 s | TC-031, TC-032 |
| FR-015 | AI conversational intake (multi-turn, confirm) | TC-033, TC-034, TC-035 |
| FR-016 | Manual intake form (required fields, 422 on missing) | TC-036, TC-037 |
| FR-017 | Intake edit (versioned, audit logged) | TC-038 |
| FR-018 | Insurance pre-check (soft, non-blocking) | TC-039, TC-040 |
| FR-019 | Staff walk-in registration → same-day queue | TC-041, TC-042 |
| FR-020 | Queue view, reorder, remove; optimistic concurrency | TC-043, TC-044, TC-045 |
| FR-021 | Staff check-in (audit logged; no patient self-check-in) | TC-046, TC-047 |
| FR-022 | Staff daily schedule view with status indicators | TC-048 |
| FR-023 | SMS reminders at T-48 h and T-2 h | TC-049 |
| FR-024 | Email reminders at T-48 h and T-2 h | TC-050 |
| FR-025 | No-show risk flag on Staff daily schedule | TC-051 |
| FR-026 | Patient document upload (type, size, virus gate) | TC-052, TC-053, TC-054, TC-055 |
| FR-027 | AES-256 encryption at rest; no unauthenticated endpoint | TC-056, TC-057 |
| FR-028 | OCR text extraction from uploaded document | TC-058, TC-059 |
| FR-029 | Structured field extraction (vitals, history, meds, etc.) | TC-060 |
| FR-030 | De-duplication — most recent non-conflicting value | TC-061 |
| FR-031 | Conflict detection and flagging for Staff review | TC-062, TC-063 |
| FR-032 | 360° patient view — consolidated de-duplicated fields | TC-064, TC-065 |
| FR-033 | ICD-10 code generation with confidence scores | TC-066, TC-067, TC-068 |
| FR-034 | CPT code generation; "No procedures" message | TC-069, TC-070 |
| FR-035 | Staff code verification (Accept/Modify/Reject; Trust-First) | TC-071, TC-072, TC-073, TC-074 |
| NFR-001 | 99.9% uptime / Windows Service auto-restart | TC-075 |
| NFR-002 | PDF confirmation email SLA ≤ 60 s | TC-076 |
| NFR-003 | API P95 ≤ 2 s at 50 VU; async job isolation | TC-077, TC-078 |
| NFR-004 | HIPAA: PHI AES-256 at rest + TLS 1.2 in transit | TC-079, TC-080 |
| NFR-005 | HTTP 403 + audit log on RBAC violation | TC-009, TC-013 |
| NFR-006 | Encrypt before write acknowledgement | TC-057 |
| NFR-007 | JWT ≤15 min; HTTPS; CSRF protection | TC-081, TC-082, TC-083 |
| NFR-008 | Audit log: HTTP 405 on modification; INSERT-only privilege | TC-084, TC-085 |
| NFR-009 | Exponential backoff (3 retries, 30 s base, 2× multiplier) | TC-086 |
| NFR-010 | Heavy jobs async — never block HTTP thread | TC-078 |
| NFR-011 | OSS-only dependency audit | TC-087 |
| NFR-012 | AI-Human Agreement Rate ≥ 98% (30-day rolling) | TC-088 |
| DR-001 | UserAccount schema + unique email index | TC-089 |
| DR-002 | Appointment FSM status constraints + rowversion | TC-090, TC-091 |
| DR-003 | WaitlistEntry unique partial index (one Active per patient) | TC-020 |
| DR-004 | IntakeRecord versioning + prior version retained | TC-038 |
| DR-005 | ClinicalDocument encryption path + blob outside DB | TC-056 |
| DR-006 | ExtractedClinicalField + ConflictFlag schema | TC-062 |
| DR-007 | MedicalCodeSuggestion Trust-First (null verifier blocked) | TC-073 |
| DR-008 | AuditLog INSERT-only privilege at DB level | TC-085 |
| DR-009 | PHI 6-year soft-delete retention | TC-092 |
| DR-010 | Redis TTL (session 900 s, slots 60 s, 360view 300 s) | TC-093 |
| AIR-001 | NLU clarification below confidence 0.70 | TC-034 |
| AIR-002 | OCR low-confidence flag (< 0.75 per page) | TC-059 |
| AIR-003 | ICD-10 top-3 candidates; Agreement Rate ≥ 98% | TC-066, TC-088 |
| AIR-004 | CPT "No procedures identified" when no procedural data | TC-070 |
| AIR-005 | Trust-First: `verified_by` required server-side for commit | TC-073 |
| AIR-006 | No paid AI API calls; local inference only | TC-087 |

---

## 7. Test Cases

### EP-001 — User Authentication, RBAC & Compliance

---

#### TC-001 · FR-001 · Patient self-registration — happy path

| Field | Value |
|-------|-------|
| **Type** | E2E / Integration |
| **Priority** | P1 |
| **Requirement** | FR-001 |
| **Given** | No existing account with target email; SMTP stub active |
| **When** | `POST /auth/register { name, dob, phone, email, password }` |
| **Then** | HTTP 201; `emailVerified=false`; verification email dispatched via Hangfire; account row inserted with `IsActive=true` |
| **Assertions** | `Assert.Equal(201, status)`; DB row `emailVerified = false`; Hangfire job `SendVerificationEmailJob` enqueued |

---

#### TC-002 · FR-001 · Patient registration — duplicate email → 409

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-001 |
| **Given** | Patient account already exists with `email=patient@clinicalhub.dev` |
| **When** | `POST /auth/register` with same email |
| **Then** | HTTP 409; no duplicate row inserted |
| **Assertions** | `Assert.Equal(409, status)`; `db.UserAccounts.Count(u => u.Email == email) == 1` |

---

#### TC-003 · FR-001 · Email verification link — activates account

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-001 |
| **Given** | Unverified account; valid single-use token |
| **When** | `GET /auth/verify-email?token={token}` |
| **Then** | HTTP 200; `emailVerified=true`; token invalidated (second use → 400 or 410) |
| **Assertions** | DB `EmailVerified = true`; second call returns ≥ 400 |

---

#### TC-004 · FR-001 · Verification token expired (> 24 h) → rejected

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-001 |
| **Given** | Token with `CreatedAt = UtcNow.AddHours(-25)` |
| **When** | `GET /auth/verify-email?token={expiredToken}` |
| **Then** | HTTP 400 / 410; account remains unverified |
| **Assertions** | DB `EmailVerified = false` |

---

#### TC-005 · FR-002 · Admin creates Staff account

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-002 |
| **Given** | Admin JWT; no existing account with target email |
| **When** | `POST /admin/users { email, role:"staff", firstName, lastName }` |
| **Then** | HTTP 201; account created in inactive state; credential-setup email enqueued |
| **Assertions** | DB row `Role = "staff"`, `IsActive = false`; Hangfire credential-setup job enqueued |

---

#### TC-006 · FR-002 · Admin deactivates Staff account

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-002 |
| **Given** | Admin JWT; active Staff account ID=2 |
| **When** | `PATCH /admin/users/2 { isActive: false }` |
| **Then** | HTTP 200; `IsActive = false`; audit log entry written |
| **Assertions** | DB `IsActive = false`; `AuditLog` entry with `ActionType = "UPDATE"` |

---

#### TC-007 · FR-002 · Admin cannot deactivate last Admin account

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-002 |
| **Given** | Only one Admin account exists; Admin JWT |
| **When** | `PATCH /admin/users/{adminId} { isActive: false }` |
| **Then** | HTTP 409; account unchanged |
| **Assertions** | DB `IsActive = true`; response body contains guard message |

---

#### TC-008 · FR-003 · Patient token accessing Staff endpoint → 403

| Field | Value |
|-------|-------|
| **Type** | Integration / Security |
| **Priority** | P1 |
| **Requirement** | FR-003, NFR-005 |
| **Given** | JWT with `role=patient` |
| **When** | `GET /staff/queue` (Staff-only endpoint) |
| **Then** | HTTP 403; AuditLog entry written; no data returned |
| **Assertions** | `Assert.Equal(403, status)`; `AuditLog` contains RBAC violation entry |

---

#### TC-009 · FR-003 · Staff token accessing Admin endpoint → 403

| Field | Value |
|-------|-------|
| **Type** | Integration / Security |
| **Priority** | P1 |
| **Requirement** | FR-003, NFR-005 |
| **Given** | JWT with `role=staff` |
| **When** | `POST /admin/users` |
| **Then** | HTTP 403 |
| **Assertions** | `Assert.Equal(403, status)` |

---

#### TC-010 · FR-004 · Session timeout after 15 min inactivity

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-004, NFR-007 |
| **Given** | Valid JWT issued; Redis token entry set with TTL=900 s; 900 s advance clock / Redis key manually expired |
| **When** | Any authenticated request with stale token |
| **Then** | HTTP 401 |
| **Assertions** | `Assert.Equal(401, status)`; Redis key absent |

---

#### TC-011 · FR-004 · Session extension resets TTL

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P2 |
| **Requirement** | FR-004 |
| **Given** | Active session; Redis TTL at ~450 s remaining |
| **When** | `POST /auth/extend-session` |
| **Then** | HTTP 200; Redis TTL reset to 900 s |
| **Assertions** | `redis.TTL(key) >= 890` |

---

#### TC-012 · FR-005 · Audit log entry written for every CRUD action

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-005 |
| **Given** | Staff JWT; patient record exists |
| **When** | Any state-changing API call (e.g., `PATCH /patients/{id}/verify`) |
| **Then** | AuditLog row inserted with correct `actorId`, `actionType`, `affectedEntityId`, `timestamp` |
| **Assertions** | `db.AuditLog.Any(a => a.ActorId == staffId && a.ActionType == "UPDATE")` |

---

#### TC-013 · FR-005 / NFR-005 · RBAC violation itself logged in audit trail

| Field | Value |
|-------|-------|
| **Type** | Integration / Security |
| **Priority** | P1 |
| **Requirement** | FR-005, NFR-005 |
| **Given** | Patient JWT; attempts to access Staff endpoint |
| **When** | `GET /staff/queue` → 403 |
| **Then** | AuditLog row with `ActionType = "ACCESS_DENIED"` written |
| **Assertions** | `db.AuditLog.Any(a => a.ActionType == "ACCESS_DENIED" && a.ActorId == patientId)` |

---

#### TC-014 · FR-005 · Read action logged (audit log read itself)

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P2 |
| **Requirement** | FR-005 |
| **Given** | Admin JWT |
| **When** | `GET /admin/audit?dateFrom=...` |
| **Then** | HTTP 200; AuditLog entry with `ActionType = "READ"` and `affectedEntityType = "AuditLog"` |
| **Assertions** | `db.AuditLog.Any(a => a.ActionType == "READ" && a.AffectedEntityType == "AuditLog")` |

---

#### TC-015 · FR-006 · Password reset — valid token flow

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-006 |
| **Given** | Active patient account |
| **When** | `POST /auth/forgot-password { email }` then `POST /auth/reset-password { token, newPassword }` |
| **Then** | HTTP 200 both calls; password updated; token invalidated |
| **Assertions** | Login with new password succeeds; second use of same token → 400/410 |

---

#### TC-016 · FR-006 · Password reset token expired after 60 min

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-006 |
| **Given** | Token with `CreatedAt = UtcNow.AddMinutes(-61)` |
| **When** | `POST /auth/reset-password { token, newPassword }` |
| **Then** | HTTP 400 / 410; password unchanged |
| **Assertions** | Login with old password still succeeds |

---

### EP-002 — Appointment Booking & Scheduling

---

#### TC-017 · FR-007 · Patient books available slot — happy path

| Field | Value |
|-------|-------|
| **Type** | E2E / Integration |
| **Priority** | P1 |
| **Requirement** | FR-007, DR-002 |
| **Given** | Patient JWT; at least one slot with `IsAvailable=true` |
| **When** | `POST /appointments { slotId }` |
| **Then** | HTTP 201; `Appointment.Status = Scheduled`; slot `IsAvailable = false`; no-show risk score stored; confirmation email job enqueued |
| **Assertions** | DB appointment row; `Slot.IsAvailable = false`; Hangfire `SendConfirmationEmailJob` enqueued |

---

#### TC-018 · FR-007 · Concurrent slot booking — exactly one succeeds (optimistic lock)

| Field | Value |
|-------|-------|
| **Type** | Integration / Performance |
| **Priority** | P1 |
| **Requirement** | FR-007, DR-002 |
| **Given** | Two patient clients; same `slotId` |
| **When** | Both `POST /appointments { slotId }` simultaneously |
| **Then** | One HTTP 201; one HTTP 409 |
| **Assertions** | DB: exactly one Appointment row; `Slot.IsAvailable = false` |

---

#### TC-019 · FR-008 · Patient joins waitlist for preferred slot

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-008, DR-003 |
| **Given** | Patient JWT; patient has confirmed appointment; preferred slot `IsAvailable=false` |
| **When** | `POST /waitlist { preferredSlotId }` |
| **Then** | HTTP 201; `WaitlistEntry.Status = Active` |
| **Assertions** | DB `WaitlistEntry` row with `Status = Active` and correct patient FK |

---

#### TC-020 · FR-008 / DR-003 · Second waitlist entry replaces first (unique partial index guard)

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-008, DR-003 |
| **Given** | Patient already has an Active `WaitlistEntry`; new preferred slot |
| **When** | `POST /waitlist { preferredSlotId: newSlot }` |
| **Then** | HTTP 200 or 201; prior entry status becomes `Expired`; new entry `Active`; still only one `Active` per patient |
| **Assertions** | `db.WaitlistEntries.Count(e => e.PatientId == id && e.Status == Active) == 1` |

---

#### TC-021 · FR-009 · Slot release triggers swap notification to first waitlisted patient

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-009 |
| **Given** | Patient A has `WaitlistEntry(Active)` for slot X; slot X becomes available |
| **When** | `SwapMonitorJob.ExecuteAsync` runs |
| **Then** | Patient A receives swap-offer notification (email + SMS mocks called); `WaitlistEntry.Status = Offered`; 2-hour expiry window set |
| **Assertions** | SMTP mock called; SMS mock called; `WaitlistEntry.Status = Offered` |

---

#### TC-022 · FR-009 · Patient accepts swap — appointment updated, old slot released

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-009 |
| **Given** | `WaitlistEntry.Status = Offered`; patient JWT |
| **When** | `POST /waitlist/{id}/accept` |
| **Then** | HTTP 200; `Appointment.SlotId` updated to preferred slot; old slot `IsAvailable = true`; `WaitlistEntry.Status = Accepted`; new confirmation email enqueued |
| **Assertions** | DB appointment at new slotId; old slot `IsAvailable = true`; Hangfire job enqueued |

---

#### TC-023 · FR-009 · Swap offer window expires — treated as implicit decline

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-009 |
| **Given** | `WaitlistEntry.Status = Offered`; offer timestamp = `UtcNow.AddHours(-3)` (past 2-h window) |
| **When** | `SwapMonitorJob` re-runs |
| **Then** | `WaitlistEntry.Status = Expired`; slot remains available; next eligible waitlist patient notified |
| **Assertions** | `WaitlistEntry.Status = Expired`; next patient's entry becomes `Offered` |

---

#### TC-024 · FR-010 · No-show risk score calculated at booking time

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-010 |
| **Given** | Patient with 2 prior no-shows; booking lead time = 1 day; intake not completed |
| **When** | `POST /appointments` |
| **Then** | `Appointment.NoShowRiskScore` > 0; score reflects rule weighting |
| **Assertions** | `Assert.True(appointment.NoShowRiskScore > 0)` |

---

#### TC-025 · FR-010 · No-show risk score visible to Staff on schedule

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-010 |
| **Given** | Staff JWT; appointment with `NoShowRiskScore > 0` |
| **When** | `GET /staff/schedule?date={today}` |
| **Then** | Response includes `noShowRiskScore` per appointment; high-risk entries include visual flag field |
| **Assertions** | `response.appointments[0].noShowRiskScore != null` |

---

#### TC-026 · FR-011 · Patient cancels before cutoff — slot released

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-011 |
| **Given** | Patient JWT; appointment with `SlotTime = UtcNow.AddHours(4)` (beyond cutoff) |
| **When** | `DELETE /appointments/{id}` |
| **Then** | HTTP 200; `Appointment.Status = Cancelled`; `Slot.IsAvailable = true`; pending reminder jobs cancelled |
| **Assertions** | DB status; slot status; Hangfire cancellation confirmed |

---

#### TC-027 · FR-011 · Patient cancels inside cutoff window — blocked

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-011 |
| **Given** | Patient JWT; appointment with `SlotTime = UtcNow.AddMinutes(30)` (within cutoff) |
| **When** | `DELETE /appointments/{id}` |
| **Then** | HTTP 422 or 409; appointment unchanged |
| **Assertions** | DB `Status = Scheduled` |

---

#### TC-028 · FR-011 · Patient reschedules — old slot released, new slot confirmed

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-011 |
| **Given** | Patient JWT; existing appointment outside cutoff; new available slot |
| **When** | `PATCH /appointments/{id}/reschedule { newSlotId }` |
| **Then** | HTTP 200; old slot `IsAvailable = true`; new appointment at new slot; confirmation email enqueued |
| **Assertions** | Old `Slot.IsAvailable = true`; new appointment row or updated `SlotId` |

---

### EP-003 — Notifications & Calendar Integration

---

#### TC-029 · FR-012 · Google Calendar sync creates event

| Field | Value |
|-------|-------|
| **Type** | Integration (OAuth mock) |
| **Priority** | P2 |
| **Requirement** | FR-012 |
| **Given** | Patient JWT; confirmed appointment; Google OAuth token stored (AES-256) |
| **When** | `POST /appointments/{id}/calendar-sync { provider: "google" }` |
| **Then** | HTTP 200; Google Calendar API mock called with correct event payload (date, time, location) |
| **Assertions** | HTTP client mock called once; response body confirms event created |

---

#### TC-030 · FR-013 · Outlook Calendar sync creates event

| Field | Value |
|-------|-------|
| **Type** | Integration (OAuth mock) |
| **Priority** | P2 |
| **Requirement** | FR-013 |
| **Given** | Patient JWT; confirmed appointment; Microsoft Graph token stored |
| **When** | `POST /appointments/{id}/calendar-sync { provider: "outlook" }` |
| **Then** | HTTP 200; Graph API mock called with event payload |
| **Assertions** | HTTP client mock called once |

---

#### TC-031 · FR-014 · PDF confirmation email enqueued within request, delivered ≤ 60 s

| Field | Value |
|-------|-------|
| **Type** | Integration / Performance |
| **Priority** | P1 |
| **Requirement** | FR-014, NFR-002 |
| **Given** | Patient books appointment; SMTP stub active |
| **When** | `POST /appointments` |
| **Then** | Hangfire `SendConfirmationEmailJob` starts and SMTP mock receives email with PDF attachment within 60 s |
| **Assertions** | SMTP mock received call; PDF attachment byte length > 0; elapsed time ≤ 60 s |

---

#### TC-032 · FR-014 · PDF generation failure — 3 retries then dead-letter

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-014, NFR-009 |
| **Given** | `SendConfirmationEmailJob`; SMTP mock throws exception on every attempt |
| **When** | Job executes (will retry 3 times with exponential backoff) |
| **Then** | After 3 failures, job moves to dead-letter queue; Staff dashboard surfaced |
| **Assertions** | Hangfire job state = `Failed`; `FailedJobCount > 0` in dashboard API |

---

#### TC-049 · FR-023 · SMS reminder fires at T-48 h

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-023 |
| **Given** | Appointment at `UtcNow.AddHours(48.5)`; Hangfire scheduled job |
| **When** | Clock advances to T-48 h; `Send48hReminderJob` executes |
| **Then** | `ISmsGateway.SendAsync` called with correct phone and appointment details |
| **Assertions** | SMS mock called once with correct patient phone |

---

#### TC-050 · FR-024 · Email reminder fires at T-48 h and T-2 h

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-024 |
| **Given** | Appointment scheduled; two Hangfire reminder jobs enqueued |
| **When** | Clock advances to T-48 h and T-2 h respectively |
| **Then** | SMTP mock called twice; each email contains appointment date, time, cancel/reschedule link |
| **Assertions** | SMTP mock call count = 2 across both jobs |

---

### EP-004 — Patient Intake

---

#### TC-033 · FR-015 · AI intake multi-turn session — fields captured and stored

| Field | Value |
|-------|-------|
| **Type** | Integration (Rasa mock) |
| **Priority** | P1 |
| **Requirement** | FR-015, AIR-001 |
| **Given** | Patient JWT; confirmed appointment; Rasa REST mock |
| **When** | `POST /intake/ai/start`, series of `POST /intake/ai/turn`, then `POST /intake/ai/confirm` |
| **Then** | HTTP 201 on confirm; `IntakeRecord.Source = "AI"`, all fields populated |
| **Assertions** | DB `IntakeRecord` row with non-null `ChiefComplaint`, `MedicalHistory` |

---

#### TC-034 · FR-015 / AIR-001 · AI intake clarification when confidence < 0.70

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-015, AIR-001 |
| **Given** | Rasa mock returns `confidence = 0.55` for a patient response |
| **When** | `POST /intake/ai/turn { sessionId, message }` |
| **Then** | Response contains clarification prompt rather than moving to next topic; `IntakeRecord` not yet stored |
| **Assertions** | Response body `requiresClarification = true` |

---

#### TC-035 · FR-015 · Mid-flow switch to manual form preserves captured fields

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P2 |
| **Requirement** | FR-015, FR-016 |
| **Given** | AI intake session in progress; 2 of 5 fields captured |
| **When** | `POST /intake/ai/switch-to-manual { sessionId }` |
| **Then** | HTTP 200; manual form pre-populated with captured fields |
| **Assertions** | Response `fields.chiefComplaint` equals previously captured value |

---

#### TC-036 · FR-016 · Manual intake — happy path

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-016 |
| **Given** | Patient JWT; confirmed appointment |
| **When** | `POST /intake/manual { appointmentId, chiefComplaint, medicalHistory, currentMeds, allergies }` |
| **Then** | HTTP 201; `IntakeRecord.Source = "Manual"`, `Version = 1` |
| **Assertions** | DB row with all fields present |

---

#### TC-037 · FR-016 · Manual intake — required field missing → 422

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-016 |
| **Given** | Patient JWT; intake body with `chiefComplaint = null` |
| **When** | `POST /intake/manual` |
| **Then** | HTTP 422; no `IntakeRecord` row created |
| **Assertions** | `Assert.Equal(422, status)` |

---

#### TC-038 · FR-017 / DR-004 · Intake edit increments version and audits delta

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-017, DR-004 |
| **Given** | Existing `IntakeRecord(Version=1)`; patient JWT; prior to check-in cutoff |
| **When** | `PATCH /intake/{intakeId} { chiefComplaint: "Updated complaint" }` |
| **Then** | HTTP 200; new `IntakeRecord(Version=2)` inserted; prior version retained; `AuditLog` entry with before/after values |
| **Assertions** | `db.IntakeRecords.Count(r => r.AppointmentId == id) == 2`; `AuditLog` entry present |

---

#### TC-039 · FR-018 · Insurance pre-check — match found → Validated

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P2 |
| **Requirement** | FR-018 |
| **Given** | Insurance record in `InsuranceDummyRecord` table |
| **When** | `POST /intake/insurance-check { providerName, insuranceId }` |
| **Then** | HTTP 200; `result = "Validated"` |
| **Assertions** | `response.result == "Validated"` |

---

#### TC-040 · FR-018 · Insurance pre-check — no match → non-blocking warning

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-018 |
| **Given** | Insurance provider/ID not in dummy records |
| **When** | `POST /intake/insurance-check { providerName, insuranceId }` |
| **Then** | HTTP 200; `result = "NotVerified"`; intake flow not blocked |
| **Assertions** | `response.result == "NotVerified"` |

---

### EP-005 — Staff Operations & Queue Management

---

#### TC-041 · FR-019 · Staff registers walk-in (existing patient)

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-019 |
| **Given** | Staff JWT; patient profile exists |
| **When** | `POST /staff/queue/walkin { patientId }` |
| **Then** | HTTP 201; patient added to same-day queue with `walkInFlag = true`; `AuditLog` entry |
| **Assertions** | DB queue entry; `AuditLog` with `ActorId = staffId` |

---

#### TC-042 · FR-019 · Staff registers walk-in (new minimal patient)

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-019 |
| **Given** | Staff JWT; patient not in system |
| **When** | `POST /staff/queue/walkin { name, dob, phone }` |
| **Then** | HTTP 201; minimal patient profile created; patient added to queue |
| **Assertions** | `db.UserAccounts` has new row; queue entry present |

---

#### TC-043 · FR-020 · Staff views and reorders queue

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-020 |
| **Given** | Staff JWT; 3 queue entries |
| **When** | `PATCH /staff/queue/reorder { entries: [{id: 3, position: 1}, {id: 1, position: 2}, {id: 2, position: 3}] }` |
| **Then** | HTTP 200; queue order reflects new positions; estimated wait times recalculated |
| **Assertions** | `db.QueueEntries` positions updated |

---

#### TC-044 · FR-020 · Queue reorder — concurrent edit conflict → 409

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-020 |
| **Given** | Two Staff clients; same queue; stale `rowversion` on second request |
| **When** | Both send `PATCH /staff/queue/reorder` |
| **Then** | First → HTTP 200; second → HTTP 409 |
| **Assertions** | `Assert.Equal(409, secondResponse.StatusCode)` |

---

#### TC-045 · FR-020 · Staff removes patient from queue

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-020 |
| **Given** | Staff JWT; queue entry with ID=5 |
| **When** | `DELETE /staff/queue/{5}` |
| **Then** | HTTP 200; entry removed; remaining positions compressed |
| **Assertions** | `db.QueueEntries.Any(e => e.Id == 5) == false` |

---

#### TC-046 · FR-021 · Staff checks in patient — status → Arrived, audit logged

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-021 |
| **Given** | Staff JWT; appointment `Status = Scheduled` |
| **When** | `PATCH /appointments/{id}/checkin` |
| **Then** | HTTP 200; `Appointment.Status = Arrived`; timestamp set; `AuditLog` entry |
| **Assertions** | DB status; `AuditLog` entry with `ActionType = "UPDATE"` |

---

#### TC-047 · FR-021 · Patient cannot self-check-in (endpoint is Staff-role-gated)

| Field | Value |
|-------|-------|
| **Type** | Security |
| **Priority** | P1 |
| **Requirement** | FR-021 |
| **Given** | Patient JWT |
| **When** | `PATCH /appointments/{id}/checkin` |
| **Then** | HTTP 403 |
| **Assertions** | `Assert.Equal(403, status)` |

---

#### TC-048 · FR-022 · Staff daily schedule shows all appointments with status indicators

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-022 |
| **Given** | Staff JWT; 3 appointments on today's date with statuses Scheduled, Arrived, Cancelled |
| **When** | `GET /staff/schedule?date={today}` |
| **Then** | HTTP 200; all 3 appointments returned; each has `status` field |
| **Assertions** | Response array length = 3; each item has `status` in expected FSM set |

---

#### TC-051 · FR-025 · High-risk appointments flagged on Staff schedule

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-025 |
| **Given** | Staff JWT; appointment with `NoShowRiskScore >= highRiskThreshold` |
| **When** | `GET /staff/schedule?date={today}` |
| **Then** | That appointment has `highRiskFlag = true` in response |
| **Assertions** | `response.appointments.Any(a => a.highRiskFlag == true)` |

---

### EP-006 — Clinical Document Management

---

#### TC-052 · FR-026 · Patient uploads valid PDF — stored encrypted

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-026, FR-027 |
| **Given** | Patient JWT; PDF file ≤ size limit; virus scan mock returns `Pass` |
| **When** | `POST /documents` (multipart/form-data) |
| **Then** | HTTP 201; `ClinicalDocument.VirusScanResult = Pass`; `EncryptedBlobPath` set; blob file not plaintext |
| **Assertions** | DB row; blob exists on filesystem; blob first bytes are not `%PDF` (i.e., encrypted) |

---

#### TC-053 · FR-026 · Document exceeds size limit → rejected

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-026 |
| **Given** | Patient JWT; file exceeds `MaxFileSizeBytes` config |
| **When** | `POST /documents` |
| **Then** | HTTP 413 or 422; no `ClinicalDocument` row created |
| **Assertions** | `db.ClinicalDocuments.Count() == 0` |

---

#### TC-054 · FR-026 · Unsupported file type → rejected

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-026 |
| **Given** | Patient JWT; `.exe` file |
| **When** | `POST /documents` |
| **Then** | HTTP 415 or 422 |
| **Assertions** | `Assert.True(status >= 400)` |

---

#### TC-055 · FR-026 · Virus scan fail → file rejected, incident logged

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-026 |
| **Given** | Virus scan mock returns `Fail` |
| **When** | `POST /documents` |
| **Then** | HTTP 422; `ClinicalDocument.VirusScanResult = Fail`; incident logged to AuditLog |
| **Assertions** | DB row with `VirusScanResult = Fail`; `AuditLog` entry for virus detection |

---

#### TC-056 · FR-027 / DR-005 · Unauthenticated request cannot access document blob

| Field | Value |
|-------|-------|
| **Type** | Security |
| **Priority** | P1 |
| **Requirement** | FR-027, DR-005 |
| **Given** | No JWT |
| **When** | `GET /documents/{id}/download` |
| **Then** | HTTP 401 |
| **Assertions** | `Assert.Equal(401, status)` |

---

#### TC-057 · NFR-006 / FR-027 · Encryption applied before write acknowledgement

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | NFR-006, FR-027 |
| **Given** | Upload in progress; check filesystem at path after HTTP 201 |
| **Then** | File bytes at `EncryptedBlobPath` are NOT a valid plaintext PDF (magic bytes `%PDF-` absent) |
| **Assertions** | `File.ReadAllBytes(path)[0..4] != "%PDF-"` |

---

### EP-007 — Clinical Data Intelligence & 360° Patient View

---

#### TC-058 · FR-028 · OCR extraction job processes uploaded document

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-028, AIR-002 |
| **Given** | `ClinicalDocument.ExtractionStatus = Pending`; Tesseract mock returns text |
| **When** | `OcrExtractionJob.ExecuteAsync(documentId, ct)` |
| **Then** | `ExtractedClinicalField` rows inserted; `ClinicalDocument.ExtractionStatus = Extracted` |
| **Assertions** | `db.ExtractedClinicalFields.Any(f => f.DocumentId == documentId)` |

---

#### TC-059 · FR-028 / AIR-002 · Low OCR confidence page → document flagged

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-028, AIR-002 |
| **Given** | Tesseract mock returns confidence `0.60` (below 0.75 threshold) |
| **When** | `OcrExtractionJob.ExecuteAsync` |
| **Then** | `ClinicalDocument.ExtractionStatus = LowConfidence`; Staff dashboard surfaced |
| **Assertions** | DB `ExtractionStatus = LowConfidence` |

---

#### TC-060 · FR-029 · Structured fields extracted from OCR text

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-029 |
| **Given** | OCR text containing recognizable vitals, chief complaint, medications, allergies |
| **When** | Field extraction pipeline runs |
| **Then** | `ExtractedClinicalField` rows with `FieldType` values: `VitalSign`, `ChiefComplaint`, `Medication`, `Allergy` |
| **Assertions** | At least one row per expected field type |

---

#### TC-061 · FR-030 · De-duplication — duplicate field retains most recent value

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-030 |
| **Given** | Two documents for same patient; same `FieldType = Medication`; different values; doc2 uploaded later |
| **When** | 360° aggregation runs |
| **Then** | Consolidated view shows doc2 medication value; duplicate flag on doc1 entry |
| **Assertions** | `360view.medications` equals doc2 value |

---

#### TC-062 · FR-031 / DR-006 · Conflict detection — contradictory allergy entries flagged

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-031, DR-006 |
| **Given** | doc1: `Allergy = Penicillin`; doc2: `Allergy = No known drug allergies` |
| **When** | Conflict detection pipeline runs |
| **Then** | `ConflictFlag` entity created referencing both `ExtractedClinicalField` IDs; `360°` view status = `RequiresReview` |
| **Assertions** | `db.ConflictFlags.Any(f => f.PatientId == patientId)` |

---

#### TC-063 · FR-031 · Staff cannot mark 360° view Verified while conflicts exist

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-031 |
| **Given** | Staff JWT; unresolved `ConflictFlag` for patient |
| **When** | `PATCH /patients/{id}/360/verify` |
| **Then** | HTTP 409; view status remains `RequiresReview` |
| **Assertions** | `Assert.Equal(409, status)` |

---

#### TC-064 · FR-032 · Staff accesses 360° view — consolidated fields displayed

| Field | Value |
|-------|-------|
| **Type** | E2E / Integration |
| **Priority** | P1 |
| **Requirement** | FR-032 |
| **Given** | Staff JWT; patient with 2 extracted documents; all conflicts resolved |
| **When** | `GET /patients/{id}/view360` |
| **Then** | HTTP 200; response contains `vitals`, `medicalHistory`, `medications`, `allergies`, `diagnosisNarratives` |
| **Assertions** | All fields present in response JSON |

---

#### TC-065 · FR-032 · 360° view served from Redis cache on second request (DR-010)

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P2 |
| **Requirement** | FR-032, DR-010 |
| **Given** | First `GET /patients/{id}/view360` populates cache |
| **When** | Second `GET /patients/{id}/view360` within 5 minutes |
| **Then** | Response served from Redis; DB query not executed (verified via query counter) |
| **Assertions** | DB query count = 0 on second call |

---

### EP-008 — AI Medical Coding & Trust-First Verification

---

#### TC-066 · FR-033 · ICD-10 generation returns top candidates with confidence scores

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-033, AIR-003 |
| **Given** | Staff JWT; verified patient; intake data present; `FallbackCodeGenerationService` active |
| **When** | `POST /patients/{id}/generate-codes?type=ICD10` |
| **Then** | HTTP 202; Hangfire `GenerateIcd10CodesJob` enqueued; after execution: `MedicalCodeSuggestion` rows with `CodeType = ICD10`, `ConfidenceScore > 0`, `Status = Pending` |
| **Assertions** | Hangfire job enqueued; DB rows with `CodeType = ICD10` |

---

#### TC-067 · FR-033 · ICD-10 LowConfidenceFlag set when confidence < 0.60

| Field | Value |
|-------|-------|
| **Type** | Unit |
| **Priority** | P1 |
| **Requirement** | FR-033 |
| **Given** | `IOllamaCodeGenerationService.GenerateIcd10Async` returns `ConfidenceScore = 0.45` |
| **When** | `GenerateIcd10CodesJob.ExecuteAsync(patientId, ct)` |
| **Then** | `MedicalCodeSuggestion.LowConfidenceFlag = true` |
| **Assertions** | `Assert.True(row.LowConfidenceFlag)` |

---

#### TC-068 · FR-033 · Code generation blocked for unverified patient

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-033 |
| **Given** | Staff JWT; patient with `VerificationStatus = Unverified` |
| **When** | `POST /patients/{id}/generate-codes?type=ICD10` |
| **Then** | HTTP 422 or 409; no Hangfire job enqueued |
| **Assertions** | `Assert.True(status >= 400)` |

---

#### TC-069 · FR-034 · CPT generation returns valid 5-digit codes only

| Field | Value |
|-------|-------|
| **Type** | Unit |
| **Priority** | P1 |
| **Requirement** | FR-034, AIR-004 |
| **Given** | `OllamaCodeGenerationService.GenerateCptAsync`; response contains `"9999"` (4-digit) + `"99213"` (valid) |
| **When** | Parsing / validation runs |
| **Then** | Only `"99213"` in results; `"9999"` discarded |
| **Assertions** | `Assert.All(results, r => Assert.Matches(@"^\d{5}$", r.SuggestedCode))` |

---

#### TC-070 · FR-034 / AIR-004 · CPT "No procedures identified" — zero rows, clean exit

| Field | Value |
|-------|-------|
| **Type** | Unit |
| **Priority** | P1 |
| **Requirement** | FR-034, AIR-004 |
| **Given** | `IOllamaCodeGenerationService.GenerateCptAsync` returns `[]` |
| **When** | `GenerateCptCodesJob.ExecuteAsync(patientId, ct)` |
| **Then** | 0 `MedicalCodeSuggestion` rows; no exception; job completes cleanly |
| **Assertions** | `Assert.Empty(await pgDb.MedicalCodeSuggestions.ToListAsync())` |

---

#### TC-071 · FR-035 · Staff accepts ICD-10 suggestion — code committed with verifier FK

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-035, AIR-005 |
| **Given** | Staff JWT; `MedicalCodeSuggestion.Status = Pending` |
| **When** | `PATCH /coding/{suggestionId} { action: "Accept", verifiedBy: staffId }` |
| **Then** | `Status = Accepted`; `VerifiedById = staffId`; `VerifiedAt` set |
| **Assertions** | DB row status; `VerifiedById != null` |

---

#### TC-072 · FR-035 · Staff modifies suggestion — modified code committed

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-035 |
| **Given** | Staff JWT; `MedicalCodeSuggestion.Status = Pending` |
| **When** | `PATCH /coding/{suggestionId} { action: "Modify", modifiedCode: "J06.9", verifiedBy: staffId }` |
| **Then** | `Status = Modified`; `CommittedCode = "J06.9"`; `VerifiedById = staffId` |
| **Assertions** | DB `CommittedCode == "J06.9"`, `Status == Modified` |

---

#### TC-073 · FR-035 / AIR-005 · Trust-First: commit without `verifiedBy` → 422

| Field | Value |
|-------|-------|
| **Type** | Security / Integration |
| **Priority** | P1 |
| **Requirement** | FR-035, AIR-005, DR-007 |
| **Given** | Staff JWT; `MedicalCodeSuggestion.Status = Pending` |
| **When** | `PATCH /coding/{suggestionId} { action: "Accept" }` (no `verifiedBy` field) |
| **Then** | HTTP 422; `Status` unchanged; no commit |
| **Assertions** | `Assert.Equal(422, status)`; DB `Status = Pending` |

---

#### TC-074 · FR-035 · Staff rejects suggestion — discarded; manual entry possible

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | FR-035 |
| **Given** | Staff JWT; `MedicalCodeSuggestion.Status = Pending` |
| **When** | `PATCH /coding/{suggestionId} { action: "Reject", verifiedBy: staffId }` |
| **Then** | `Status = Rejected`; no `CommittedCode` |
| **Assertions** | `db.MedicalCodeSuggestions.Single(s => s.Id == id).Status == Rejected` |

---

### Non-Functional Requirements

---

#### TC-075 · NFR-001 · Windows Service auto-restart after crash

| Field | Value |
|-------|-------|
| **Type** | Infrastructure |
| **Priority** | P2 |
| **Requirement** | NFR-001 |
| **When** | Kill API process; Windows Service recovery action triggers restart |
| **Then** | API responds to health check within 30 s of kill |
| **Assertions** | `GET /health` returns 200 within 30 s |

---

#### TC-076 · NFR-002 · PDF confirmation delivered ≤ 60 s

| Field | Value |
|-------|-------|
| **Type** | Performance |
| **Priority** | P1 |
| **Requirement** | NFR-002 |
| **Tool** | k6 + Hangfire job timer |
| **When** | Appointment booking under 50 VU |
| **Then** | SMTP mock receives email with PDF attachment; elapsed time from booking POST to SMTP call ≤ 60 s |
| **Assertions** | `p95(confirmationLatency) <= 60_000 ms` in k6 output |

---

#### TC-077 · NFR-003 · API P95 response time ≤ 2 s at 50 VU

| Field | Value |
|-------|-------|
| **Type** | Performance |
| **Priority** | P1 |
| **Requirement** | NFR-003 |
| **Tool** | k6 — `tests/perf/api-load.js` |
| **Scenario** | 50 VUs × 3-minute ramp; mix of `GET /slots`, `POST /appointments`, `GET /patients/{id}/view360` |
| **Then** | P95 latency ≤ 2000 ms; error rate < 1% |
| **Assertions** | k6 threshold `http_req_duration{p(95)} <= 2000` |

---

#### TC-078 · NFR-003 / NFR-010 · OCR / code-gen jobs do not block HTTP thread

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | NFR-003, NFR-010 |
| **Given** | OCR job takes ≥ 5 s (simulated by mock) |
| **When** | `POST /documents` triggers OCR job enqueue |
| **Then** | HTTP 202 returned immediately (< 500 ms); job executes asynchronously in Hangfire worker |
| **Assertions** | Response time < 500 ms; Hangfire job visible in scheduler |

---

#### TC-079 · NFR-004 · PHI encrypted at rest (AES-256)

| Field | Value |
|-------|-------|
| **Type** | Security |
| **Priority** | P1 |
| **Requirement** | NFR-004, NFR-006 |
| **When** | Inspect `EncryptedBlobPath` file after upload |
| **Then** | File is NOT a valid plaintext PDF; decrypted with AES key returns valid PDF |
| **Assertions** | Decryption succeeds; plaintext bytes start with `%PDF-` |

---

#### TC-080 · NFR-004 · TLS 1.2+ enforced — HTTP redirected to HTTPS

| Field | Value |
|-------|-------|
| **Type** | Security |
| **Priority** | P1 |
| **Requirement** | NFR-004, NFR-007 |
| **Tool** | OWASP ZAP / curl |
| **When** | `GET http://localhost:5153/api/slots` (plaintext HTTP) |
| **Then** | HTTP 301/307 redirect to HTTPS; TLS 1.2 minimum negotiated |
| **Assertions** | ZAP scan finds no mixed-content or HTTP-only endpoints |

---

#### TC-081 · NFR-007 · JWT expiry ≤ 15 min

| Field | Value |
|-------|-------|
| **Type** | Security |
| **Priority** | P1 |
| **Requirement** | NFR-007 |
| **When** | Decode JWT from login response |
| **Then** | `exp - iat <= 900 seconds` |
| **Assertions** | `Assert.True((exp - iat) <= 900)` |

---

#### TC-082 · NFR-007 · CSRF protection applied to state-changing endpoints

| Field | Value |
|-------|-------|
| **Type** | Security |
| **Priority** | P1 |
| **Requirement** | NFR-007 |
| **Tool** | OWASP ZAP CSRF scan |
| **When** | Cross-site POST to `/appointments`, `/intake/manual`, `/coding/{id}` without anti-CSRF token |
| **Then** | HTTP 400 or 403; request rejected |
| **Assertions** | ZAP active scan finds zero CSRF vulnerabilities |

---

#### TC-083 · NFR-007 · Stale JWT (post-logout) rejected

| Field | Value |
|-------|-------|
| **Type** | Security |
| **Priority** | P1 |
| **Requirement** | NFR-007 |
| **Given** | Valid JWT; patient calls `POST /auth/logout` |
| **When** | Same JWT used on any subsequent request |
| **Then** | HTTP 401; Redis key absent |
| **Assertions** | `Assert.Equal(401, status)` |

---

#### TC-084 · NFR-008 · Audit modification via API → HTTP 405

| Field | Value |
|-------|-------|
| **Type** | Security |
| **Priority** | P1 |
| **Requirement** | NFR-008 |
| **Given** | Admin JWT; known audit log entry ID |
| **When** | `DELETE /admin/audit/{id}` and `PATCH /admin/audit/{id}` |
| **Then** | HTTP 405 both; attempt itself logged in `AuditLog` |
| **Assertions** | `Assert.Equal(405, status)`; `db.AuditLog.Any(a => a.ActionType == "BLOCKED_AUDIT_MODIFICATION")` |

---

#### TC-085 · NFR-008 / DR-008 · AuditLog INSERT-only — no UPDATE/DELETE privilege at DB level

| Field | Value |
|-------|-------|
| **Type** | Data Integrity / Security |
| **Priority** | P1 |
| **Requirement** | NFR-008, DR-008 |
| **When** | Execute `UPDATE AuditLog SET ActionType = 'TAMPERED' WHERE Id = 1` as app DB user |
| **Then** | SQL error: `permission denied for table AuditLog` |
| **Assertions** | psql returns error; row unchanged |

---

#### TC-086 · NFR-009 · Hangfire retry — exponential backoff 3× then dead-letter

| Field | Value |
|-------|-------|
| **Type** | Integration |
| **Priority** | P1 |
| **Requirement** | NFR-009 |
| **Given** | `SendConfirmationEmailJob` throws exception on all attempts; backoff multiplier = 2×, base = 30 s, max = 3 |
| **When** | Job executes and fails 3 times |
| **Then** | Retry delays: ~30 s, ~60 s, ~120 s; final state = `Failed`; visible in Hangfire dashboard dead-letter queue |
| **Assertions** | Hangfire retry count = 3; state = `Failed` |

---

#### TC-087 · NFR-011 / AIR-006 · OSS-only dependency audit passes

| Field | Value |
|-------|-------|
| **Type** | Compliance |
| **Priority** | P2 |
| **Requirement** | NFR-011, AIR-006 |
| **Tool** | `dotnet list package --include-transitive`; `npm ls`; licence-checker |
| **When** | Audit runs on `main` branch |
| **Then** | No MIT/Apache/LGPL-incompatible licences; no paid API calls in code; no `new HttpClient("https://api.openai.com")` calls |
| **Assertions** | Zero licence-checker violations; grep finds zero paid AI API URLs |

---

#### TC-088 · NFR-012 / AIR-003 · AI-Human Agreement Rate ≥ 98%

| Field | Value |
|-------|-------|
| **Type** | AI Accuracy |
| **Priority** | P1 |
| **Requirement** | NFR-012, AIR-003 |
| **Tool** | Offline evaluation harness; 100 labeled patient narratives |
| **When** | `RuleCodeGenerationService` processes all 100 narratives; compare AI suggestions vs. ground-truth accepted codes |
| **Then** | Agreement Rate = (matched accepted codes / total accepted codes) × 100 ≥ 98% |
| **Assertions** | `agreementRate >= 98.0` |

---

### Data Requirements

---

#### TC-089 · DR-001 · UserAccount unique email index prevents duplicates at DB level

| Field | Value |
|-------|-------|
| **Type** | Data Integrity |
| **Priority** | P1 |
| **Requirement** | DR-001 |
| **When** | Insert two `UserAccount` rows with same `email` (bypassing app layer) via psql |
| **Then** | Second INSERT fails with unique constraint violation |
| **Assertions** | psql error `unique_violation` (code 23505) |

---

#### TC-090 · DR-002 · Appointment FSM rejects invalid status transition

| Field | Value |
|-------|-------|
| **Type** | Data Integrity |
| **Priority** | P1 |
| **Requirement** | DR-002 |
| **Given** | `Appointment.Status = Cancelled` |
| **When** | Attempt `Status = Arrived` via EF Core save |
| **Then** | `InvalidOperationException` or custom domain exception thrown; DB row unchanged |
| **Assertions** | Exception thrown; `db.Appointments.Single(a => a.Id == id).Status == Cancelled` |

---

#### TC-091 · DR-002 · Optimistic concurrency — rowversion conflict → DbUpdateConcurrencyException

| Field | Value |
|-------|-------|
| **Type** | Data Integrity |
| **Priority** | P1 |
| **Requirement** | DR-002, DR-009 |
| **Given** | Two EF Core contexts load same `Slot` row |
| **When** | Both update `IsAvailable = false` and call `SaveChangesAsync` |
| **Then** | First succeeds; second throws `DbUpdateConcurrencyException` |
| **Assertions** | `await Assert.ThrowsAsync<DbUpdateConcurrencyException>(...)` |

---

#### TC-092 · DR-009 · PHI soft-delete — record not physically deleted during retention window

| Field | Value |
|-------|-------|
| **Type** | Data Integrity / Compliance |
| **Priority** | P1 |
| **Requirement** | DR-009 |
| **When** | `DELETE /patients/{id}` (or equivalent soft-delete endpoint) |
| **Then** | Row has `DeletedAt` set; `RetainUntil = DeletedAt.AddYears(6)`; row still present in DB; not returned by default EF Core queries |
| **Assertions** | `db.UserAccounts.IgnoreQueryFilters().Single(u => u.Id == id).DeletedAt != null` |

---

#### TC-093 · DR-010 · Redis slot cache TTL = 60 s; invalidated on booking

| Field | Value |
|-------|-------|
| **Type** | Data Integrity |
| **Priority** | P1 |
| **Requirement** | DR-010 |
| **Given** | First `GET /slots?date=X` populates Redis cache with TTL=60 s |
| **When** | `POST /appointments { slotId }` books a slot |
| **Then** | Redis cache for that date key is invalidated (DEL or TTL=0); next `GET /slots?date=X` fetches from DB |
| **Assertions** | After booking, Redis key absent; DB query executed on next fetch |

---

## 8. Security Test Coverage (OWASP Top 10)

| OWASP Risk | Relevant Test Cases | Mitigation |
|------------|---------------------|------------|
| A01 Broken Access Control | TC-008, TC-009, TC-047 | Server-side RBAC; JWT role claim checked at every endpoint |
| A02 Cryptographic Failures | TC-057, TC-079, TC-080 | AES-256 at rest; TLS 1.2+ in transit; no plaintext PHI |
| A03 Injection | SQL: parameterised EF Core queries; XSS: Angular DomSanitizer | OWASP ZAP active scan |
| A04 Insecure Design | TC-073 (Trust-First at API) | `verified_by` required server-side |
| A05 Security Misconfiguration | TC-080, TC-082 | HTTPS-only; CSRF tokens; Swagger disabled in Production |
| A07 Auth Failures | TC-010, TC-015, TC-081, TC-083 | Short-lived JWT; Redis-backed revocation; lockout after 5 failures |
| A08 Software Integrity | TC-087 | OSS licence audit; dependency pinning in `packages.lock.json` |
| A09 Logging Failures | TC-012, TC-013, TC-014, TC-084 | Immutable audit log; RBAC violations logged; modification attempts blocked |

---

## 9. Entry Criteria

| # | Criterion |
|---|-----------|
| 1 | API builds clean (`dotnet build` zero errors) |
| 2 | Angular SPA builds clean (`ng build --configuration=production` zero errors) |
| 3 | EF Core migrations applied to `clinical_dev` PostgreSQL and SQL Server AppDb |
| 4 | Seed data present: admin, staff, patient accounts; 14 days of slots |
| 5 | Hangfire dashboard accessible at `/hangfire` (Admin role) |
| 6 | All unit tests pass: `dotnet test` zero failures |

---

## 10. Exit Criteria

| # | Criterion |
|---|-----------|
| 1 | All P1 test cases pass |
| 2 | Unit test line coverage ≥ 90% per service/job class |
| 3 | API P95 latency ≤ 2000 ms under 50 VU k6 load |
| 4 | Zero OWASP ZAP critical or high findings |
| 5 | AI-Human Agreement Rate ≥ 98% on evaluation harness |
| 6 | No open P1 defects; all P2 defects triaged with owner and target sprint |

---

## 11. Defect Classification

| Severity | Criteria | SLA to Fix |
|----------|----------|------------|
| P1 Critical | Security vulnerability, data loss, PHI exposure, application crash, HIPAA violation | Same sprint |
| P2 High | Feature blocked, incorrect data persisted, failed audit log entry | Next sprint |
| P3 Medium | Incorrect UI label, non-critical edge case failure | Backlog |
| P4 Low | Cosmetic / accessibility issue | Backlog |

---

## 12. Test Commands Reference

```powershell
# Run all unit tests
dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\

# Run tests by epic filter
dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~GenerateIcd10CodesJobTests"
dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~GenerateCptCodesJobTests"

# Run with code coverage
dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --collect:"XPlat Code Coverage"

# Run k6 performance tests (requires k6 installed)
k6 run tests\perf\api-load.js

# Run Playwright E2E tests
cd tests\e2e
npx playwright test

# OWASP ZAP baseline scan (Docker)
docker run -t owasp/zap2docker-stable zap-baseline.py -t http://localhost:5153
```

---

## 13. Documentation References

| Reference | Link |
|-----------|------|
| Requirements Specification | `.propel/context/docs/spec.md` |
| Architecture & NFRs | `.propel/context/docs/design.md` |
| Epic Definitions | `.propel/context/docs/epics.md` |
| BRD | `BRD.md` |
| ICD-10 Unit Test Plan | `.propel/context/tasks/EP-008/us_045/unittest/test_plan_be_icd10-generation-ollama-biomistral.md` |
| CPT Unit Test Plan | `.propel/context/tasks/EP-008/us_046/unittest/test_plan_be_cpt-generation-ollama-biomistral.md` |
| xUnit Docs | https://xunit.net/docs/getting-started/netcore/cmdline |
| Moq Quickstart | https://github.com/moq/moq4/wiki/Quickstart |
| Playwright TypeScript | https://playwright.dev/docs/intro |
| k6 Load Testing | https://k6.io/docs/ |
| OWASP ZAP | https://www.zaproxy.org/docs/ |
