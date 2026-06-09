# Epic - Unified Patient Access & Clinical Intelligence Platform

## Epic Summary Table

| Epic ID | Epic Title | Mapped Requirement IDs |
|---------|------------|------------------------|
| EP-TECH | Platform Infrastructure & OSS Stack | TR-001, TR-002, TR-003, TR-004, TR-005, TR-006, TR-017, TR-018, NFR-001, NFR-003, NFR-009, NFR-011 |
| EP-DATA | Data Entity Scaffolding & Persistence Layer | DR-001, DR-002, DR-003, DR-004, DR-005, DR-006, DR-007, DR-008, DR-009, DR-010 |
| EP-001 | User Authentication, RBAC & Compliance | FR-001, FR-002, FR-003, FR-004, FR-005, FR-006, NFR-004, NFR-005, NFR-007, NFR-008, TR-010 |
| EP-002 | Appointment Booking & Scheduling | FR-007, FR-008, FR-009, FR-010, FR-011 |
| EP-003 | Notifications & Calendar Integration | FR-012, FR-013, FR-014, FR-023, FR-024, NFR-002, TR-012, TR-013, TR-014, TR-015 |
| EP-004 | Patient Intake — AI-Assisted & Manual | FR-015, FR-016, FR-017, FR-018, AIR-001, TR-008 |
| EP-005 | Staff Operations & Queue Management | FR-019, FR-020, FR-021, FR-022, FR-025 |
| EP-006 | Clinical Document Management | FR-026, FR-027, NFR-006, NFR-010, TR-011, TR-016 |
| EP-007 | Clinical Data Intelligence & 360° Patient View | FR-028, FR-029, FR-030, FR-031, FR-032, AIR-002, AIR-006, TR-007 |
| EP-008 | AI Medical Coding & Trust-First Verification | FR-033, FR-034, FR-035, AIR-003, AIR-004, AIR-005, TR-009, NFR-012 |

**Notes:**

1. Green-field project: EP-TECH and EP-DATA are auto-generated infrastructure epics (`[SOURCE:INFERRED]`).
2. No `[UNCLEAR]` requirements detected — all 81 requirements are mapped.
3. No UXR requirements present — `figma_spec` artifact is not yet produced; UI Impact reflects current state.
4. Every requirement appears in exactly one epic. Zero orphans. Zero duplicates.
5. After EP-DATA is complete, EP-001 through EP-008 are fully independent and can execute in parallel.

---

## Epic Description

### EP-TECH: Platform Infrastructure & OSS Stack

**Business Value**: Establishes the complete technical foundation — Angular SPA scaffolding, .NET 8 Web API skeleton, CI/CD pipeline, all OSS runtime dependencies, deployment targets, structured logging, and development toolchain — enabling every feature epic to begin immediately after completion. Without this epic, no code can be written, tested, or deployed.

**Description**: This green-field infrastructure epic bootstraps the entire system. It covers Angular 17 project initialisation with routing and RBAC guards, .NET 8 vertical-slice API project structure with Swashbuckle OpenAPI, IIS deployment manifests, Netlify/Vercel static build configuration, Hangfire worker registration, GitHub Actions CI/CD pipeline (build → test → deploy), Serilog structured logging with Seq CE, Upstash Redis StackExchange.Redis client configuration, and all NuGet/npm package version pinning to the OSS-only constraint (NFR-011). No feature logic is included — only wiring, configuration, and the empty module structure that feature epics populate.

**UI Impact**: No

**Screen References**: N/A

