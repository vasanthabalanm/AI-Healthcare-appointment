# Architecture Design

## Project Overview

The **Unified Patient Access & Clinical Intelligence Platform** is a standalone, end-to-end healthcare operations system for a single clinic or healthcare organization. It serves three roles — Patient, Staff (front desk/call center), and Admin — across two primary capability domains:

1. **Patient Access:** Self-service appointment booking with dynamic waitlist/slot-swap, rule-based no-show risk scoring, flexible AI-guided or manual intake, insurance pre-check, automated SMS/email reminders, Google/Outlook calendar sync, and centralized Staff-controlled arrival and queue management.

2. **Clinical Intelligence:** Secure patient document upload, OCR-backed text extraction, multi-document de-duplication and conflict detection, 360° patient view assembly, and AI-assisted ICD-10/CPT code generation with mandatory Staff verification before any code is committed to the patient record (Trust-First design).

The system is a greenfield deployment targeting free and open-source infrastructure only (Netlify/Vercel/GitHub Codespaces for static hosting, Windows Services/IIS for backend), with 100% HIPAA-compliant data handling, and no paid cloud services.

---

## Architecture Goals

- **Architecture Goal 1:** HIPAA-first security — every design decision must preserve PHI confidentiality, integrity, and availability in compliance with HIPAA §164.312 technical safeguards; encryption, access control, and immutable audit logging are non-negotiable at every layer.
- **Architecture Goal 2:** Trust-First AI — no AI-generated clinical output (extracted fields, ICD-10/CPT codes) is persisted to the patient record without explicit Staff human confirmation; the architecture enforces this at the API layer, not just the UI layer.
- **Architecture Goal 3:** OSS-only cost envelope — every component, dependency, API, and infrastructure choice must be deployable at zero licence cost; paid SaaS and cloud APIs are structurally prohibited.
- **Architecture Goal 4:** Async-by-default for heavy workloads — document OCR, clinical aggregation, code generation, and notification delivery are decoupled from request threads via a persistent job queue to meet performance targets without blocking user-facing responses.
- **Architecture Goal 5:** Centralized Staff control — the architecture must make patient self-check-in technically impossible, not merely UI-blocked; check-in endpoints are Staff-role-gated at the API authorization layer.
- **Architecture Goal 6:** Constraint-driven technology selection — every technology in the stack is justified by a specific NFR, DR, or AIR in this document; no technology is selected by convention or prior project habit.

---

## Non-Functional Requirements

- NFR-001: [SOURCE:INPUT] System MUST achieve a minimum uptime of 99.9% measured monthly, with a maximum planned downtime window of 43 minutes per month; unplanned outages MUST trigger automatic service restart via Windows Service recovery actions.
  Basis: BRD §7 "99.9% uptime target"; FR-009, FR-011 require background processing to continue during low-traffic windows.

- NFR-002: [SOURCE:INPUT] System MUST deliver the appointment confirmation email with PDF attachment within 60 seconds of booking confirmation under normal operating conditions (≤50 concurrent users).
  Basis: spec Success Criteria "Appointment confirmation PDF delivered via email within 60 seconds of booking confirmation".

- NFR-003: [SOURCE:INFERRED] System MUST return foreground API responses for all patient-facing and Staff-facing interactions within 2 seconds at P95 under normal load; OCR extraction, code generation, and 360° aggregation MUST execute asynchronously and MUST NOT block the calling HTTP request thread.
  Basis: BRD §3 "2-minute verification action" clinical efficiency target implies sub-second UI interactions; async job architecture is the only viable pattern for OCR/LLM workloads on OSS infrastructure.

- NFR-004: [SOURCE:INPUT] System MUST comply with HIPAA §164.312 technical safeguards: (a)(1) access control, (b) audit controls, (c) integrity controls, and (d) transmission security; any PHI at rest MUST be encrypted; any PHI in transit MUST use TLS 1.2 or higher.
  Basis: BRD §7 "100% HIPAA-compliant data handling"; spec C-003.

- NFR-005: [SOURCE:INPUT] System MUST enforce role-based access control (RBAC) at every API endpoint; requests from a token scoped to Role A attempting a Role B endpoint MUST receive HTTP 403 and be logged to the audit trail; no client-side role enforcement is sufficient.
  Basis: spec FR-003; BRD §7 "role-based access".

- NFR-006: [SOURCE:INPUT] System MUST encrypt all uploaded clinical documents using AES-256 before the write acknowledgement is returned to the client; no plaintext document byte MUST exist in any storage layer, temporary file, or process buffer after the encryption step completes.
  Basis: spec FR-027; HIPAA §164.312(a)(2)(iv) encryption at rest; BRD §7.

- NFR-007: [SOURCE:INPUT] System MUST enforce a 15-minute server-side session inactivity timeout for all authenticated roles; JWT tokens MUST be short-lived (≤15 min expiry); HTTPS MUST be enforced on all endpoints; CSRF protection MUST be applied to all state-changing API requests.
  Basis: spec FR-004; BRD §7 "robust session management (15-minute timeout)"; OWASP A07 Security Misconfiguration, A01 Broken Access Control.

