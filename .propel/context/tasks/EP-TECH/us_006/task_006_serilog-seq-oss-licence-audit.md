# Task - TASK_006

## Requirement Reference

- **User Story**: US_006 — Serilog + Seq + OSS licence audit
- **Story Location**: `.propel/context/tasks/EP-TECH/us_006/us_006.md`
- **Parent Epic**: EP-TECH

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | Serilog configured with rolling 30-day file sink and Seq CE sink |
| AC-002 | `CorrelationIdMiddleware` adds `X-Correlation-ID` header to all requests and enriches log context |
| AC-003 | PHI filter destructures and redacts PHI fields to `[REDACTED]` in all log sinks |
| AC-004 | Seq CE is the designated log aggregation target; Seq URL configured from env var |
| AC-005 | OSS licence audit script validates all NuGet packages are MIT / Apache-2.0 / LGPL / GPL licensed; fails CI on violation |

### Edge Cases

- `X-Correlation-ID` header absent on incoming request → middleware generates a new GUID and sets it on both request and response
- PHI filter must cover: `Email`, `DateOfBirth`, `PhoneNumber`, `FirstName`, `LastName`, `Address`, `SSN` property names

---

## Design References

N/A — UI Impact: No

---

## AI References

N/A — AI Impact: No

---

## Mobile References

N/A — Mobile Impact: No

---

## Applicable Technology Stack

| Layer | Technology | Version | Justification |
|-------|-----------|---------|---------------|
| Backend | Serilog | 3.x | Structured logging per design.md |
| Backend | Serilog.Sinks.Seq | 6.x | Seq CE sink per design.md |
| Backend | Serilog.Sinks.File | 5.x | Rolling file sink per design.md |
| Backend | Serilog.AspNetCore | 8.x | ASP.NET Core request logging integration |
| Observability | Seq Community Edition | Latest OSS | Log aggregation per design.md |

---

## Task Overview

Configure Serilog in the .NET 8 Web API with rolling 30-day file sink, Seq CE sink, `CorrelationIdMiddleware`, and a PHI destructuring policy that redacts sensitive fields. Create an OSS licence audit PowerShell/Bash script runnable in GitHub Actions CI.

---

## Dependent Tasks

- **TASK_001 (us_002)** — Web API scaffold must exist for middleware registration

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Program.cs` — Serilog host configuration
- `src/ClinicalHealthcare.Api/Middleware/CorrelationIdMiddleware.cs` — correlation ID propagation
- `src/ClinicalHealthcare.Infrastructure/Logging/PhiRedactingDestructuringPolicy.cs` — PHI filter
- `scripts/audit-oss-licences.ps1` — OSS licence validation script
- `.github/workflows/ci.yml` — licence audit step

---

## Implementation Plan

1. Add Serilog NuGet packages: `Serilog.AspNetCore`, `Serilog.Sinks.File`, `Serilog.Sinks.Seq`.
2. Configure `UseSerilog()` in `Program.cs`; file sink: `rollingInterval = RollingInterval.Day`, `retainedFileCountLimit = 30`; Seq sink from `SEQ_SERVER_URL` env var.
3. Create `PhiRedactingDestructuringPolicy` that intercepts known PHI property names and replaces values with `[REDACTED]`; register with `Destructure.With<PhiRedactingDestructuringPolicy>()`.
4. Create `CorrelationIdMiddleware`: read `X-Correlation-ID` from request header (or generate GUID); push to `LogContext.PushProperty("CorrelationId", id)`; set header on response.
5. Register `CorrelationIdMiddleware` before `UseSerilogRequestLogging()`.
6. Create `scripts/audit-oss-licences.ps1` using `dotnet-project-licenses` or `nuget-license` CLI; exit code 1 if any disallowed licence found.
7. Add `audit-licences` step to `.github/workflows/ci.yml` running the script.
8. Document `SEQ_SERVER_URL` as required env var in `docs/deployment-runbook.md`.

---

## Current Project State

```
src/
└── ClinicalHealthcare.Api/
    ├── Program.cs
    └── Middleware/  (empty)
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Middleware/CorrelationIdMiddleware.cs` | Adds/propagates X-Correlation-ID |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Logging/PhiRedactingDestructuringPolicy.cs` | Redacts PHI fields to [REDACTED] |
| MODIFY | `src/ClinicalHealthcare.Api/Program.cs` | Configure Serilog with file + Seq sinks, register middleware |
| CREATE | `scripts/audit-oss-licences.ps1` | OSS licence validation; fails on non-permissive licences |
| MODIFY | `.github/workflows/ci.yml` | Add audit-licences step |
| MODIFY | `docs/deployment-runbook.md` | Document SEQ_SERVER_URL env var |

---

## External References

- [Serilog.AspNetCore](https://github.com/serilog/serilog-aspnetcore)
- [Serilog Destructuring Policies](https://github.com/serilog/serilog/wiki/Destructuring)
- [Seq Community Edition](https://datalust.co/seq)
- [dotnet-project-licenses](https://github.com/tomchavakis/nuget-license)

---

## Build Commands

```bash
dotnet add src/ClinicalHealthcare.Api package Serilog.AspNetCore
dotnet add src/ClinicalHealthcare.Api package Serilog.Sinks.File
dotnet add src/ClinicalHealthcare.Api package Serilog.Sinks.Seq
dotnet build
```

---

## Implementation Validation Strategy

- `dotnet build` → 0 errors.
- Run app; send request without `X-Correlation-ID` → response includes generated UUID header.
- Log file created in `logs/` with date-stamped name.
- Log an object with `Email` property → Seq shows `Email: [REDACTED]`.
- Run `scripts/audit-oss-licences.ps1` → exit 0 on clean project; exit 1 after adding a GPL-incompatible package.

---

## Implementation Checklist

- [x] **[AC-001]** Serilog file sink (30-day rolling) and Seq CE sink configured
- [x] **[AC-002]** `CorrelationIdMiddleware` generates/propagates `X-Correlation-ID`; enriches log context
- [x] **[AC-003]** `PhiRedactingDestructuringPolicy` redacts known PHI property names to `[REDACTED]`
- [x] **[AC-004]** Seq URL read from `SEQ_SERVER_URL` env var; documented in runbook
- [x] **[AC-005]** `audit-oss-licences.ps1` validates NuGet licences; CI step fails on violation
- [x] `CorrelationIdMiddleware` registered before `UseSerilogRequestLogging()`
- [x] PHI property list covers: Email, DateOfBirth, PhoneNumber, FirstName, LastName, Address, SSN
- [x] `dotnet build` passes with 0 errors
