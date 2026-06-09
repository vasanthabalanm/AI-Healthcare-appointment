# Implementation Analysis — task_014_rbac-policy-enforcement.md

## Verdict

**Status:** Conditional Pass  
**Summary:** All structural requirements for TASK_014 are in place — three named policies registered (`AdminOnly`, `StaffOrAdmin`, `AnyAuthenticated`), convention test passing (1/1), endpoints annotated, and `RbacViolationHandler` compiled and registered. However, the middleware is positioned **after** `UseAuthorization()` in the pipeline, which means `InvokeAsync` is never invoked when a 403 short-circuit occurs. The `Response.OnStarting` callback is therefore never registered, and `RBAC-Violation` audit entries are **never written in production**. This directly breaks AC-002. One critical fix (move middleware before `UseAuthorization`) is required before marking the task complete.

---

## Traceability Matrix

| AC ID | Requirement | Evidence | Result |
|-------|-------------|----------|--------|
| AC-001 | Every endpoint has `[Authorize]` or `[AllowAnonymous]` | `RegisterEndpoint.cs:64,74` (AllowAnonymous), `CreateUserEndpoint.cs:44` (RequireAuthorization), `UpdateUserEndpoint.cs:32` (RequireAuthorization) | **Pass** |
| AC-001 | Startup convention test asserts metadata | `EndpointAuthorizationConventionTests.cs:27-72` | **Pass** |
| AC-002 | Cross-role 403 writes AuditLog `RBAC-Violation` | `RbacViolationHandler.cs` exists; but `Program.cs:163` places it **after** `UseAuthorization()` — short-circuits never reach InvokeAsync | **Fail** |
| AC-003 | Admin-only endpoints reject Staff/Patient JWTs with 403 | `AdminOnly` policy registered with `RequireRole("admin")`; both Admin endpoints use it | **Pass** (policy correct; execution depends on F1 fix) |
| AC-004 | StaffOrAdmin endpoints reject Patient JWTs with 403 | `StaffOrAdmin` policy registered; no endpoint currently uses it; no test verifies rejection behavior | **Gap** |
| AC-005 | Convention test fails build on missing auth attribute | Convention test reflects `IEndpointDefinition` types and checks metadata — verified passing `1/1` | **Pass** |
| Edge case | AuditLog entry includes ActorId, AttemptedEndpoint, RequiredRole, ActualRole | `RbacViolationHandler.cs:63-78` — all four fields in AfterValue JSON | **Pass** (blocked by F1) |

---

## Logical & Design Findings

- **Business Logic:** The `OnStarting` callback pattern is architecturally sound for audit-on-403, but only if the middleware is upstream of `UseAuthorization`. Currently it is downstream, so the callback is never registered when authorization short-circuits.

- **Security:** Audit failure is silently swallowed (`catch { }` on lines 87-90) — correct, as audit must not suppress the 403. No PII is logged in the RBAC entry beyond endpoint path and role names.

- **Error Handling:** `ResolveRequiredRolesAsync` returns `string.Empty` when no policy/roles metadata is found — safe default. Audit entry is still written with `requiredRole = ""`, which is traceable.

- **Data Access:** `SaveChangesAsync()` called without a `CancellationToken` in `WriteViolationAuditIfForbiddenAsync`. `OnStarting` callbacks do not receive a token; this is acceptable since the request is already committed to responding.

- **Patterns & Standards:** `IServiceScopeFactory` used correctly — avoids captive-dependency anti-pattern for scoped `ApplicationDbContext`. Per-slice `AddAuthorization` guards (`is null`) remain safe no-ops because central registration in `Program.cs` runs first.

- **Convention Test Scope Gap:** The test only scans `IEndpointDefinition` types. Two non-definition routes (`/health` via `MapHealthChecks`, `/hangfire` via `UseHangfireDashboard`) are not covered. The health endpoint has no auth metadata but is intentionally public — this represents an untested assumption.

---

## Test Review

**Existing Tests:**
- `EndpointAuthorizationConventionTests.AllEndpoints_HaveAuthorizationOrAllowAnonymousMetadata` — passes (1/1). Covers AC-005 metadata enforcement.
- No tests for AC-002 (audit write on 403).
- No tests for AC-004 (StaffOrAdmin role rejection).

**Missing Tests (must add):**

- [ ] Unit: `RbacViolationHandler_OnForbidden_WritesAuditLogEntry` — create in-memory middleware test; assert `db.AuditLogs` contains one entry with `Action == "RBAC-Violation"` after a response with status 403.
- [ ] Unit: `RbacViolationHandler_OnSuccess_NoAuditEntry` — status 200 must NOT produce an audit entry.
- [ ] Unit: `StaffOrAdmin_Policy_RejectsPatientRole` — use `AuthorizationService` directly with a "patient" claims principal against the `StaffOrAdmin` policy; assert `Forbidden`.
- [ ] Negative/Edge: `RbacViolationHandler_AuditFailure_DoesNotAlterResponseStatus` — make DB throw; verify response still returns 403.

---

## Validation Results

**Commands Executed:**
```
dotnet build -v quiet
dotnet test --no-build --logger "console;verbosity=minimal"
```

**Outcomes:**
- Build: 0 errors, 0 warnings ✅
- `ClinicalHealthcare.Api.Tests`: 1 passed, 0 failed ✅
- `ClinicalHealthcare.Infrastructure.Tests`: 120 passed, 0 failed ✅

---

## Fix Plan (Prioritized)

| # | Finding | Severity | File / Line | Fix | Risk |
|---|---------|----------|-------------|-----|------|
| F1 | Middleware placed after `UseAuthorization` — `InvokeAsync` never called on 403 short-circuit; AC-002 broken | **CRITICAL** | `Program.cs:162-165` | Move `app.UseMiddleware<RbacViolationHandler>()` to **before** `app.UseAuthorization()` | Low |
| F2 | No unit test verifying audit write on 403 (AC-002) | **HIGH** | New test file | Add `RbacViolationHandler_OnForbidden_WritesAuditLogEntry` | Low |
| F3 | No test demonstrating `StaffOrAdmin` policy rejects Patient JWT (AC-004) | **LOW** | New test or `EndpointAuthorizationConventionTests.cs` | Add policy assertion test using `AuthorizationService` | Low |
| F4 | Convention test scope excludes `/health` and `/hangfire` endpoints | **LOW** | `EndpointAuthorizationConventionTests.cs` | Document known exclusions as inline comment (no code change needed) | Negligible |

---

## Appendix

**Search Evidence:**
- `grep RequireAuthorization src/**/*.cs` → 2 matches (CreateUser, UpdateUser endpoints)
- `grep AllowAnonymous src/**/*.cs` → 2 matches (RegisterEndpoint POST + GET)
- `grep UseMiddleware.*RbacViolation Program.cs` → line 165, positioned after UseAuthorization at line 163
- `dotnet test` → 121/121 pass
