# Unit Test Plan - TASK_013

## Requirement Reference
- **User Story**: us_013
- **Story Location**: `.propel/context/tasks/EP-001/us_013/us_013.md`
- **Layer**: BE
- **Related Test Plans**: None (single-layer feature)
- **Acceptance Criteria Covered**:
  - AC-001: Admin creates Staff/Admin account; credential setup email dispatched
  - AC-002: Admin updates name, role, or status via PATCH; AuditLog written
  - AC-003: Deactivation of last active Admin account returns 409
  - AC-004: Duplicate email on creation returns 409
  - AC-005: Non-Admin access returns 403 (policy enforced at route; verified by convention test)

## Test Plan Overview
Covers the admin user lifecycle vertical slice (`CreateUserEndpoint`, `UpdateUserEndpoint`). Handler methods are called directly. Authorization (401/403 for non-admin JWT) is declared via `.RequireAuthorization("AdminOnly")` and verified by `EndpointAuthorizationConventionTests` — not repeated here. InMemory EF Core + a fake email service are used throughout.

## Dependent Tasks
- TASK_007 — `UserAccount` entity must exist in `ApplicationDbContext`

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `CreateUserEndpoint.HandleCreateUser` | static method | `src/ClinicalHealthcare.Api/Features/Admin/CreateUserEndpoint.cs` | Validates input, checks duplicate email, creates account, stages AuditLog INSERT, sends setup email |
| `UpdateUserEndpoint.HandleUpdateUser` | static method | `src/ClinicalHealthcare.Api/Features/Admin/UpdateUserEndpoint.cs` | Loads account, enforces last-admin guard, applies field updates, stages AuditLog UPDATE |
| `CreateUserEndpoint.Snapshot` | internal static | `src/ClinicalHealthcare.Api/Features/Admin/CreateUserEndpoint.cs` | Serializes account state to anonymous object for before/after audit values |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 [SOURCE:INPUT] | positive | Admin creates Staff account returns 201 | Fresh DB; valid `CreateUserRequest` with `role=staff` | `HandleCreateUser` called with admin `HttpContext` | HTTP 201 | `IStatusCodeHttpResult.StatusCode == 201` — Basis: AC-001 |
| TC-002 [SOURCE:INPUT] | positive | Created account has correct role and IsActive=true | Fresh DB | `HandleCreateUser` called | UserAccount row in DB | `account.Role == "staff"`, `account.IsActive == true` — Basis: AC-001 |
| TC-003 [SOURCE:INPUT] | positive | AuditLog INSERT entry staged on account creation | Fresh DB | `HandleCreateUser` called | AuditLog row inserted | `auditLog.Action == "INSERT"`, `auditLog.EntityType == "UserAccount"`, `auditLog.AfterValue != null` — Basis: AC-002 (US) |
| TC-004 [SOURCE:INPUT] | positive | PATCH updates specified fields and returns 200 | UserAccount exists in DB | `HandleUpdateUser` called with `{ FirstName: "Updated" }` | HTTP 200; FirstName updated | Status 200; `account.FirstName == "Updated"` — Basis: AC-002 |
| TC-005 [SOURCE:INPUT] | positive | AuditLog UPDATE entry contains before/after values | UserAccount exists | `HandleUpdateUser` called with field change | AuditLog UPDATE row | `auditLog.Action == "UPDATE"`, `BeforeValue != null`, `AfterValue != null` — Basis: AC-002 |
| TC-006 [SOURCE:INPUT] | negative | Deactivate last active admin returns 409 | Single active admin UserAccount in DB | `HandleUpdateUser` called with `{ IsActive: false }` on that account | HTTP 409 | `IStatusCodeHttpResult.StatusCode == 409` — Basis: AC-003 |
| TC-007 [SOURCE:INPUT] | negative | Duplicate email returns 409 | UserAccount with `email=dup@test.com` exists | `HandleCreateUser` called with same email | HTTP 409 | `IStatusCodeHttpResult.StatusCode == 409` — Basis: AC-004 |
| TC-008 [SOURCE:INPUT] | negative | Invalid role value returns 422 | Fresh DB | `HandleCreateUser` called with `role = "superuser"` | HTTP 422 | `IStatusCodeHttpResult.StatusCode == 422` — Basis: AC-001 validation |
| EC-001 [SOURCE:INFERRED] | edge_case | Deactivation allowed when 2 active admins exist | Two active admin accounts in DB | `HandleUpdateUser` called with `{ IsActive: false }` on one | HTTP 200 | Status 200; target account `IsActive == false` — Basis: last-admin guard is count-based |
| EC-002 [SOURCE:INFERRED] | edge_case | Blank first/last name in PATCH returns 422 | UserAccount exists | `HandleUpdateUser` called with `{ FirstName: "   " }` | HTTP 422 | `IStatusCodeHttpResult.StatusCode == 422` — Basis: blank name after Trim() fails guard |
| ES-001 [SOURCE:INFERRED] | error | Target user not found returns 404 | Empty DB | `HandleUpdateUser` called with `id = 9999` | HTTP 404 | `IStatusCodeHttpResult.StatusCode == 404` — Basis: account null check in handler |