- NFR-008: [SOURCE:INPUT] System MUST maintain an immutable, append-only audit log; no API endpoint, database role, or application code path MUST permit UPDATE or DELETE of audit records; any such attempt MUST return HTTP 405 and itself be logged.
  Basis: spec FR-005; HIPAA §164.312(b) audit controls.

- NFR-009: [SOURCE:INPUT] System MUST implement retry-with-exponential-backoff (maximum 3 retries, base delay 30 seconds, multiplier 2×) for all background jobs: notification delivery, OCR extraction, slot-swap processing, and AI code generation; failed jobs after max retries MUST be moved to a dead-letter queue and surfaced in the Staff dashboard.
  Basis: spec FR-009, FR-011, FR-023, FR-024, FR-028 extensions; BRD §3 reliability requirement.

- NFR-010: [SOURCE:INPUT] System MUST process document OCR extraction, clinical data aggregation, and AI code generation asynchronously via a persistent background job queue; these operations MUST NOT execute in the HTTP request pipeline and MUST NOT cause request timeouts.
  Basis: spec UC-018, UC-021; BRD §3 "background processing and workflows"; clinical prep efficiency target.

- NFR-011: [SOURCE:INPUT] System MUST use exclusively free and open-source licensed components, frameworks, and API integrations; no component with a paid licence tier, paid API call cost, or paid cloud infrastructure dependency is permitted.
  Basis: BRD §5 "Strictly free and open-source stacks"; spec C-001, C-002.

- NFR-012: [SOURCE:INPUT] System MUST achieve an AI-Human Agreement Rate of ≥ 98% for AI-generated ICD-10 and CPT code suggestions versus Staff-verified final codes, measured as a rolling 30-day metric visible in the Admin dashboard.
  Basis: BRD §8 "AI-Human Agreement Rate >98%"; spec Success Criteria.

---

## Data Requirements

- DR-001: [SOURCE:INPUT] System MUST persist a Patient entity containing: unique patient ID, full name, date of birth, contact phone, email address, email verification status, account status (Active/Inactive), hashed password, role assignment, and registration timestamp; email address MUST be stored as a unique indexed column.
  Basis: spec FR-001; BRD §6 user roles.

- DR-002: [SOURCE:INPUT] System MUST persist an Appointment entity containing: appointment ID, patient FK, slot FK (date, time, location), status (Scheduled/Arrived/In-Progress/Completed/No-Show/Cancelled), no-show risk score, cancellation cutoff timestamp, created-by, created-at, and last-modified-at; status transitions MUST be validated against the defined FSM before persistence.
  Basis: spec FR-007, FR-010, FR-022; UC-004, UC-015, UC-016.

- DR-003: [SOURCE:INPUT] System MUST persist a WaitlistEntry entity containing: entry ID, patient FK, preferred slot FK, confirmed appointment FK, registration timestamp, and status (Active/Offered/Accepted/Declined/Expired); only one Active entry per patient is permitted (enforced by unique partial index).
  Basis: spec FR-008, FR-009; UC-005, UC-006.

- DR-004: [SOURCE:INPUT] System MUST persist an IntakeRecord entity containing: intake ID, appointment FK, intake source (AI/Manual), structured fields (demographics, chief complaint, medical history, medications, allergies) as typed columns or JSON, submission timestamp, version number, and editor identity for every edit; prior versions MUST be retained for audit purposes.
  Basis: spec FR-015, FR-016, FR-017; HIPAA audit integrity.

- DR-005: [SOURCE:INPUT] System MUST persist a ClinicalDocument entity containing: document ID, patient FK, original filename, MIME type, encrypted blob storage path, upload timestamp, virus-scan result (Pass/Fail/Pending), extraction status (Pending/Extracted/LowConfidence/NoData/Failed), and uploader identity; the encrypted blob MUST be stored outside the database (filesystem or object store) with only the path persisted.
  Basis: spec FR-026, FR-027; UC-017.

- DR-006: [SOURCE:INPUT] System MUST persist ExtractedClinicalField records containing: field ID, document FK, patient FK, field type (VitalSign/MedicalHistory/Medication/Allergy/Diagnosis), field value, source document reference, extraction confidence score, and extraction timestamp; conflict flags MUST be stored as a separate ConflictFlag entity referencing two or more conflicting ExtractedClinicalField IDs.
  Basis: spec FR-029, FR-030, FR-031; UC-018, UC-019.

- DR-007: [SOURCE:INPUT] System MUST persist a MedicalCodeSuggestion entity containing: suggestion ID, patient FK, appointment FK, code type (ICD-10/CPT), suggested code, code description, confidence score, verification status (Pending/Accepted/Modified/Rejected), Staff verifier FK, verified-at timestamp, and committed code (post-modification); no suggestion may transition to Accepted/Modified without a non-null Staff verifier FK.
  Basis: spec FR-033, FR-034, FR-035; AIR-005 Trust-First constraint.

