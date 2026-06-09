# Unit Test Plan - TASK_001

## Requirement Reference
- **User Story**: us_001
- **Story Location**: `.propel/context/tasks/EP-TECH/us_001/us_001.md`
- **Layer**: FE
- **Related Test Plans**: [test_plan_fe_auth-guard.md](test_plan_fe_auth-guard.md), [test_plan_fe_shell.md](test_plan_fe_shell.md)
- **Acceptance Criteria Covered**:
  - AC-002: AuthGuard redirects unauthenticated navigation; no unhandled exceptions on malformed/empty JWT
  - AC-003: RoleGuard blocks cross-role access; role extracted from decoded JWT

## Test Plan Overview

This plan covers unit tests for `AuthService` — the foundational JWT accessor and decoder used by both `authGuard` and `roleGuard`. Tests validate token read/write, Base64URL decoding, expiry comparison, and role extraction. All `localStorage` and `Date.now` calls are mocked so tests are fully isolated and deterministic.

## Dependent Tasks

- None — `AuthService` has no upstream runtime dependency other than `HttpClient` (mocked via `HttpClientTestingModule`)

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| AuthService | service | `clinical-hub/src/app/core/services/auth.service.ts` | JWT storage accessor, Base64URL decoder, expiry checker, role extractor |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions | Source |
|---------|------|-------------|-------|------|------|------------|--------|
| TC-001 | positive | Returns stored token from localStorage | localStorage contains `access_token = 'mock.jwt.token'` | `getToken()` is called | Returns the stored string | `expect(result).toBe('mock.jwt.token')` | [SOURCE:INPUT] Basis: AC-002 — token presence check is core guard prerequisite |
| TC-002 | negative | Returns null when localStorage is empty | localStorage is empty (no `access_token` key) | `getToken()` is called | Returns null | `expect(result).toBeNull()` | [SOURCE:INPUT] Basis: AC-002 — null token must trigger unauthorized redirect |
| TC-003 | positive | Decodes valid Base64URL JWT payload to typed object | Valid JWT with Base64URL-encoded payload `{"sub":"1","role":"patient","exp":9999999999}` | `decodeToken(validJwt)` is called | Returns parsed `JwtPayload` object with correct fields | `expect(result?.role).toBe('patient'); expect(result?.sub).toBe('1'); expect(result?.exp).toBe(9999999999)` | [SOURCE:INPUT] Basis: AC-002, AC-003 — role and expiry extracted from payload |
| TC-004 | edge_case | Returns null for JWT with fewer than 3 parts | Token string `'only.two'` (2 parts, not 3) | `decodeToken('only.two')` is called | Returns null without throwing | `expect(result).toBeNull()` — no exception thrown | [SOURCE:INPUT] Basis: AC-002 edge case — malformed JWT must not throw |
| TC-005 | edge_case | Returns null for JWT with empty second segment | Token string `'header..signature'` (empty payload part) | `decodeToken('header..signature')` is called | Returns null without throwing | `expect(result).toBeNull()` — no exception propagates | [SOURCE:INPUT] Basis: AC-002 edge case — empty payload with valid signature structure |
| TC-006 | positive | Returns false (not expired) for token with future exp | JWT payload with `exp: Math.floor(Date.now()/1000) + 3600` (1 hour ahead) | `isTokenExpired(token)` is called | Returns `false` | `expect(result).toBeFalse()` | [SOURCE:INPUT] Basis: AC-002 — valid non-expired token must pass guard |
| TC-007 | negative | Returns true (expired) for token with past exp | JWT payload with `exp: 1000000000` (year 2001, in past) | `isTokenExpired(token)` is called | Returns `true` | `expect(result).toBeTrue()` | [SOURCE:INPUT] Basis: AC-002 — expired token triggers timeout redirect |
| TC-008 | positive | Extracts valid UserRole from token claim | localStorage contains JWT with `role: 'staff'` in payload | `getCurrentRole()` is called | Returns `'staff'` | `expect(result).toBe('staff')` | [SOURCE:INPUT] Basis: AC-003, AC-004 — role drives RoleGuard and shell nav |

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `clinical-hub/src/app/core/services/auth.service.spec.ts` | Unit tests for AuthService — all 8 TCs |
| CREATE | `clinical-hub/src/app/core/services/test-data/jwt-fixtures.ts` | Shared JWT test data (valid, expired, malformed) |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `localStorage.getItem` | spy | `spyOn(localStorage, 'getItem')` — configurable per test | `'mock.jwt.token'` or `null` |
| `localStorage.setItem` | spy | `spyOn(localStorage, 'setItem')` — verify calls | void |
| `localStorage.removeItem` | spy | `spyOn(localStorage, 'removeItem')` — verify calls | void |
| `Date.now` | spy | `spyOn(Date, 'now').and.returnValue(fixedTimestamp)` | Fixed `number` to control expiry comparison |
| `HttpClient` | stub | `HttpClientTestingModule` — not exercised in these TCs | N/A |

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Valid patient JWT | `eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIiwicm9sZSI6InBhdGllbnQiLCJleHAiOjk5OTk5OTk5OTl9.sig` | `{ sub: '1', role: 'patient', exp: 9999999999 }` |
| Valid staff JWT | `eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIyIiwicm9sZSI6InN0YWZmIiwiZXhwIjo5OTk5OTk5OTk5fQ.sig` | `{ sub: '2', role: 'staff', exp: 9999999999 }` |
| Expired JWT | JWT with `exp: 1000000000` (Sep 2001) | `isTokenExpired → true` |
| Two-part malformed | `'not.valid'` | `decodeToken → null` |
| Empty payload | `'header..signature'` | `decodeToken → null` |
| Invalid base64 | `'header.!!!.signature'` | `decodeToken → null` |
| Unknown role claim | JWT with `role: 'superuser'` | `getCurrentRole → null` |

## Test Commands

- **Run Tests**: `npx ng test --include="**/auth.service.spec.ts" --watch=false`
- **Run with Coverage**: `npx ng test --watch=false --code-coverage --include="**/auth.service.spec.ts"`
- **Run Single Test**: `npx ng test --watch=false --include="**/auth.service.spec.ts" --grep="TC-001"`

## Coverage Target

- **Line Coverage**: 100%
- **Branch Coverage**: 100%
- **Critical Paths**: All branches in `decodeToken` (valid/invalid base64, wrong part count, null payload object), all branches in `isTokenExpired` (null payload, missing exp, expired, valid), all branches in `getCurrentRole` (null token, invalid role string, valid role string)

## Documentation References

- **Framework Docs**: [Jasmine 5.1 — https://jasmine.github.io/api/5.1/global](https://jasmine.github.io/api/5.1/global)
- **Project Test Patterns**: `clinical-hub/src/app/core/services/auth.service.ts`
- **Mocking Guide**: [Angular TestBed — https://angular.io/guide/testing-services](https://angular.io/guide/testing-services)

## Implementation Checklist

- [x] Create test file structure per Expected Changes
- [x] Set up test data fixtures in `jwt-fixtures.ts`
- [x] Configure `HttpClientTestingModule` and localStorage spies in `beforeEach`
- [x] Implement positive test cases: TC-001, TC-003, TC-006, TC-008
- [x] Implement negative test cases: TC-002, TC-007
- [x] Implement edge case tests: TC-004, TC-005
- [x] Run test suite and validate coverage meets 100% line + branch target