**Key Deliverables**:
- Angular 17 project scaffold: routing module, RBAC route guards (stub), role-aware layout shell, HTTPS-enforced build config, Netlify/Vercel deploy config
- .NET 8 ASP.NET Core Web API project: vertical-slice feature folder structure, Program.cs DI wiring, Swashbuckle OpenAPI enabled, health check endpoint
- SQL Server 2022 Express: EF Core 8 project scaffolding, DbContext base, migration runner, AuditLog INSERT-only GRANT scripts
- PostgreSQL 16: separate ClinicalDbContext scaffolded, Npgsql EF Core provider configured, no shared transaction scope enforced by design
- Upstash Redis: StackExchange.Redis client registered, connection string from environment variables, cache-aside helper registered
- Hangfire 1.8.x: SQL Server-backed job store registered, recurring job infrastructure, Admin-only dashboard route configured
- GitHub Actions CI/CD: build, unit test, Angular build, .NET publish, deploy-to-IIS and deploy-to-Netlify workflows
- Serilog: rolling file sink (daily, 30-day retention) + Seq CE sink, correlation ID middleware, PHI-safe log filter
- OSS audit: all dependencies confirmed MIT/Apache/LGPL/GPL — no paid licence, no paid API cost
- Environment secrets: `.env.example` template; `.gitignore` rules for credentials; Windows DPAPI config for production key storage

**Dependent EPICs**:
- None

---

### EP-DATA: Data Entity Scaffolding & Persistence Layer

**Business Value**: Defines and migrates all domain entity schemas across SQL Server and PostgreSQL, enforces referential integrity, unique partial indexes, FSM-compliant status constraints, and PHI retention rules. Every feature epic requires at least one entity from this epic; it is the critical-path dependency for the entire feature backlog.

**Description**: Implements all 10 data requirements (DR-001–DR-010) across both databases. SQL Server entities: UserAccount (DR-001), Appointment (DR-002), WaitlistEntry (DR-003), IntakeRecord (DR-004), AuditLog (DR-008). PostgreSQL entities: ClinicalDocument (DR-005), ExtractedClinicalField + ConflictFlag (DR-006), MedicalCodeSuggestion (DR-007). Cross-cutting: soft-delete retention flag for 6-year HIPAA minimum (DR-009), Upstash Redis TTL configuration for three cache entry types (DR-010). Includes EF Core migrations, seed data for InsuranceDummyRecord lookup table, database index scripts, and the INSERT-only SQL GRANT for AuditLog.

**UI Impact**: No

**Screen References**: N/A

**Key Deliverables**:
- SQL Server migrations: UserAccount (unique email index, role enum, status enum), Appointment (FSM status enum, rowversion for optimistic concurrency, noShowRiskScore int), WaitlistEntry (unique partial index — one Active entry per patient), IntakeRecord (version column, structured fields JSONB), AuditLog (append-only; no UPDATE/DELETE grants)
- PostgreSQL migrations: ClinicalDocument (encryptedBlobPath, virusScanResult enum, extractionStatus enum), ExtractedClinicalField (fieldType enum, confidenceScore float), ConflictFlag (resolution enum), MedicalCodeSuggestion (codeType enum, status enum, verifiedById FK with NOT NULL enforcement for accepted/modified transitions)
- Soft-delete column (`deletedAt`, `retainUntil`) on all PHI-bearing entities; deletion guard in EF Core SaveChanges override (DR-009)
- Upstash Redis TTL constants in shared configuration: session=900s, slots=60s, 360view=300s (DR-010)
- InsuranceDummyRecord seed data file (50+ sample records for intake pre-check)
- Database integration test harness: Testcontainers SQL Server + PostgreSQL instances for migration smoke tests

**Dependent EPICs**:
- EP-TECH — Foundational — Requires project scaffolding, EF Core packages, and DbContext wiring from EP-TECH before migrations can run

---

### EP-001: User Authentication, RBAC & Compliance

**Business Value**: Delivers the security and identity foundation that HIPAA §164.312 mandates: role-gated access, immutable audit trail, session control, and password lifecycle management. No other feature can go to production without authenticated, role-scoped sessions. This epic is also the direct compliance gate for the entire platform.

**Description**: Implements all authentication and authorisation flows across three roles (Patient, Staff, Admin). Covers patient self-registration with email verification (FR-001), Admin-managed Staff/Admin account creation and lifecycle (FR-002), RBAC enforcement at every API endpoint with HTTP 403 on cross-role access (FR-003), 15-minute server-side inactivity timeout with forced re-authentication (FR-004), immutable append-only audit logging for every CRUD action across all roles (FR-005), and password-reset via time-limited email token (FR-006). Security controls: HIPAA §164.312 technical safeguards including AES-256 PHI at rest and TLS 1.2+ in transit (NFR-004), server-side RBAC with HTTP 403 + audit log on violation (NFR-005), JWT ≤15 min expiry with Redis-backed token allowlist — rejected on absence regardless of signature validity (NFR-007), and append-only audit table with HTTP 405 on any modification attempt (NFR-008). Identity stack: ASP.NET Core Identity with PBKDF2 SHA-256 100k iterations; JWT Bearer via TR-010.