- DR-008: [SOURCE:INFERRED] System MUST persist an AuditLog entity as an append-only table containing: log ID (auto-increment), actor user FK, actor role, action type (CREATE/READ/UPDATE/DELETE/LOGIN/LOGOUT/EXPORT), affected entity type, affected entity ID, before-value snapshot (nullable), after-value snapshot (nullable), IP address, and timestamp; the table MUST have no UPDATE or DELETE privileges granted to any database user.
  Basis: spec FR-005; HIPAA §164.312(b); NFR-008.

- DR-009: [SOURCE:INFERRED] System MUST retain all PHI records (patient, appointment, intake, clinical documents, extracted fields, codes) for a minimum of 6 years from the date of last clinical encounter, consistent with HIPAA minimum retention standards; records flagged for retention MUST NOT be permanently deleted during the retention window, only soft-deleted.
  Basis: HIPAA §164.530(j) record retention; implied by BRD §7 HIPAA compliance.

- DR-010: [SOURCE:INPUT] System MUST use Upstash Redis as the caching layer for: active session token validation (TTL = 15 minutes), available appointment slot lists (TTL = 60 seconds), and 360° patient view assembly results (TTL = 5 minutes, invalidated on any new document extraction); cache misses MUST fall through to the primary database without error.
  Basis: BRD §7 "Upstash Redis for caching"; spec A-003.

### Domain Entities

- **Patient:** Core identity entity. Owns Appointments, IntakeRecords, ClinicalDocuments. One active WaitlistEntry maximum. Linked to AuditLog by actor FK.
- **Appointment:** Central scheduling entity. Has status FSM. Owns MedicalCodeSuggestions. References Slot (available time block). Linked to WaitlistEntry (preferred slot linkage).
- **Slot:** Configurable available time block (date, time, location, capacity). Managed by Admin/Staff configuration. Referenced by Appointment and WaitlistEntry.
- **WaitlistEntry:** Links a Patient to a preferred Slot while holding a confirmed Appointment. One active entry per patient enforced at DB level.
- **IntakeRecord:** Versioned structured intake data per appointment. Supports AI and manual source paths. Linked to InsuranceValidationResult.
- **InsuranceDummyRecord:** Static lookup table of provider name + insurance ID combinations. Populated manually by Admin pre-go-live.
- **ClinicalDocument:** Metadata record for uploaded patient documents. Blob stored encrypted on filesystem. Drives extraction pipeline.
- **ExtractedClinicalField:** Typed structured fields extracted per document per patient. Feeds de-duplication and conflict detection.
- **ConflictFlag:** Links two or more ExtractedClinicalField records with conflicting values. Resolved or dismissed by Staff before 360° view is Verified.
- **MedicalCodeSuggestion:** AI-generated ICD-10/CPT candidate per patient. Immutable until Staff verification action; Trust-First constraint enforced at entity level.
- **AuditLog:** Append-only, no FK constraints to mutable tables, no application-level delete path.
- **UserAccount:** Polymorphic principal (Patient, Staff, Admin). Owns sessions. All role checks derive from this entity's role field.

---

## AI Consideration

**Status:** Applicable

The upstream specification contains the following AI-tagged requirements:
- FR-015 `[HYBRID]` — AI-assisted conversational intake (NLU dialogue, human confirms summary)
- FR-028 `[AI-CANDIDATE]` — OCR-backed document text extraction
- FR-033 `[AI-CANDIDATE]` — ICD-10 code mapping from clinical narratives
- FR-034 `[AI-CANDIDATE]` — CPT code mapping from procedural data
- FR-035 `[HYBRID]` — Staff verification of AI-generated codes before commit

`$AI_SIGNAL = true`. AI Requirements section follows.

---

## AI Requirements

- AIR-001: [SOURCE:INPUT] System MUST support an AI-guided conversational intake flow that uses an NLU model to interpret patient free-text responses, map them to structured intake fields (demographics, chief complaint, history, medications, allergies), and request clarification when response confidence falls below a configurable threshold (default: 0.70).
  Basis: spec FR-015; UC-008; BRD §4 "AI conversational" intake.

- AIR-002: [SOURCE:INPUT] System MUST apply OCR text extraction to all uploaded clinical documents; extraction confidence is computed per-page; documents where any page confidence falls below a configurable threshold (default: 0.75) MUST be flagged as LowConfidence in the ClinicalDocument extraction status and surfaced to Staff for manual review.
  Basis: spec FR-028; UC-018 extension 3a; BRD §3 "Ingests patient-uploaded historical documents".

- AIR-003: [SOURCE:INPUT] System MUST map extracted clinical diagnosis narratives to ICD-10 codes using an AI/NLP model; each mapping MUST return the top-3 candidate codes ranked by confidence score; the production AI-Human Agreement Rate for accepted codes MUST be ≥ 98% measured as a rolling 30-day metric.
  Basis: spec FR-033; NFR-012; BRD §8 "AI-Human Agreement Rate >98%".