## AI Component Test Cases
> Skipped — AI Impact = No (no AIR-XXX requirements in scope for this story).

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE (exists) | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/AdminUserEndpointTests.cs` | Unit tests for CreateUserEndpoint and UpdateUserEndpoint — already implemented |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `IEmailService` | `FakeAdminEmailService` stub | Captures sent email for assertion; no SMTP call | `Task.CompletedTask` |
| `IPasswordHasher<string>` | Real `PasswordHasher<string>` | PBKDF2 hashing | Hashed password string |
| `IConfiguration` | `ConfigurationBuilder` in-memory | `App:BaseUrl = "https://localhost:7001"` | IConfiguration |
| `ApplicationDbContext` | EF Core InMemory | Isolated per-test store; `TransactionIgnoredWarning` suppressed | In-memory store |
| `HttpContext` | `new DefaultHttpContext { User = adminClaimsPrincipal }` | Actor ID = 99; role = "admin" | DefaultHttpContext |

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Valid staff creation | `{ Email: "newstaff@test.com", FirstName: "New", LastName: "Staff", Role: "staff" }` | HTTP 201; `role=staff`; `IsActive=true` |
| Duplicate email | Same email, two CreateUser calls | HTTP 409 on second |
| Invalid role | `Role: "superuser"` | HTTP 422 |
| Deactivate last admin | Single active admin; `PATCH { IsActive: false }` | HTTP 409 |
| Two admins, deactivate one | Two active admins; deactivate one | HTTP 200 |
| Blank name PATCH | `PATCH { FirstName: "   " }` | HTTP 422 |
| Unknown user PATCH | `id = 9999` | HTTP 404 |

## Test Commands
- **Run Tests**: `dotnet test ClinicalHealthcare.slnx --filter "FullyQualifiedName~AdminUserEndpointTests" -v q`
- **Run with Coverage**: `dotnet test ClinicalHealthcare.slnx --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~AdminUserEndpointTests"`
- **Run Single Test**: `dotnet test ClinicalHealthcare.slnx --filter "FullyQualifiedName~AdminUserEndpointTests.CreateUser_WithValidRequest_Returns201"`

## Coverage Target
- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**: `HandleUpdateUser` last-admin guard branch; `HandleCreateUser` duplicate-email and role-validation branches must have 100% coverage

## Documentation References
- **Framework Docs**: xUnit 2.x — https://xunit.net/docs
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/AdminUserEndpointTests.cs`
- **Mocking Guide**: `FakeAdminEmailService` defined in test file; Moq used where needed for IConnectionMultiplexer

## Implementation Checklist
- [x] Create test file structure per Expected Changes (`AdminUserEndpointTests.cs` exists)
- [x] Set up test data fixtures per Test Data section (inline data and `SeedUser` helper)
- [x] Configure mocking dependencies per Mocking Strategy
- [x] Implement positive test cases (TC-001 through TC-005)
- [x] Implement negative test cases (TC-006, TC-007, TC-008)
- [x] Implement edge case tests (EC-001, EC-002)
- [x] Implement error scenario tests (ES-001)
- [x] Run test suite and validate coverage meets target (101/101 tests green — 2026-05-24)