**UI Impact**: No

**Screen References**: N/A

**Key Deliverables**:
- Patient self-registration endpoint: `POST /auth/register` — duplicate email 409, INSERT with `emailVerified=false`, verification email dispatched via Hangfire
- Email verification endpoint: `GET /auth/verify-email?token=` — token TTL 24h, single-use, sets `emailVerified=true`, status=Active
- Resend verification endpoint: `POST /auth/resend-verification`
- Login endpoint: `POST /auth/login` — PBKDF2 verify, JWT issued (exp=15min), Redis SET token:{jti} EX 900; account lockout after 5 failures for 15 minutes
- Token validation middleware: Redis GET token:{jti} on every authenticated request; nil → HTTP 401; hit → EXPIRE reset
- Session extension endpoint: `POST /auth/extend-session` — Redis EXPIRE reset
- Logout endpoint: `POST /auth/logout` — Redis DEL token:{jti}
- RBAC policy definitions: Patient/Staff/Admin policies applied via `[Authorize(Policy="...")]` on all controllers
- HTTP 403 handler: logs violation to AuditLog before response
- Admin user management endpoints: `POST /admin/users`, `PATCH /admin/users/{id}` — last-active-Admin guard (409 on deactivation attempt)
- Password reset: `POST /auth/forgot-password` → token email; `POST /auth/reset-password` — 60-min TTL, single-use token invalidation
- AuditLog middleware: intercepts all state-changing responses, appends AuditLog entry (INSERT-only, no UPDATE/DELETE routes)
- HTTP 405 guard on `DELETE /audit` and `PATCH /audit` — itself logged
- Angular route guards: `AuthGuard` + `RoleGuard` for Patient/Staff/Admin navigation paths; redirect to `/login?reason=unauthorized` on failure

**Dependent EPICs**:
- EP-DATA — Foundational — Requires UserAccount entity, AuditLog table, and Redis TTL configuration from EP-DATA

---

### EP-002: Appointment Booking & Scheduling

**Business Value**: Delivers the core booking workflow that directly generates patient throughput and operational revenue. Enables patients to browse, book, and manage appointments while automating waitlist fulfillment and no-show prediction — the primary levers for reducing missed appointments and maximising slot utilisation.

**Description**: Covers the complete appointment lifecycle for patients: browsing available slots with Redis-cached availability (FR-007), joining a single active waitlist entry per patient for a preferred unavailable slot (FR-008), automated slot-swap via Hangfire SwapMonitor when a preferred slot is released — with patient opt-out window of configurable default 2 hours (FR-009), rule-based no-show risk score calculated at booking time from prior no-show history, booking lead time, and intake completion status and displayed to Staff (FR-010), and patient-initiated cancellation or reschedule up to a configurable cutoff time with slot release back to pool (FR-011). Implements optimistic concurrency via EF Core rowversion on Slot entity to prevent double-booking under concurrent requests.

**UI Impact**: No

**Screen References**: N/A

**Key Deliverables**:
- Slot availability endpoint: `GET /slots?date=` — Redis cache-aside (TTL 60s); cache invalidated on every booking/cancellation
- Booking endpoint: `POST /appointments` — rowversion optimistic lock; 409 on concurrency conflict; no-show risk score calculation (rule engine); enqueues confirmation email job + reminder schedule jobs (48h, 2h)
- Waitlist registration endpoint: `POST /waitlist` — unique partial index guard (one Active entry per patient); update-or-insert logic
- Slot-swap Hangfire job: `SwapMonitorJob` — triggered on slot release; queries waitlist ordered by `registeredAt ASC`; sends offer via email + SMS; 2-hour response window; Accept/Decline endpoints; ROLLBACK + Expired status on slot taken by another booking
- Cancellation/reschedule endpoint: `DELETE /appointments/{id}` / `PATCH /appointments/{id}/reschedule` — cutoff window enforced; released slot triggers swap check; pending reminder jobs cancelled
- No-show risk score engine: rule-based scoring (prior no-show count, lead time in days, intake status); score stored on Appointment entity; exposed on Staff schedule views
- Appointment FSM validator: EF Core SaveChanges interceptor enforces valid status transitions (Scheduled→Arrived, Arrived→InProgress, etc.)
- Concurrency integration tests: parallel booking requests against same slot; exactly one succeeds