- AIR-004: [SOURCE:INPUT] System MUST map extracted procedural and service data to CPT codes using an AI/NLP model; when no CPT-mappable procedure is present in the extracted data, the system MUST surface "No procedures identified" to the Staff verification panel rather than generating a zero-confidence suggestion.
  Basis: spec FR-034; UC-021 extension 4a.

- AIR-005: [SOURCE:INPUT] System MUST enforce the Trust-First constraint at the API layer: no AI-generated ICD-10 or CPT code, no OCR-extracted field, and no AI intake response MUST be written to the canonical patient record without an explicit Staff or Patient confirmation action; the API endpoint for code commit MUST require a non-null `verified_by` Staff user ID in the request body, validated server-side.
  Basis: spec FR-035; BRD §4 "Trust-First" design; AIR-003, AIR-004.

- AIR-006: [SOURCE:INPUT] System MUST use exclusively free and open-source AI/NLP/OCR components; no paid model API (OpenAI, Anthropic, AWS Bedrock, Azure AI) MUST be called; all model inference MUST be executed locally within the deployment environment.
  Basis: BRD §5 "Strictly free and open-source stacks"; NFR-011; spec A-002.

### AI Architecture Pattern

**Selected Pattern:** Hybrid — Local Inference Pipeline + Human-in-the-Loop Verification

**Rationale:**
- **AIR-001 (Conversational Intake):** HYBRID pattern — NLU model generates structured field candidates from patient dialogue; patient reviews and confirms the summary before it is stored. No response is persisted without patient confirmation.
- **AIR-002 (OCR Extraction):** AI-CANDIDATE pattern — Tesseract OCR executes as a background pipeline step; output is stored as candidate ExtractedClinicalField records with confidence scores; not auto-committed to 360° view until Staff reviews conflicts.
- **AIR-003/AIR-004 (ICD-10/CPT Coding):** HYBRID pattern — locally-hosted medical LLM generates code suggestions with confidence scores; Staff must Accept/Modify/Reject each before commit (AIR-005). Trust-First constraint makes this structurally HYBRID.
- **AIR-006 (OSS-only):** All models run via Ollama (local LLM runtime) or direct library inference; no external API calls.

---

## Architecture and Design Decisions

- **Decision 1 — Layered Monolith, Not Microservices:** The OSS-only, single-tenant, free-infrastructure constraint (NFR-011, C-001) makes microservice orchestration (Kubernetes, service mesh) impractical. A well-structured layered monolith (.NET 8 Web API with vertical slice / feature folder organisation) delivers required modularity without operational overhead. Migration to microservices is a post-Phase-1 decision point.

- **Decision 2 — Background Job Queue via Hangfire:** NFR-009 and NFR-010 require persistent, retryable async jobs. Hangfire (OSS, MIT licence) persists job state in SQL Server, provides a built-in dashboard, supports exponential backoff, and integrates natively with .NET DI — meeting all constraints without a paid message broker (RabbitMQ requires infra; Azure Service Bus is out of scope).

- **Decision 3 — Dual Database (SQL Server + PostgreSQL):** BRD §7 and spec A-003 explicitly name both. SQL Server serves as the primary relational store for scheduling, users, audit, and configuration. PostgreSQL serves as the clinical data store for extracted fields, code suggestions, and 360° view assembly — chosen for its JSONB flexibility for variable clinical field schemas and its OSS licence. Both are accessed through Entity Framework Core (EF Core) with separate DbContext instances, maintaining bounded-context isolation.

- **Decision 4 — Local LLM via Ollama for Medical Coding:** AIR-006 prohibits paid APIs. Ollama provides a local OpenAI-compatible REST API wrapping open-weight medical LLMs (BioMistral 7B, MedLLaMA2). This meets AIR-003/AIR-004 accuracy targets while remaining fully OSS-deployable on a Windows Server host with sufficient VRAM (≥8GB for 7B quantized models).

- **Decision 5 — Tesseract OCR for Document Extraction:** AIR-002, AIR-006, and spec A-001 identify Tesseract as the leading free OCR engine. Tesseract 5.x with LSTM engine delivers acceptable accuracy on typed clinical PDF text. Integration via the Tesseract .NET wrapper (Tesseract NuGet package) runs within the Hangfire background job context.

- **Decision 6 — JWT + ASP.NET Core Identity for AuthN/AuthZ:** NFR-007 requires short-lived JWTs with server-side timeout enforcement. ASP.NET Core Identity provides the credential store, password hashing (PBKDF2/Argon2), and token generation. Upstash Redis caches the valid token set for O(1) revocation checks on each request. When a session is invalidated (timeout or logout), the token is removed from Redis — any subsequent request with the stale token fails the cache lookup before reaching business logic.

