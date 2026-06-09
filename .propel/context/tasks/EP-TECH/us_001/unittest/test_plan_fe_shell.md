# Unit Test Plan - TASK_001

## Requirement Reference
- **User Story**: us_001
- **Story Location**: `.propel/context/tasks/EP-TECH/us_001/us_001.md`
- **Layer**: FE
- **Related Test Plans**: [test_plan_fe_auth-service.md](test_plan_fe_auth-service.md), [test_plan_fe_auth-guard.md](test_plan_fe_auth-guard.md)
- **Acceptance Criteria Covered**:
  - AC-004: Role-aware navigation shell renders correct menu per role; cross-role links absent from DOM
  - AC-005: Production environment `apiBaseUrl` uses `https://`; no `http://` origins in production bundle
  - AC-006: `netlify.toml` present with `/* → /index.html` SPA redirect rule

## Test Plan Overview

This plan covers: (1) unit tests for `ShellComponent` — verifying role-scoped navigation links are DOM-present or DOM-absent based on `AuthService.getCurrentRole()`; (2) static assertions against `environment.production.ts` to confirm HTTPS-only base URL; (3) a file-content assertion on `netlify.toml` to confirm SPA redirect configuration. No network calls are made. All `AuthService` calls are mocked.

## Dependent Tasks

- `test_plan_fe_auth-service.md` — `ShellComponent` injects `AuthService`; correctness of role extraction is validated there.

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| ShellComponent | component | `clinical-hub/src/app/layout/shell/shell.component.ts` | Role-scoped nav link rendering; DOM-gates links via `@if` (not `[hidden]`) |
| environment.production | config file | `clinical-hub/src/environments/environment.production.ts` | Production `apiBaseUrl` — must use `https://` |
| netlify.toml | config file | `clinical-hub/netlify.toml` | SPA redirect rule `/* → /index.html, status 200` |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions | Source |
|---------|------|-------------|-------|------|------|------------|--------|
| TC-001 | positive | Patient role: patient nav links are present in the DOM | `AuthService.getCurrentRole()` returns `'patient'`; component rendered | Shell template evaluated | DOM contains links for Dashboard, Book Appointment, My Appointments, My Documents | `expect(fixture.nativeElement.querySelector('[routerLink="/patient/dashboard"]')).toBeTruthy()` for each patient link | [SOURCE:INPUT] Basis: AC-004 — patient shell must show patient nav |
| TC-002 | positive | Staff role: staff nav links are present in the DOM | `AuthService.getCurrentRole()` returns `'staff'`; component rendered | Shell template evaluated | DOM contains links for Daily Schedule, Walk-In Registration, Same-Day Queue | `expect(fixture.nativeElement.querySelector('[routerLink="/staff/schedule"]')).toBeTruthy()` for each staff link | [SOURCE:INPUT] Basis: AC-004 — staff shell must show staff nav |
| TC-003 | negative | Patient role: staff nav links are completely absent from DOM (not hidden) | `AuthService.getCurrentRole()` returns `'patient'`; component rendered | Shell template evaluated | DOM does not contain any element with `/staff/` routes | `expect(fixture.nativeElement.querySelector('[routerLink="/staff/schedule"]')).toBeNull()` — element must not exist, not merely hidden | [SOURCE:INPUT] Basis: AC-004 — cross-role links must be absent from DOM, not just visually hidden |
| TC-004 | positive | Production apiBaseUrl uses https:// scheme | `environment.production.ts` imported | `apiBaseUrl` property read | Value starts with `https://` | `expect(productionEnv.apiBaseUrl.startsWith('https://')).toBeTrue(); expect(productionEnv.apiBaseUrl.startsWith('http://')).toBeFalse()` | [SOURCE:INPUT] Basis: AC-005 — no http:// origins in production bundle |
| TC-005 | positive | netlify.toml contains SPA redirect rule for /* | `netlify.toml` read from file system via `fs.readFileSync` in Node test context | File content parsed | String contains `from = "/*"`, `to = "/index.html"`, `status = 200` | `expect(tomlContent).toContain('from = "/*"'); expect(tomlContent).toContain('to = "/index.html"'); expect(tomlContent).toContain('status = 200')` | [SOURCE:INPUT] Basis: AC-006 — SPA redirect required for deep-linking |

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `clinical-hub/src/app/layout/shell/shell.component.spec.ts` | Unit tests for ShellComponent role-aware DOM rendering (TC-001–TC-003) |
| CREATE | `clinical-hub/src/environments/environment.production.spec.ts` | Static assertion test for https:// in production config (TC-004) |
| CREATE | `clinical-hub/src/app/core/config/netlify.spec.ts` | File-content assertion test for SPA redirect rule (TC-005) |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `AuthService` | spy object | `jasmine.createSpyObj('AuthService', ['getCurrentRole','getToken','isAuthenticated'])` | `getCurrentRole` returns configurable role string per test |
| `RouterModule` | stub | `RouterTestingModule` — provides router outlet and link directives without navigation | N/A |
| `HttpClient` | stub | `HttpClientTestingModule` — not exercised in shell tests | N/A |
| `fs.readFileSync` (TC-005) | none — real call | Test reads actual `netlify.toml` from `process.cwd()` | Raw file string content |

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Patient role nav | `getCurrentRole → 'patient'` | `/patient/dashboard`, `/patient/book`, `/patient/appointments`, `/patient/documents` present |
| Staff role nav | `getCurrentRole → 'staff'` | `/staff/schedule`, `/staff/walkin`, `/staff/queue` present; patient links absent |
| Cross-role absence | `getCurrentRole → 'patient'` | `/staff/schedule` element is `null` in DOM query |
| Production env HTTPS | `environment.production.apiBaseUrl` | Starts with `https://` |
| netlify.toml content | `netlify.toml` file string | Contains `from = "/*"`, `to = "/index.html"`, `status = 200` |

## Test Commands

- **Run Tests**: `npx ng test --include="**/shell.component.spec.ts" --watch=false`
- **Run with Coverage**: `npx ng test --watch=false --code-coverage --include="**/layout/shell/**"`
- **Run Single Test**: `npx ng test --watch=false --include="**/environment.production.spec.ts"`

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 100%
- **Critical Paths**: All `@if` role-gate branches in shell template (patient visible/absent, staff visible/absent, admin visible/absent); `environment.production.apiBaseUrl` value assertion; `netlify.toml` redirect block presence

## Documentation References

- **Framework Docs**: [Angular ComponentFixture — https://angular.io/api/core/testing/ComponentFixture](https://angular.io/api/core/testing/ComponentFixture)
- **Project Test Patterns**: `clinical-hub/src/app/layout/shell/shell.component.ts`
- **Mocking Guide**: [Angular RouterTestingModule — https://angular.io/api/router/testing/RouterTestingModule](https://angular.io/api/router/testing/RouterTestingModule)

## Implementation Checklist

- [x] Create test file structure per Expected Changes
- [x] Configure `TestBed` with `RouterTestingModule`, `AuthService` spy, `HttpClientTestingModule`
- [x] Implement positive test cases: TC-001, TC-002, TC-004, TC-005
- [x] Implement negative test cases: TC-003
- [x] Assert DOM element presence using `fixture.nativeElement.querySelector` (not `[hidden]`)
- [x] Run test suite and validate 90% line + 100% branch coverage on ShellComponent