**Dependent EPICs**:
- EP-DATA — Foundational — Requires Appointment, Slot, WaitlistEntry entities and Redis slot-cache TTL from EP-DATA

---

### EP-003: Notifications & Calendar Integration

**Business Value**: Drives patient engagement and appointment adherence through timely confirmations, multi-channel reminders, and frictionless calendar sync. Directly reduces no-show rates. Produces the PDF confirmation attachment mandated by the NFR-002 60-second SLA and enables integration with Google Calendar and Microsoft Outlook at zero marginal cost.

**Description**: Covers the full notification and calendar surface: confirmation email with QuestPDF-generated PDF attachment dispatched within 60 seconds of booking (FR-014, NFR-002); automated email reminders at configurable intervals defaulting to T-48h and T-2h (FR-024); automated SMS reminders at the same intervals via Twilio/Vonage sandbox behind `ISmsGateway` (FR-023); one-click Google Calendar event creation via Google Calendar API v3 OAuth2+PKCE (FR-012); one-click Microsoft Outlook event creation via Microsoft Graph API v1.0 OAuth2+PKCE (FR-013). All email via MailKit 4.x SMTPS/STARTTLS with SMTP credentials in environment variables (TR-012). SMS via `ISmsGateway` abstraction (TR-013). OAuth tokens stored AES-256 encrypted per patient (TR-014). PDF generation via QuestPDF 2024.x ≤2s, streamed directly to attachment (TR-015).

**UI Impact**: No

**Screen References**: N/A

**Key Deliverables**:
- QuestPDF appointment confirmation template: appointment ID, date, time, location, provider; PDF ≤2s; streamed to MailKit attachment — no intermediate filesystem write
- Confirmation email Hangfire job: `SendConfirmationEmailJob` — enqueued immediately on booking; exponential backoff 3× (base 30s, 2× multiplier); dead-letter surfaced in Staff dashboard (NFR-009)
- T-48h and T-2h reminder Hangfire scheduled jobs: `Send48hReminderJob`, `Send2hReminderJob`; jobs cancelled on appointment cancellation/reschedule
- MailKit 4.x SMTP client: SMTPS port 465 or STARTTLS port 587; credentials from environment variables; retry on transient failure
- SMS gateway abstraction: `ISmsGateway` interface + Twilio/Vonage sandbox implementation; credentials from environment variables; retry via Hangfire backoff
- Google Calendar OAuth2+PKCE flow: `GET /calendar/oauth/google`, `GET /calendar/callback`; access + refresh tokens stored AES-256 per patient; event creation via `POST /calendars/primary/events`
- Microsoft Graph OAuth2+PKCE flow: equivalent endpoints for Outlook; Graph API v1.0 event creation
- Calendar sync endpoint: `POST /appointments/{id}/calendar-sync {provider}` — patient-facing, post-booking; graceful 503 on calendar API rate-limit or timeout with retry hint
- Email + SMS integration tests: MailKit SMTP mock; `ISmsGateway` mock; PDF byte length assertion

**Dependent EPICs**:
- EP-DATA — Foundational — Requires Appointment entity, patient contact fields, and slot detail from EP-DATA

---

### EP-004: Patient Intake — AI-Assisted & Manual

**Business Value**: Collects structured pre-visit clinical data that powers the downstream 360° patient view and AI medical coding pipeline. Reduces administrative burden at point-of-care by capturing demographics, chief complaint, medical history, medications, allergies, and insurance details before the appointment — directly enabling the clinical intelligence features in EP-007 and EP-008.