- **Decision 7 — Immutable Audit Log via Database-Level Constraint:** NFR-008 requires no role to be able to modify audit records. This is enforced at two levels: (1) the AuditLog table is granted INSERT-only privileges to the application database user — no UPDATE/DELETE grants exist; (2) the API layer has no endpoint that accepts audit modification requests, returning HTTP 405 by design.

- **Decision 8 — QuestPDF for PDF Generation:** NFR-002 requires PDF confirmation within 60 seconds. QuestPDF (OSS, MIT licence) is a fluent .NET PDF generation library with no external dependencies, suitable for synchronous generation of appointment confirmation PDFs within the request pipeline or as a fast synchronous step in the notification Hangfire job.

- **Decision 9 — Optimistic Concurrency for Slot Reservation:** DR-008 requires no double-booking on concurrent slot swaps. EF Core's `[ConcurrencyToken]` on the Slot entity's `IsAvailable` column (mapped to a SQL Server `rowversion` / `timestamp` column) ensures that two concurrent swap transactions cannot both succeed — the second receives a `DbUpdateConcurrencyException` and returns a graceful failure to the caller.

- **Decision 10 — Rasa Open Source for Conversational Intake NLU:** AIR-001 and AIR-006 require an OSS NLU engine. Rasa Open Source provides intent classification, entity extraction, and multi-turn dialogue management with a Python REST API that the .NET backend calls as an internal microservice. An alternative is a fine-tuned sentence-transformers model for intent classification only, but Rasa provides the full dialogue state machine needed for the multi-turn intake flow.

---

## Technology Stack

| Layer | Technology | Version | Justification |
|-------|------------|---------|---------------|
| Frontend | Angular | 17.x LTS | NFR-005, NFR-007; BRD §5 explicit selection; strong RBAC guard support, reactive forms for intake |
| Mobile | N/A | — | No mobile scope in Phase 1; BRD §6 out-of-scope |
| Backend | ASP.NET Core Web API (.NET 8) | .NET 8 LTS | NFR-004, NFR-005, NFR-007, NFR-011; BRD §5 explicit selection; LTS lifecycle to 2026, native JWT, EF Core, DI |
| Database (Primary) | SQL Server | 2022 / Express | DR-001–DR-008; BRD §7 explicit; free Express edition covers single-tenant load; EF Core provider mature |
| Database (Clinical) | PostgreSQL | 16.x | DR-005–DR-007, DR-010; BRD §7 explicit; OSS, JSONB for variable field schemas, EF Core Npgsql provider |
| Caching | Upstash Redis | Latest | DR-010, NFR-001, NFR-007; BRD §7 explicit; serverless Redis with free tier, session token store + slot cache |
| Job Queue | Hangfire | 1.8.x | NFR-009, NFR-010; OSS MIT licence; SQL Server-backed persistence, built-in retry/backoff, .NET DI integration |
| OCR Engine | Tesseract | 5.x (Tesseract .NET NuGet) | AIR-002, AIR-006, NFR-011; leading OSS OCR engine; LSTM accuracy acceptable for typed clinical PDFs |
| AI — NLU (Intake) | Rasa Open Source | 3.x | AIR-001, AIR-006; multi-turn dialogue state machine; Python REST API called from .NET backend |
| AI — Medical LLM | Ollama + BioMistral 7B (GGUF Q4) | Ollama 0.x / BioMistral 7B | AIR-003, AIR-004, AIR-006; local inference, OpenAI-compatible REST, medical domain pre-training |
| Auth | ASP.NET Core Identity + JWT Bearer | .NET 8 | NFR-005, NFR-007, NFR-008; native .NET, PBKDF2 hashing, short-lived JWT, Redis-backed revocation |
| Encryption | System.Security.Cryptography (AES-256-CBC) | .NET 8 BCL | NFR-006, DR-005; FIPS-compliant, no external dependency, integrated with .NET 8 BCL |
| Email | MailKit | 4.x | NFR-011, FR-014, FR-023; OSS MIT, SMTP/IMAP, widely used .NET email library, no paid tier |
| SMS | Twilio free tier / OSS Vonage sandbox | Latest | NFR-011, FR-023; free sandbox tiers for dev/demo; production can swap to self-hosted VoIP gateway |
| Calendar | Google Calendar API v3 + Microsoft Graph API v1.0 | Current | NFR-011, FR-012, FR-013; free-tier quotas sufficient for single-tenant clinical volume |
| PDF Generation | QuestPDF | 2024.x | NFR-002, FR-014; OSS MIT, fluent .NET API, synchronous generation, no external process |
| Virus Scan | ClamAV (via nClam .NET client) | 1.x / ClamAV 1.x | NFR-006, FR-026; OSS GPL, daemon-based scan, .NET client available, runs as Windows Service |
| Testing | xUnit + Playwright + Moq | Latest | NFR-003, NFR-005; xUnit for unit/integration, Playwright for E2E Angular, Moq for mocking |
| Infrastructure | Windows Services + IIS + Netlify/Vercel | Current | NFR-011, C-001; free static hosting for Angular SPA; IIS for .NET API; Windows Service for Hangfire |
| Security | OWASP dependency-check + SonarQube Community | Latest | NFR-004, NFR-007; OSS SAST and dependency CVE scanning in CI pipeline |
| Deployment | GitHub Actions (CI/CD) | Current | NFR-011; free tier on public/private repos; deploys Angular to Netlify, API to IIS via web deploy |
| Monitoring | Serilog + Seq Community | Latest | NFR-001, NFR-009; structured logging, free Seq community edition for log aggregation and alerting |
| API Docs | Swashbuckle (Swagger/OpenAPI) | 6.x | TR-002; auto-generated from .NET controller annotations; no additional cost |

