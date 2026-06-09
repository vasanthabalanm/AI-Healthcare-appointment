# Task - TASK_013

## Requirement Reference

- **User Story**: US_013 — Admin user account lifecycle
- **Story Location**: `.propel/context/tasks/EP-001/us_013/us_013.md`
- **Parent Epic**: EP-001

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `POST /admin/users` creates a new user account (Admin or Staff role); returns 201 |
| AC-002 | `PATCH /admin/users/{id}` updates user details or deactivates account |
| AC-003 | Cannot deactivate the last active Admin account; returns 409 |
| AC-004 | AuditLog entry written before and after every account change (before/after values) |
| AC-005 | Credential setup email sent to new user via MailKit on account creation |

### Edge Cases

- Attempt to deactivate own account while being the only Admin → 409
- PATCH with no changes (same values) → 200 but AuditLog entry still written (idempotent response, auditable action)

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
| Backend | ASP.NET Core Web API | 8 LTS | Endpoint hosting per design.md |
| Backend | ASP.NET Core Identity | .NET 8 | Password hashing + role management |
| Backend | MailKit | 4.x | Credential setup email per design.md |
| Database | SQL Server | 2022 / Express | `UserAccount` + `AuditLog` storage |

---

## Task Overview

Implement Admin user account lifecycle endpoints: `POST /admin/users` and `PATCH /admin/users/{id}`. Enforce last-Admin guard, write AuditLog before/after for every change, and send credential setup email to new users via MailKit.

---

## Dependent Tasks

- **TASK_001 (us_007)** — `UserAccount` entity
- **TASK_001 (us_011)** — `AuditLog` entity
- **TASK_001 (us_012)** — `IEmailService` / `MailKitEmailService`

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Features/Admin/CreateUserEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Admin/UpdateUserEndpoint.cs`
- `src/ClinicalHealthcare.Api/Features/Admin/CreateUserRequest.cs`
- `src/ClinicalHealthcare.Api/Features/Admin/UpdateUserRequest.cs`

---

## Implementation Plan

1. Create `CreateUserRequest` DTO: `Email`, `FirstName`, `LastName`, `Role` (Admin/Staff).
2. Implement `POST /admin/users` (`[Authorize(Roles="Admin")]`): validate DTO; check duplicate email (409); create `UserAccount` with temp password; write AuditLog; send credential setup email.
3. Create `UpdateUserRequest` DTO: `FirstName`, `LastName`, `IsActive`.
4. Implement `PATCH /admin/users/{id}` (`[Authorize(Roles="Admin")]`): load existing user; capture `beforeValue`; if deactivating — count active Admins; if only 1 Admin → 409; apply changes; write AuditLog with `beforeValue` + `afterValue`; return 200.
5. AuditLog helper method: serialize entity state to JSON for before/after values; include `ActorId` from JWT claim.
6. Credential email: generate one-time setup token (same mechanism as email verification); include link to set permanent password.

---

## Current Project State

```
src/ClinicalHealthcare.Api/Features/Admin/
└── README.md
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/Features/Admin/CreateUserEndpoint.cs` | POST /admin/users |
| CREATE | `src/ClinicalHealthcare.Api/Features/Admin/CreateUserRequest.cs` | DTO for user creation |
| CREATE | `src/ClinicalHealthcare.Api/Features/Admin/UpdateUserEndpoint.cs` | PATCH /admin/users/{id} |
| CREATE | `src/ClinicalHealthcare.Api/Features/Admin/UpdateUserRequest.cs` | DTO for user update |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Helpers/AuditLogHelper.cs` | Before/after JSON serialization helper |

---

## External References

- [ASP.NET Core Authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/roles)
- [MailKit](https://github.com/jstedfast/MailKit)

---

## Build Commands

```bash
dotnet build
```

---

## Implementation Validation Strategy

- `POST /admin/users` → 201; `UserAccount` in DB; AuditLog entry written; email sent.
- `PATCH /admin/users/{id}` deactivate last Admin → 409.
- `PATCH /admin/users/{id}` deactivate non-last Admin → 200; `AuditLog.BeforeValue` contains old `IsActive=true`.
- Non-Admin JWT → 403 on both endpoints.
- `dotnet build` → 0 errors.

---

## Implementation Checklist

- [x] **[AC-001]** `POST /admin/users` returns 201; `UserAccount` created with Admin/Staff role
- [x] **[AC-002]** `PATCH /admin/users/{id}` updates user details or deactivation status
- [x] **[AC-003]** Last-active-Admin deactivation guard returns 409
- [x] **[AC-004]** AuditLog before/after entry written for every account change
- [x] **[AC-005]** Credential setup email sent to new user via MailKit
- [x] Both endpoints require `.RequireAuthorization()` — 401/403 enforced by middleware pipeline (verified by code inspection; integration test host required to test JWT 403)
- [x] `ActorId` captured from JWT claims (`ClaimTypes.NameIdentifier`) in AuditLog
- [x] `dotnet build` passes with 0 errors