**Description**: Delivers dual intake pathways for patients. AI conversational intake (FR-015, AIR-001): Rasa NLU 3.x locally deployed as Windows Service, accessible only on localhost (TR-008); dialogue-driven field extraction with clarification prompts when response confidence < 0.70; patient reviews and confirms structured summary before storage — Trust-First patient confirmation gate. Manual intake form (FR-016): static form collecting identical fields as fallback or preference; partial AI-session fields preserved on mid-flow switch. Intake editing up to check-in cutoff (FR-017): versioned IntakeRecord with audit log entry on every edit. Insurance pre-check (FR-018): soft validation against InsuranceDummyRecord lookup — non-blocking warning on mismatch, no real payer connectivity.

**UI Impact**: No

**Screen References**: N/A

**Key Deliverables**:
- AI intake session start: `POST /intake/ai/start {appointmentId}` — creates Rasa session, returns initial prompt
- AI intake turn endpoint: `POST /intake/ai/turn {sessionId, message}` — proxies to Rasa REST webhook `localhost:5005`; returns next prompt + captured fields
- AI intake confirm endpoint: `POST /intake/ai/confirm {sessionId, fields}` — patient confirms structured summary; `INSERT IntakeRecord (source=AI, version=1)`
- Mid-flow switch endpoint: `POST /intake/ai/switch-to-manual {sessionId}` — preserves captured fields, pre-fills manual form
- Manual intake endpoint: `POST /intake/manual {appointmentId, fields}` — required field validation → 422 on missing; `INSERT IntakeRecord (source=Manual, version=1)`
- Intake edit endpoint: `PATCH /intake/{intakeId}` — version increment; INSERT new version + AuditLog entry; prior versions retained
- Insurance pre-check endpoint: `POST /intake/insurance-check {providerName, insuranceId}` — SELECT InsuranceDummyRecord; returns Validated/NotVerified/Skipped indicator; never blocks intake submission
- Rasa service integration: .NET HttpClient factory targeting `localhost:5005`; request timeout configuration; 503 fallback message if service unreachable
- Intake versioning: IntakeRecord `version` int + soft previous-version chain; EF Core query filter exposes only latest by default
- Intake audit: AuditLog entry written on every PATCH with before/after snapshot of structuredFields JSON

**Dependent EPICs**:
- EP-DATA — Foundational — Requires IntakeRecord, InsuranceDummyRecord entities from EP-DATA

---

### EP-005: Staff Operations & Queue Management

**Business Value**: Equips Staff with the real-time operational tools needed to manage patient flow on the day of service — walk-in registration, queue visibility and reordering, arrival check-in, daily schedule, and proactive no-show risk outreach. Reduces manual coordination overhead and enables same-day capacity utilisation without prior booking.

**Description**: Covers the full Staff operational surface for a working day. Walk-in registration (FR-019): Staff can search existing patient profiles by name/DOB or create a minimal patient profile, then immediately assign the patient to the same-day queue — no prior booking required; capacity override supported with override flag. Queue management (FR-020): real-time queue view with position and estimated wait time; drag-to-reorder with optimistic concurrency guard (409 on stale-data conflict); entry removal. Patient arrival check-in (FR-021): Staff marks patient as Arrived from daily schedule or queue; self-check-in via any mechanism is explicitly prohibited; action recorded in audit log; override available for duplicate check-in attempt. Daily schedule view (FR-022): all appointments for current date with status indicators (Scheduled, Arrived, InProgress, Completed, NoShow, Cancelled). No-show risk dashboard (FR-025): visual alert flag on high-risk appointments; Staff can record outreach note and mark NoShow; no-show history feeds future risk score calculations.

**UI Impact**: No

**Screen References**: N/A