### AI Component Stack

| Component | Technology | Purpose |
|-----------|------------|---------|
| Model Provider | Ollama + BioMistral 7B (GGUF Q4_K_M quantization) | Local LLM inference for ICD-10/CPT code mapping; OpenAI-compatible REST endpoint |
| NLU Engine | Rasa Open Source 3.x | Multi-turn conversational intake; intent classification + entity extraction + dialogue management |
| OCR Engine | Tesseract 5.x (LSTM) | PDF-to-text extraction pipeline; confidence scoring per page |
| Guardrails | Custom .NET middleware: Trust-First API gate | Intercepts all AI output commits; enforces non-null `verified_by` Staff ID before any persistence; rejects unverified payloads with HTTP 422 |

### Alternative Technology Options

- **Hangfire vs Quartz.NET:** Quartz.NET is a capable OSS job scheduler but lacks a built-in persistence-backed retry queue and dead-letter concept. Hangfire provides these natively backed by SQL Server, reducing custom infrastructure code — selected over Quartz.NET for NFR-009.
- **QuestPDF vs iTextSharp:** iTextSharp's AGPL licence requires source disclosure for commercial derivative works; QuestPDF (MIT) has no such restriction and provides a more ergonomic .NET fluent API — selected for NFR-011 (OSS-only) and NFR-002.
- **Tesseract vs PaddleOCR:** PaddleOCR has higher accuracy on complex layouts but requires a Python runtime and GPU for acceptable performance. Tesseract runs natively in the .NET process via P/Invoke, reducing deployment complexity — selected for AIR-006 and NFR-011.
- **[AI] BioMistral vs Llama 3 Medical:** Llama 3 base model lacks medical domain fine-tuning; BioMistral 7B is fine-tuned on PubMed/MIMIC biomedical data, providing better ICD/CPT mapping accuracy for AIR-003/AIR-004 targets. BioMistral selected.
- **[AI] Rasa vs spaCy NER pipeline:** spaCy provides NER and intent detection but does not natively support multi-turn dialogue state management required by AIR-001 (clarification follow-ups, mid-flow handoff to manual form). Rasa selected for full dialogue management.
- **[AI] No Vector Store selected:** RAG architecture considered for code suggestion retrieval; rejected because ICD-10/CPT codebook is a static, structured lookup that fits direct LLM prompt augmentation better than embedding retrieval. Vector store is not required in Phase 1.

### Technology Decision

| Metric (from NFR/DR) | Hangfire | Quartz.NET | Rationale |
|---|---|---|---|
| NFR-009: Persistent retry with exponential backoff | ✅ Built-in, SQL-backed | ⚠️ Requires custom plugin | Hangfire wins — zero custom retry code |
| NFR-010: Dead-letter queue visibility | ✅ Built-in dashboard | ❌ Not native | Hangfire wins — Staff dashboard surfacing built-in |
| NFR-011: OSS licence | ✅ LGPL (free) | ✅ Apache 2.0 | Tie |
| DR-003: SQL Server persistence | ✅ Native | ⚠️ ADO.NET plugin | Hangfire wins — single NuGet, no adapters |

| Metric (from NFR/DR) | QuestPDF | iTextSharp |
|---|---|---|
| NFR-011: OSS licence | ✅ MIT | ⚠️ AGPL (copyleft risk) |
| NFR-002: Synchronous generation speed | ✅ <500ms typical | ✅ Comparable |
| Developer ergonomics | ✅ Fluent .NET API | ⚠️ Verbose XML-like API |

| Metric (from AIR) | BioMistral 7B | Llama 3 8B Base | Rationale |
|---|---|---|---|
| AIR-003: Medical domain accuracy | ✅ PubMed/MIMIC fine-tune | ⚠️ General domain | BioMistral wins — domain pre-training |
| AIR-006: OSS / local inference | ✅ Apache 2.0 | ✅ Meta Llama licence (free) | Tie |
| NFR-011: Zero API cost | ✅ Local via Ollama | ✅ Local via Ollama | Tie |
| Hardware: 8GB VRAM Q4 quantized | ✅ Fits 7B Q4_K_M | ✅ Fits 8B Q4_K_M | Tie |

---

## Technical Requirements

