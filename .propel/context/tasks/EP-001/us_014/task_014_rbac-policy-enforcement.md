# Task - TASK_014

## Requirement Reference

- **User Story**: US_014 — RBAC policy enforcement all endpoints
- **Story Location**: `.propel/context/tasks/EP-001/us_014/us_014.md`
- **Parent Epic**: EP-001

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | Every endpoint has `[Authorize]` or `[AllowAnonymous]`; startup convention test asserts this |
| AC-002 | Cross-role access attempt returns 403 and writes AuditLog `RBAC-Violation` entry |
| AC-003 | `[Authorize(Roles="Admin")]` endpoints reject Staff and Patient JWTs with 403 |
| AC-004 | `[Authorize(Roles="Staff,Admin")]` endpoints reject Patient JWTs with 403 |
| AC-005 | Startup convention test fails the build if any endpoint lacks an authorization attribute |

### Edge Cases

- New endpoint added without authorization attribute → CI pipeline fails on startup convention test
- AuditLog `RBAC-Violation` entry includes `ActorId`, `AttemptedEndpoint`, `RequiredRole`, `ActualRole`

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
| Backend | ASP.NET Core Authorization | 8.x | Role-based policy enforcement per design.md |
| Backend | ASP.NET Core Web API | 8 LTS | Endpoint convention scanning |
| Backend | xUnit / Reflect | Latest | Startup convention test |

---

## Task Overview

Add RBAC role policies for Admin, Staff, and Patient. Implement a custom `IAuthorizationMiddlewareResultHandler` that writes an AuditLog `RBAC-Violation` entry on 403. Create a startup convention xUnit test that reflects over all endpoint classes and asserts each has `[Authorize]` or `[AllowAnonymous]`.

---

## Dependent Tasks

- **TASK_001 (us_002)** — Endpoint registration infrastructure
- **TASK_001 (us_011)** — `AuditLog` entity

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Authorization/RbacViolationHandler.cs`
- `src/ClinicalHealthcare.Api/Program.cs` — policy registration
- `tests/ClinicalHealthcare.Api.Tests/Conventions/EndpointAuthorizationConventionTests.cs`

---

## Implementation Plan

1. Define named policies in `Program.cs`: `AdminOnly` (requires `role=Admin`), `StaffOrAdmin` (requires `role=Staff` or `role=Admin`), `AnyAuthenticated`.
2. Implement `RbacViolationHandler : IAuthorizationMiddlewareResultHandler`: on 403, write AuditLog entry with `Action=RBAC-Violation`, `EntityType=Endpoint`, `ActorId` from JWT, `AfterValue=JSON({attemptedEndpoint, requiredRole, actualRole})`.
3. Register `RbacViolationHandler` in DI as `IAuthorizationMiddlewareResultHandler`.
4. Create xUnit test project; reflection test: scan all types in the API assembly that implement `IEndpointDefinition`; assert each `MapEndpoints` method applies `[Authorize]` or `[AllowAnonymous]`; alternatively scan minimal API route handlers for metadata.
5. Ensure test is added to `dotnet test` in CI pipeline.
6. Verify all existing feature endpoints have appropriate authorization attributes.

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/
└── Auth/, Admin/, Appointments/, ...
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Authorization/RbacViolationHandler.cs` | Writes AuditLog on 403 |
| MODIFY | `src/ClinicalHealthcare.Api/Program.cs` | Register policies; register RbacViolationHandler |
| CREATE | `tests/ClinicalHealthcare.Api.Tests/Conventions/EndpointAuthorizationConventionTests.cs` | Startup convention: every endpoint has auth attribute |
| CREATE | `tests/ClinicalHealthcare.Api.Tests/ClinicalHealthcare.Api.Tests.csproj` | xUnit test project (if not yet created) |

---

## External References

- [ASP.NET Core Policy-Based Authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies)
- [IAuthorizationMiddlewareResultHandler](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/customizingauthorizationmiddlewareresponse)

---

## Build Commands

```bash
dotnet new xunit -n ClinicalHealthcare.Api.Tests -o tests/ClinicalHealthcare.Api.Tests
dotnet sln add tests/ClinicalHealthcare.Api.Tests/ClinicalHealthcare.Api.Tests.csproj
dotnet test
```

---

## Implementation Validation Strategy

- Add a test endpoint without `[Authorize]` → convention test fails.
- Staff JWT calling Admin-only endpoint → 403 + AuditLog `RBAC-Violation` entry.
- `dotnet test` → all convention tests pass on clean codebase.
- `dotnet build` → 0 errors.

---

## Implementation Checklist

- [x] **[AC-001]** Named policies (`AdminOnly`, `StaffOrAdmin`, `AnyAuthenticated`) registered
- [x] **[AC-002]** `RbacViolationHandler` writes AuditLog `RBAC-Violation` on 403
- [x] **[AC-003]** Admin-only endpoints return 403 for Staff and Patient JWTs
- [x] **[AC-004]** StaffOrAdmin endpoints return 403 for Patient JWTs
- [x] **[AC-005]** Convention test asserts every endpoint has `[Authorize]` or `[AllowAnonymous]`
- [x] `RbacViolationHandler` registered in DI
- [x] Convention test included in CI `dotnet test` step
- [x] `dotnet build` + `dotnet test` pass with 0 errors