**Key Deliverables**:
- Patient search endpoint: `GET /patients/search?q=` — name LIKE or exact DOB match; returns profile list
- Minimal patient profile endpoint: `POST /patients/minimal {name, dob, contact}` — sets `needsFullRegistration=true` on UserAccount
- Walk-in queue registration: `POST /queue/walkin {patientId, staffId}` — capacity check; override flag support; AuditLog entry
- Queue view endpoint: `GET /queue/today` — ordered by position; estimated wait time calculated from average service time
- Queue reorder endpoint: `PATCH /queue/reorder {newOrder}` — EF Core rowversion optimistic lock; 409 on concurrent edit with refresh hint
- Queue entry removal: `DELETE /queue/{entryId}` — resequences remaining positions
- Patient check-in endpoint: `PATCH /appointments/{id}/checkin {staffId}` — status→Arrived; auto-removes from queue; AuditLog entry; 409 on already-arrived with forceOverride option
- Daily schedule endpoint: `GET /schedule/today` — all appointments with status enum display
- No-show risk filter: `GET /schedule/today?filter=high-risk` — appointments with `noShowRiskScore >= threshold`
- Outreach recording: `PATCH /appointments/{id}/outreach {note, staffId}`
- Status update endpoint: `PATCH /appointments/{id}/status {status, staffId}` — NoShow transition; AuditLog entry
- Queue concurrency integration tests: parallel reorder requests assert exactly one succeeds with 409 on the other

**Dependent EPICs**:
- EP-DATA — Foundational — Requires Appointment, UserAccount, and AuditLog entities from EP-DATA

---

### EP-006: Clinical Document Management

**Business Value**: Provides the secure, encrypted document ingestion pipeline that feeds all downstream clinical intelligence (EP-007) and AI coding (EP-008) features. Enforces HIPAA §164.312 encryption-at-rest and virus-scanning requirements before any document byte touches storage, protecting the organisation from malware and compliance exposure.

**Description**: Covers the complete document upload and secure storage pipeline. Patient PDF upload (FR-026): configurable file size limit, PDF MIME type validation, ClamAV daemon virus scan via nClam TCP before any storage action — 503 rejection if daemon unreachable (TR-016). AES-256-CBC encryption at rest before write acknowledgement (FR-027, NFR-006): `System.Security.Cryptography.Aes`; key from environment variable or Windows DPAPI; no plaintext byte in any buffer post-encryption step. Asynchronous OCR pipeline trigger: Hangfire OCR job enqueued immediately after successful upload — upload response never waits for OCR (NFR-010). Document metadata persisted in SQL Server ClinicalDocument entity; encrypted blob stored on filesystem with only path in DB.

**UI Impact**: No

**Screen References**: N/A

**Key Deliverables**:
- Document upload endpoint: `POST /documents/upload` (multipart) — MIME validation (application/pdf required); file size limit from configuration; nClam ClamAV scan; AES-256-CBC encrypt; filesystem write; INSERT ClinicalDocument; Hangfire OCR job enqueued; 201 on success
- ClamAV integration: nClam TCP client; 503 response + AuditLog entry on daemon unavailable; silent bypass of scan is not permitted (TR-016)
- AES-256-CBC encryption service: `IDocumentEncryptionService` — key loaded from environment / DPAPI; encrypt-then-write; decrypt-on-read; key never written to source control or appsettings
- Encrypted blob filesystem writer: configurable base path; GUID-named blob files; path stored in `ClinicalDocument.encryptedBlobPath`
- Document retrieval endpoint: `GET /documents/{id}` — role-gated (owning patient or authorised Staff only); decrypt on-the-fly; streamed response; never writes decrypted temp file
- HTTP 415 on unsupported file format; HTTP 422 on infected file (virusScan=Fail logged)
- Hangfire OCR job enqueue: `EnqueueOcrJob(documentId)` — immediately on upload success; independent of upload response
- Virus scan result stored on ClinicalDocument: Pass/Fail/Pending enum
- Security integration tests: ClamAV daemon mock; infected file upload asserts 422; daemon-unavailable asserts 503; plaintext bytes asserted absent from filesystem and DB

**Dependent EPICs**:
- EP-DATA — Foundational — Requires ClinicalDocument entity, encrypted filesystem path pattern, and Hangfire job store from EP-DATA

---

### EP-007: Clinical Data Intelligence & 360° Patient View