- TR-001: [SOURCE:INPUT] System MUST use Angular 17.x as the single-page application frontend framework, with Angular Route Guards implementing RBAC to prevent cross-role navigation client-side (server-side RBAC per NFR-005 remains authoritative).
  Basis: NFR-005, NFR-007; BRD §5 explicit Angular selection.

- TR-002: [SOURCE:INPUT] System MUST use .NET 8 ASP.NET Core Web API as the backend runtime, organized in a vertical-slice (feature folder) architecture with a single deployable assembly; Swashbuckle OpenAPI documentation MUST be generated from controller annotations.
  Basis: NFR-003, NFR-004, NFR-005; BRD §5 explicit .NET 8 selection.

- TR-003: [SOURCE:INPUT] System MUST use SQL Server 2022 (Express edition acceptable for single-tenant Phase 1 load) as the primary relational database, accessed via Entity Framework Core 8 with code-first migrations; the AuditLog table MUST have INSERT-only database user privileges enforced via SQL GRANT statements in migration scripts.
  Basis: DR-001–DR-008, NFR-008; BRD §7 explicit SQL Server selection.

- TR-004: [SOURCE:INPUT] System MUST use PostgreSQL 16 as the clinical data store for ExtractedClinicalField, ConflictFlag, MedicalCodeSuggestion, and 360° view assembly queries, accessed via EF Core with the Npgsql provider in a separate ClinicalDbContext; the two DbContext instances MUST NOT share a transaction scope.
  Basis: DR-005–DR-007; BRD §7 explicit PostgreSQL selection.

- TR-005: [SOURCE:INPUT] System MUST use Upstash Redis as the caching layer; session token validity checks MUST hit Redis before any database query; slot availability and 360° view cache entries MUST use the TTLs specified in DR-010; cache-aside pattern MUST be used — no write-through.
  Basis: DR-010, NFR-001, NFR-007; BRD §7 explicit Upstash Redis selection.

- TR-006: [SOURCE:INPUT] System MUST use Hangfire 1.8.x backed by SQL Server for all background job processing (notifications, OCR extraction, slot-swap, code generation); Hangfire recurring jobs MUST replace any use of `System.Threading.Timer` or `Task.Delay` loops in the application; Hangfire dashboard MUST be restricted to the Admin role.
  Basis: NFR-009, NFR-010; Decision 2.

- TR-007: [SOURCE:INPUT] System MUST integrate Tesseract 5.x via the Tesseract .NET NuGet package for OCR processing of uploaded clinical documents; Tesseract MUST run within the Hangfire job context (not in the HTTP pipeline); extracted text and per-page confidence scores MUST be stored before any NLP extraction step begins.
  Basis: AIR-002, AIR-006; Decision 5.

- TR-008: [SOURCE:INPUT] System MUST integrate Rasa Open Source 3.x as a locally-deployed Python service for AI conversational intake; the .NET backend MUST communicate with Rasa via its REST webhook API on localhost; the Rasa service MUST be deployed as a Windows Service or within GitHub Codespaces alongside the .NET API; Rasa MUST NOT be accessible from the public internet.
  Basis: AIR-001, AIR-006; Decision 10.

- TR-009: [SOURCE:INPUT] System MUST integrate Ollama running BioMistral 7B (GGUF Q4_K_M quantization) as a locally-deployed LLM service for ICD-10/CPT code generation; the .NET backend MUST call Ollama's OpenAI-compatible `/api/chat` endpoint on localhost; the Ollama service MUST NOT be accessible from the public internet; prompt templates for ICD-10 and CPT mapping MUST be version-controlled.
  Basis: AIR-003, AIR-004, AIR-006; Decision 4.

- TR-010: [SOURCE:INPUT] System MUST use ASP.NET Core Identity for credential storage and password hashing (PBKDF2 with SHA-256, 100,000 iterations minimum); JWT Bearer tokens MUST be issued with a maximum 15-minute expiry; token revocation MUST be enforced via Redis-backed token allowlist — a token not present in Redis MUST be rejected with HTTP 401 regardless of JWT signature validity.
  Basis: NFR-007, NFR-005; Decision 6.

- TR-011: [SOURCE:INPUT] System MUST encrypt clinical document blobs using AES-256-CBC via `System.Security.Cryptography.Aes` before writing to filesystem storage; the encryption key MUST be stored in a separate secrets store (environment variable or Windows DPAPI-protected file) and MUST NOT be embedded in source code or configuration files committed to version control.
  Basis: NFR-006, DR-005; Decision 7; OWASP A02 Cryptographic Failures.

- TR-012: [SOURCE:INPUT] System MUST use MailKit 4.x for all outbound email (confirmations, reminders, credential setup); SMTP credentials MUST be stored in environment variables, not in `appsettings.json`; all emails MUST be sent over SMTPS (port 465) or STARTTLS (port 587).
  Basis: NFR-011, FR-014, FR-023, FR-024; OWASP A02.

