# Unit Test Plan - TASK_001

## Requirement Reference
- **User Story**: us_001
- **Story Location**: `.propel/context/tasks/EP-TECH/us_001/us_001.md`
- **Layer**: FE
- **Related Test Plans**: [test_plan_fe_auth-service.md](test_plan_fe_auth-service.md), [test_plan_fe_shell.md](test_plan_fe_shell.md)
- **Acceptance Criteria Covered**:
  - AC-002: AuthGuard redirects unauthenticated navigation to `/login?reason=unauthorized`; no unhandled exceptions on malformed/empty JWT
  - AC-003: RoleGuard blocks cross-role route access; Patient JWT cannot reach `/staff/**`; violation logged
  - Edge Case EC-002: Wildcard `**` route redirects to role-appropriate dashboard
  - Edge Case EC-003: JWT expiry on route activation redirects to `/login?reason=timeout`

## Test Plan Overview

This plan covers unit tests for `authGuard`, `roleGuard`, and `roleRedirectGuard` — the three `CanActivateFn` guards protecting routes in `app.routes.ts`. Tests verify redirect UrlTree values, `console.warn` calls, and guard return values across all authenticated, unauthenticated, expired, and cross-role scenarios. All `AuthService` methods and `Router` are mocked via Jasmine spies.

## Dependent Tasks

- `test_plan_fe_auth-service.md` must pass first — guards depend on `AuthService` whose correctness is validated in that plan.

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| authGuard | guard function | `clinical-hub/src/app/core/guards/auth.guard.ts` | JWT presence + expiry check; produces UrlTree for redirect or `true` |
| roleGuard | guard function | `clinical-hub/src/app/core/guards/role.guard.ts` | Role claim check against `route.data.roles`; logs violation; produces UrlTree or `true` |
| roleRedirectGuard | guard function | `clinical-hub/src/app/core/guards/role-redirect.guard.ts` | Maps current role to dashboard UrlTree; unauthenticated → login |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions | Source |
|---------|------|-------------|-------|------|------|------------|--------|
| TC-001 | positive | authGuard returns true for valid non-expired token | `AuthService.getToken()` returns valid JWT; `isTokenExpired()` returns `false` | `authGuard` executes | Returns `true` | `expect(result).toBeTrue()` — no redirect UrlTree | [SOURCE:INPUT] Basis: AC-002 — valid session must pass through guard |
| TC-002 | negative | authGuard redirects to /login?reason=unauthorized when token is null | `AuthService.getToken()` returns `null` | `authGuard` executes | Returns UrlTree navigating to `/login?reason=unauthorized` | `expect(result).toEqual(router.createUrlTree(['/login'], { queryParams: { reason: 'unauthorized' } }))` | [SOURCE:INPUT] Basis: AC-002 — no token triggers unauthorized redirect |
| TC-003 | error | authGuard redirects to /login?reason=timeout and clears token on expiry | `getToken()` returns token; `isTokenExpired()` returns `true` | `authGuard` executes | Returns UrlTree to `/login?reason=timeout`; `clearToken()` is called | `expect(clearTokenSpy).toHaveBeenCalledOnce(); expect(result).toEqual(timeoutUrlTree)` | [SOURCE:INPUT] Basis: AC-002 edge case EC-003 — expired token triggers timeout redirect |
| TC-004 | positive | roleGuard returns true when user role is in allowed list | `getCurrentRole()` returns `'patient'`; `route.data.roles = ['patient']` | `roleGuard` executes | Returns `true` | `expect(result).toBeTrue()` — no redirect | [SOURCE:INPUT] Basis: AC-003 — matching role grants access |
| TC-005 | negative | roleGuard redirects and logs warning for cross-role access | `getCurrentRole()` returns `'patient'`; `route.data.roles = ['staff']` | `roleGuard` executes | Returns UrlTree to `/login?reason=unauthorized`; `console.warn` called with structured object | `expect(warnSpy).toHaveBeenCalledWith('[RoleGuard] Cross-role access blocked', jasmine.objectContaining({ userRole: 'patient', required: ['staff'] })); expect(result).toEqual(unauthorizedUrlTree)` | [SOURCE:INPUT] Basis: AC-003 — cross-role access must be blocked and logged |
| TC-006 | edge_case | roleGuard allows navigation when route has no roles requirement | `getCurrentRole()` returns `'patient'`; `route.data.roles = undefined` | `roleGuard` executes | Returns `true` — no restriction applied | `expect(result).toBeTrue()` | [SOURCE:INFERRED] Basis: public/unprotected routes must not be blocked by RoleGuard when no roles array defined |
| TC-007 | positive | roleRedirectGuard returns patient dashboard UrlTree for patient role | `getCurrentRole()` returns `'patient'` | `roleRedirectGuard` executes | Returns UrlTree for `/patient/dashboard` | `expect(result).toEqual(router.createUrlTree(['/patient/dashboard']))` | [SOURCE:INPUT] Basis: AC-002 edge case EC-002 — wildcard route maps to role-appropriate dashboard |
| TC-008 | negative | roleRedirectGuard redirects unauthenticated user to login | `getCurrentRole()` returns `null` | `roleRedirectGuard` executes | Returns UrlTree for `/login?reason=unauthorized` | `expect(result).toEqual(router.createUrlTree(['/login'], { queryParams: { reason: 'unauthorized' } }))` | [SOURCE:INPUT] Basis: EC-002 — unauthenticated wildcard access must not show blank screen |

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `clinical-hub/src/app/core/guards/auth.guard.spec.ts` | Unit tests for `authGuard` (TC-001–TC-003) |
| CREATE | `clinical-hub/src/app/core/guards/role.guard.spec.ts` | Unit tests for `roleGuard` (TC-004–TC-006) |
| CREATE | `clinical-hub/src/app/core/guards/role-redirect.guard.spec.ts` | Unit tests for `roleRedirectGuard` (TC-007–TC-008) |
| CREATE | `clinical-hub/src/app/core/services/test-data/jwt-fixtures.ts` | Shared JWT fixtures (if not already created by auth-service plan) |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `AuthService` | spy object | `jasmine.createSpyObj('AuthService', ['getToken','isTokenExpired','clearToken','getCurrentRole'])` | Configurable per test |
| `Router` | spy object | `jasmine.createSpyObj('Router', ['createUrlTree','navigate'])` — `createUrlTree` returns real `UrlTree` via `TestBed` router | UrlTree instances |
| `ActivatedRouteSnapshot` | manual stub | `{ data: { roles: [...] }, url: [] }` plain object cast to type | N/A |
| `console.warn` | spy | `spyOn(console, 'warn')` — verify structured log payload | void |

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| No token | `getToken → null` | `authGuard → UrlTree('/login?reason=unauthorized')` |
| Expired token | `getToken → expiredJwt; isTokenExpired → true` | `authGuard → UrlTree('/login?reason=timeout'), clearToken called` |
| Valid token | `getToken → validJwt; isTokenExpired → false` | `authGuard → true` |
| Role match | `getCurrentRole → 'patient'; roles → ['patient']` | `roleGuard → true` |
| Role mismatch | `getCurrentRole → 'patient'; roles → ['staff']` | `roleGuard → UrlTree('/login?reason=unauthorized'), console.warn called` |
| No roles data | `getCurrentRole → 'patient'; roles → undefined` | `roleGuard → true` |
| Patient redirect | `getCurrentRole → 'patient'` | `roleRedirectGuard → UrlTree('/patient/dashboard')` |
| Staff redirect | `getCurrentRole → 'staff'` | `roleRedirectGuard → UrlTree('/staff/schedule')` |
| Admin redirect | `getCurrentRole → 'admin'` | `roleRedirectGuard → UrlTree('/admin/users')` |
| Unauthenticated redirect | `getCurrentRole → null` | `roleRedirectGuard → UrlTree('/login?reason=unauthorized')` |