**Business Value**: Transforms raw encrypted document blobs into a structured, de-duplicated, conflict-resolved 360° patient view that Staff can access in a single screen — enabling informed clinical decision-making and satisfying the prerequisite for AI medical coding. Directly addresses the core clinical problem stated in the BRD: fragmented, inaccessible patient histories.

**Description**: Implements the full OCR-to-aggregation-to-360view AI pipeline. OCR extraction (FR-028, AIR-002, TR-007): Hangfire OCR job reads encrypted blob, decrypts, passes to Tesseract 5.x via .NET P/Invoke; per-page confidence evaluated against threshold 0.75; fields with confidence ≥ 0.75 stored as Extracted; below threshold stored as LowConfidence and flagged for Staff manual review; extraction retries with exponential backoff 3× (NFR-009). Field extraction (FR-029): NLP logic maps Tesseract text to typed clinical fields (VitalSign, MedicalHistory, Medication, Allergy, Diagnosis). De-duplication (FR-030): Hangfire aggregation job triggered after OCR; retains most recent non-conflicting value per field type. Conflict detection (FR-031): same-field-type value conflicts produce ConflictFlag records (Unresolved); Staff must resolve before 360° view can be marked Verified. 360° patient view (FR-032): Redis cache-aside (TTL 5 min, invalidated on any new extraction); cache miss falls through to PostgreSQL aggregation query; Staff can mark view as Verified only when all ConflictFlags are Resolved or Dismissed. OSS-only constraint enforced across all AI components (AIR-006).

**UI Impact**: No

**Screen References**: N/A

**Key Deliverables**:
- Hangfire OCR job: `ExecuteOcrJob(documentId)` — decrypt blob; Tesseract 5.x P/Invoke ExtractText; per-page confidence evaluation; ExtractedClinicalField INSERT; ClinicalDocument status update (Extracted/LowConfidence/NoData/Failed); trigger aggregation job on success
- Tesseract 5.x integration: Tesseract .NET NuGet package; LSTM engine; runs in Hangfire context — never in HTTP pipeline; raw text + page confidence scores stored before NLP step
- NLP field extraction: typed field mapping (fieldType enum) from Tesseract raw text; confidence score propagation to ExtractedClinicalField records
- Hangfire aggregation job: `ExecuteAggregationJob(patientId)` — DeduplicateByFieldType (most recent wins); DetectConflicts (same field type, different values); INSERT ConflictFlag (Unresolved); Redis cache invalidation `360_view:{patientId}`
- 360° view endpoint: `GET /patients/{id}/360-view` — Redis GET; cache miss → PostgreSQL de-duplicated query; SET cache EX 300; returns view + conflictFlags[]
- Conflict resolution endpoint: `PATCH /conflicts/{id}/resolve {selectedFieldId, staffId}` — UPDATE ConflictFlag resolution=Resolved; AuditLog entry
- Conflict dismiss endpoint: `PATCH /conflicts/{id}/dismiss {staffId}` — UPDATE resolution=Dismissed; AuditLog entry; flag remains visible
- 360° view verify endpoint: `PATCH /patients/{id}/360-view/verify {staffId}` — 409 if any ConflictFlag still Unresolved; UPDATE status=Verified + verifiedById
- Staff manual review dashboard items: ClinicalDocument records with extractionStatus=LowConfidence surfaced; LowConfidence fields shown with confidence scores
- Dead-letter surface: OCR jobs that exhaust 3 retries appear in Staff dashboard alert
- OCR pipeline integration tests: Tesseract mock; confidence threshold branch coverage; aggregation job conflict detection assertions

**Dependent EPICs**:
- EP-DATA — Foundational — Requires ClinicalDocument, ExtractedClinicalField, ConflictFlag entities, Redis 360-view TTL, and Hangfire job store from EP-DATA

---

### EP-008: AI Medical Coding & Trust-First Verification

**Business Value**: Automates the labour-intensive ICD-10 and CPT medical coding task using a locally-hosted medical LLM (BioMistral 7B), presenting Staff with ranked code suggestions they can Accept, Modify, or Reject — reducing coding time while enforcing the Trust-First constraint that no AI output reaches the patient record without explicit human sign-off. Targets the NFR-012 KPI: ≥98% AI-Human Agreement Rate measured as a 30-day rolling metric visible in the Admin dashboard.