- TR-013: [SOURCE:INFERRED] System MUST integrate an SMS gateway via a free-tier or sandbox API (Twilio free trial or Vonage sandbox) for appointment reminder delivery; SMS gateway credentials MUST be stored in environment variables; the SMS integration MUST be implemented behind an `ISmsGateway` interface to permit provider swap without business logic changes.
  Basis: NFR-011, FR-023; OSS-only constraint; interface abstraction follows NFR-009 retry requirements.

- TR-014: [SOURCE:INPUT] System MUST integrate Google Calendar API v3 and Microsoft Graph API v1.0 for calendar event creation; both integrations MUST use OAuth 2.0 Authorization Code flow with PKCE; OAuth tokens MUST be stored encrypted per patient (TR-011 encryption standard) and MUST NOT be logged in the audit trail.
  Basis: NFR-011, FR-012, FR-013; OWASP A07 security misconfiguration.

- TR-015: [SOURCE:INPUT] System MUST use QuestPDF 2024.x for synchronous appointment confirmation PDF generation; PDF generation MUST complete within 2 seconds for a single-appointment document; generated PDFs MUST be streamed directly to the email attachment without intermediate filesystem writes.
  Basis: NFR-002, FR-014; Decision 8.

- TR-016: [SOURCE:INPUT] System MUST integrate ClamAV via the nClam .NET client for virus scanning of all uploaded documents before encryption and storage; ClamAV MUST run as a Windows Service (clamd daemon); if the ClamAV daemon is unreachable, the upload MUST be rejected with HTTP 503 and the event logged — silent bypass of virus scanning is not permitted.
  Basis: NFR-006, FR-026; OWASP A08 Software and Data Integrity Failures.

- TR-017: [SOURCE:INPUT] System MUST deploy the Angular SPA as a static site to Netlify or Vercel (free tier) served over HTTPS with HSTS headers; the .NET 8 Web API MUST be deployed to IIS on Windows Server with TLS certificate; Hangfire worker and Ollama/Rasa services MUST run as Windows Services on the same host or within GitHub Codespaces for development.
  Basis: NFR-011, C-001; BRD §5 deployment targets.

- TR-018: [SOURCE:INFERRED] System MUST implement structured logging via Serilog with sinks to: (1) rolling file (daily rotation, 30-day retention), and (2) Seq Community Edition for queryable log aggregation; all log entries MUST include correlation ID, user ID (non-PHI), request path, and response status; PHI MUST NOT appear in log output.
  Basis: NFR-001, NFR-009; HIPAA §164.312(b) audit controls; OWASP A09 Security Logging and Monitoring Failures.

---

## Technical Constraints & Assumptions

- **TC-001 [SOURCE:INPUT]:** No paid cloud infrastructure (AWS, Azure, GCP) may be used in any tier. All compute, storage, and networking must be self-hosted or free-tier SaaS.
- **TC-002 [SOURCE:INPUT]:** All software licences must be OSS-compatible (MIT, Apache 2.0, LGPL, GPL) with no copyleft risk to proprietary application code, except ClamAV (GPL, used as a separate process/daemon — no GPL propagation to application).
- **TC-003 [SOURCE:INPUT]:** The deployment target is a single Windows Server host for backend services; horizontal scaling is not required in Phase 1.
- **TC-004 [SOURCE:INPUT]:** Provider (clinician) accounts, EHR integration, payment processing, family member profiles, and patient self-check-in are structurally excluded — no API endpoints for these use cases will exist.
- **TC-005 [SOURCE:INFERRED]:** Ollama requires a host with ≥ 8 GB VRAM (or ≥ 16 GB RAM for CPU-only inference) to run BioMistral 7B Q4_K_M at acceptable latency (< 30 seconds per code generation request). Production deployment must validate hardware against this constraint.
- **TC-006 [SOURCE:INFERRED]:** Rasa Open Source requires Python 3.10+ runtime on the same host as the .NET backend; the Python environment must be isolated (venv or Conda) to avoid dependency conflicts with other system Python packages.
- **TC-007 [SOURCE:INFERRED]:** SQL Server Express edition has a 10 GB database size limit and does not include the SQL Agent service (used for scheduled jobs). All scheduled job execution must route through Hangfire (TR-006) rather than SQL Agent.
- **TC-008 [SOURCE:INPUT]:** Upstash Redis is accessed as a managed serverless Redis via its REST API or Redis protocol; the free tier supports 256 MB storage and 10,000 commands/day — sufficient for single-tenant Phase 1 session and slot caching volume.
- **TC-009 [SOURCE:INFERRED]:** The Angular SPA is a pure static build (no SSR); server-side rendering via Angular Universal is not required and would add deployment complexity without benefit given the authenticated-only nature of the application.
- **TC-010 [SOURCE:INFERRED]:** All OAuth tokens (Google Calendar, Microsoft Graph) obtained per patient must be treated as PHI-adjacent credentials; they must be encrypted at rest (TR-011), scoped to the minimum required Calendar write permission, and subject to the 6-year retention/deletion policy (DR-009) aligned with the patient record lifecycle.