## Test Commands

- **Run Tests**: `npx ng test --include="**/guards/*.spec.ts" --watch=false`
- **Run with Coverage**: `npx ng test --watch=false --code-coverage --include="**/guards/*.spec.ts"`
- **Run Single Test**: `npx ng test --watch=false --include="**/auth.guard.spec.ts"`

## Coverage Target

- **Line Coverage**: 100%
- **Branch Coverage**: 100%
- **Critical Paths**: Every branch of `authGuard` (null token, expired, valid); every branch of `roleGuard` (no roles, role match, role mismatch, null user role); all 4 ROLE_DASHBOARD entries in `roleRedirectGuard` (patient, staff, admin, null)

## Documentation References

- **Framework Docs**: [Angular Route Guards — https://angular.io/guide/router#preventing-unauthorized-access](https://angular.io/guide/router#preventing-unauthorized-access)
- **Project Test Patterns**: `clinical-hub/src/app/core/guards/auth.guard.ts`
- **Mocking Guide**: [Jasmine spyObj — https://jasmine.github.io/api/5.1/jasmine.html#.createSpyObj](https://jasmine.github.io/api/5.1/jasmine.html#.createSpyObj)

## Implementation Checklist

- [x] Create test file structure per Expected Changes
- [x] Set up `AuthService` and `Router` spy objects in `beforeEach` via `TestBed.configureTestingModule`
- [x] Configure `RouterTestingModule` for UrlTree comparison
- [x] Implement positive test cases: TC-001, TC-004, TC-007
- [x] Implement negative test cases: TC-002, TC-005, TC-008
- [x] Implement edge case tests: TC-006
- [x] Implement error scenario tests: TC-003
- [x] Run test suite and validate 100% line + branch coverage on all three guard files