**Description**: Implements the full ICD-10/CPT code generation and Trust-First verification pipeline. ICD-10 mapping (FR-033, AIR-003): Hangfire code generation job queries verified 360° clinical fields (status=Verified); sends ICD-10 prompt template + diagnosis narratives to Ollama BioMistral 7B `localhost:11434`; returns top-3 candidate codes ranked by confidence; stored as MedicalCodeSuggestion (status=Pending). CPT mapping (FR-034, AIR-004): same job, CPT prompt template + procedure data; "No procedures identified" surfaced when no mappable procedure present — no zero-confidence suggestion inserted. Trust-First verification (FR-035, AIR-005): Staff review interface shows all Pending suggestions with code, description, confidence, lowConfidence flag; PATCH endpoint requires non-null `verifiedById` Staff user ID — HTTP 422 if absent (enforced at .NET middleware layer); Staff can Accept (committed as-is), Modify (custom code committed), or Reject (suggestion discarded). Batch accept-all supported with client-side confirmation prompt; server-side commits all with `verifiedById`. Coding-complete gate: all suggestions must be actioned before `coding_status=Complete` set on appointment (409 if pending remain). Agreement rate metric: Admin dashboard aggregates `verifiedCode == suggestedCode` proportion over rolling 30-day window. OSS-only: Ollama + BioMistral 7B Q4_K_M quantization; no external API calls (TR-009, AIR-006).

**UI Impact**: No

**Screen References**: N/A

**Key Deliverables**:
- Code generation trigger: `POST /coding/generate {patientId, appointmentId}` — verifies 360° view status=Verified; enqueues Hangfire CodeGenJob; returns 202 {jobId}
- Hangfire code generation job: `ExecuteCodeGenJob(patientId, fields)` — ICD-10 prompt template + narratives → `POST /api/chat` Ollama localhost:11434; CPT prompt template + procedures → same; INSERT MedicalCodeSuggestion[] (status=Pending, confidenceScore); retry backoff 3× on Ollama unavailable
- Ollama client: HttpClient factory targeting `localhost:11434`; OpenAI-compatible `/api/chat` request format; version-controlled ICD-10 and CPT prompt templates
- Suggestions retrieval: `GET /coding/suggestions {appointmentId}` — SELECT MedicalCodeSuggestion WHERE status=Pending; response includes lowConfidence flag when score below configurable threshold (default 0.6)
- Trust-First verification middleware: `TrustFirstVerificationMiddleware` — intercepts `PATCH /coding/suggestions/{id}`; asserts `verifiedById` non-null Staff user ID; HTTP 422 with descriptive error if absent
- Verification endpoint: `PATCH /coding/suggestions/{id} {action, verifiedById, committedCode?}` — UPDATE status=Accepted/Modified/Rejected, verifiedById, verifiedAt, committedCode; AuditLog entry
- Accept-all endpoint: `POST /coding/accept-all {appointmentId, verifiedById}` — Trust-First middleware validation; bulk UPDATE; client-side confirmation prompt mandated
- Coding-complete endpoint: `POST /coding/complete {appointmentId, verifiedById}` — COUNT pending=0 check; 409 with pendingIds[] if any remain; UPDATE coding_status=Complete
- AI-Human Agreement Rate metric: Admin endpoint `GET /admin/metrics/code-agreement?days=30` — aggregates Accepted+Modified suggestions where `committedCode == suggestedCode` / total actioned; displayed on Admin dashboard
- Dead-letter surface: CodeGen jobs exhausting 3 retries appear in Staff dashboard with "Code generation pending — engine unavailable" message
- Trust-First integration tests: 422 asserted on missing verifiedById; 200 on valid verifiedById; accept-all with verifiedById bulk commit assertion; 409 on coding-complete with pending suggestions remaining

**Dependent EPICs**:
- EP-DATA — Foundational — Requires MedicalCodeSuggestion entity, Appointment entity, and Hangfire job store from EP-DATA

