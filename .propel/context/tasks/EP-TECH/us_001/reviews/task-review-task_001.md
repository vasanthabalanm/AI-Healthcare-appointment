# Implementation Analysis — `.propel/context/tasks/EP-TECH/us_001/task_001_angular-spa-scaffold-rbac.md`

## Verdict

**Status:** Conditional Pass
**Summary:** The Angular 17 SPA scaffold is structurally sound and the production build passes with zero errors. All six acceptance criteria have traceable implementations; `AuthGuard`, `RoleGuard`, `RoleRedirectGuard`, and the `ShellComponent` are correctly implemented against the wireframe design system. Four gaps prevent a full Pass: (1) `aria-current="page"` is hardcoded to `null` instead of being driven by `routerLinkActive`, breaking WCAG 2.2 AA navigation landmark semantics; (2) the `app.config.ts` description mentions a "JWT interceptor stub" but none exists — downstream tasks (us_019 onwards) that call the API will fail silently without the Bearer header; (3) the `Implementation Validation Strategy` checklist items remain as unchecked `[ ]` in the task file despite the build and code being complete; (4) `ShellComponent` reads the role on every `computed()` call from `localStorage` rather than from a reactive signal, meaning a live role-change won't re-evaluate the nav without a page reload. All four are addressable without architectural changes.

---

## Traceability Matrix

| Requirement / AC | Evidence (file : function / line) | Result |
|---|---|---|
| AC-001 — `ng build --configuration production` → `dist/`, zero errors | `angular.json` L3; build log: "Application bundle generation complete [11.220s]" | **Pass** |
| AC-002 — `AuthGuard` redirects unauthenticated to `/login?reason=unauthorized`; no throw on malformed JWT | `auth.guard.ts` L11-13; `auth.service.ts` `decodeToken()` L31-41 try/catch; `auth.guard.spec.ts` L27-50 | **Pass** |
| AC-002 edge — JWT expiry redirect to `/login?reason=timeout` before any API call | `auth.guard.ts` L15-18; `auth.guard.spec.ts` L35-40 | **Pass** |
| AC-003 — `RoleGuard` blocks cross-role; `console.warn` violation logged | `role.guard.ts` L14-21; `role.guard.spec.ts` L39-47 `warnSpy` verified | **Pass** |
| AC-003 edge — empty JWT payload with valid signature → no throw | `auth.service.ts` `decodeToken()` L34-36 (3-part check) + try/catch; `auth.guard.spec.ts` L43-49 | **Pass** |
| AC-004 — Shell renders only role-correct nav; cross-role links absent from DOM | `shell.component.ts` `navItems` computed L46-52; `shell.component.html` `@for` L18 (never renders non-role items) | **Pass** |
| AC-004 — `@if` not `[hidden]` for DOM exclusion | `shell.component.html` uses `@for` over role-scoped array — no hidden items possible | **Pass** |
| AC-005 — `environment.production.ts` uses `https://`; no `http://` in prod bundle | `environment.production.ts` L2: `https://api.clinicalhub.app`; `Select-String` grep count = 0 | **Pass** |
| AC-006 — `netlify.toml` present; `/* → /index.html` | `netlify.toml` L1-4: `[[redirects]] from="/*" to="/index.html" status=200` | **Pass** |
| Edge — Wildcard `**` → role-appropriate dashboard, not blank screen | `app.routes.ts` L67: `{ path: '**', canActivate: [roleRedirectGuard] }`; `role-redirect.guard.ts` L12-17 | **Pass** |
| Edge — Admin role change live → next `canActivate` re-reads role | `auth.service.ts` `getCurrentRole()` L51-59 reads from token on every call — no memoisation | **Pass** |
| `app.config.ts` JWT interceptor stub | `app.config.ts` L1-9 — `provideHttpClient(withInterceptorsFromDi())` present, but **no interceptor registered** | **Gap** |
| `aria-current="page"` on active nav item | `shell.component.html` L21: `[attr.aria-current]="null"` — hardcoded null; `routerLinkActive` not used for `aria-current` | **Gap** |
| `Implementation Validation Strategy` items marked complete | Task file `[ ]` items still unchecked after implementation | **Gap** |
| Reactive role change signal | `navItems` uses `computed(() => this.auth.getCurrentRole())` reading localStorage — not a signal-reactive source | **Gap** |

---

## Logical & Design Findings

**Business Logic:**
- `roleRedirectGuard` correctly reads role on every activation — live role changes handled. However, the `ShellComponent.navItems` computed signal re-evaluates on Angular's change detection cycle (not on a signal write), so if a role claim changes in `localStorage` without a navigation event, the sidebar will not update until the next route change. This is acceptable for the scaffold scope but should be documented.
- `decodeToken` uses `atob(parts[1])` — correct for standard Base64; however, Base64URL-encoded JWTs (RFC 7519 §3) use `-` and `_` instead of `+` and `/`. Real JWTs from the .NET 8 backend will use Base64URL. The current implementation will fail silently (returns `null`) for tokens with characters outside standard Base64 alphabet. This is a latent defect that will surface when the backend is integrated.

**Security:**
- `localStorage` for JWT storage is consistent with the task spec (NFR-007), but it carries XSS risk. No Content-Security-Policy meta tag is present in `index.html`. Recommend adding at minimum `<meta http-equiv="Content-Security-Policy" content="default-src 'self'">` — but this is out of scope for this task.
- The `console.warn` violation log in `RoleGuard` leaks route path information to the browser console. In a production HIPAA context this is acceptable (no PHI present in route paths as defined), but should be noted.
- No `HttpInterceptor` stub: the `app.config.ts` comment mentions "JWT interceptor stub" but none is registered. Any `HttpClient` call made by downstream components will be sent without the `Authorization: Bearer` header. This is flagged as a gap for the next integration task.

**Error Handling:**
- `authGuard` calls `auth.clearToken()` before redirecting on expiry (L16) — correct, prevents re-processing an expired token.
- `loginComponent` clears password field on API error (L38) — correct OWASP practice.
- No error boundary for `loadComponent` lazy-load failures (network offline). Angular's default error handler applies. Acceptable for scaffold scope.

**Frontend:**
- `[attr.aria-current]="null"` on nav items means screen readers cannot identify the current page in the navigation. `routerLinkActive` sets the `active` CSS class but does not set `aria-current`. Should be `[attr.aria-current]="rla.isActive ? 'page' : null"` using a `#rla="routerLinkActive"` template reference.
- `shell.component.html` sets `aria-label` on `<aside>` as `(currentRole() ?? 'App') + ' navigation'` — this produces "patient navigation", "staff navigation", "admin navigation" dynamically. Correct and accessible.
- Mobile responsive: `@media (max-width: 768px)` hides sidebar — no mobile nav replacement (hamburger menu). Out of scope for scaffold, but flagged for Mobile Impact = No tracking.

**Performance:**
- All feature routes use `loadComponent` (lazy loading) — correct. No eager bundles beyond the shell.
- `navItems` computed is called on each change detection cycle. The computation is O(1) lookup in `NAV_MAP` — no performance concern.

**Patterns & Standards:**
- Functional guards (`CanActivateFn`) used throughout — Angular 17 idiomatic, matches task spec.
- `computed()` signals used in `ShellComponent` — Angular 17 signal-based reactive model, correct.
- `TOKEN_KEY = 'access_token'` constant defined — no magic strings.
- `ROLE_DASHBOARD` map in `role-redirect.guard.ts` is a module-level constant, not duplicated across guards — DRY compliant.

---

## Test Review

**Existing Tests:**

| File | Cases | Coverage |
|------|-------|----------|
| `auth.service.spec.ts` | 12 | `decodeToken` (4), `isTokenExpired` (4), `getCurrentRole` (3), `isAuthenticated` (2) — all happy + null + edge paths |
| `auth.guard.spec.ts` | 4 | null token, expired token, valid token, empty-payload no-throw |
| `role.guard.spec.ts` | 6 | patient→staff block+warn, staff→patient block, patient→patient pass, staff→staff pass, admin→admin pass, no-roles pass |

**Missing Tests (must add):**

- [ ] **Unit — `auth.service.ts`**: `decodeToken` with Base64URL characters (`-`, `_`) — currently returns `null` silently; will break on real JWT. Must add fix + test.
- [ ] **Unit — `auth.service.ts`**: `setToken` / `clearToken` localStorage side-effects verified.
- [ ] **Unit — `role-redirect.guard.ts`**: All three role values redirect to correct dashboard path; unauthenticated redirects to `/login?reason=unauthorized`.
- [ ] **Unit — `shell.component.ts`**: `navItems()` returns correct array for each role; returns empty array for null role.
- [ ] **Unit — `shell.component.ts`**: `signOut()` clears token and navigates to `/login`.
- [ ] **Unit — `login.component.ts`**: Submit with invalid form → no HTTP call. Valid submit → token stored + redirect.
- [ ] **Integration — `app.routes.ts`**: Route guard chain `authGuard → roleGuard` applied on all three role-protected paths.
- [ ] **Negative — `authGuard`**: Token with `exp` as a string (not number) — `isTokenExpired` should treat as expired (currently returns `true` — verify test).

---

## Validation Results

**Commands Executed:**

| Command | Outcome |
|---------|---------|
| `npx ng build --configuration production` | **Pass** — "Application bundle generation complete [11.220s]" |
| `Select-String dist/**/*.js -Pattern "http://localhost\|http://api"` | **Pass** — Count: 0 |
| `npm install` | **Pass** — dependencies resolved |

**Validation Strategy Checklist (from task file):**

| Item | Status |
|------|--------|
| Unit tests: `AuthGuard` (4 cases) | Written — `auth.guard.spec.ts` |
| Unit tests: `RoleGuard` (3 cases) | Written — `role.guard.spec.ts` |
| Unit tests: `AuthService.decodeToken` (malformed/empty/expired) | Written — `auth.service.spec.ts` |
| `ng build --configuration production` exits 0 + `dist/` exists | **Confirmed** |
| grep `dist/main*.js` for `http://` → zero matches | **Confirmed** |
| Task file `[ ]` items updated to `[x]` | **Checklist updated; Validation Strategy items remain `[ ]`** |

---

## Fix Plan (Prioritized)

| # | Fix | Files / Functions | Effort | Risk |
|---|-----|-------------------|--------|------|
| 1 | **Base64URL decode** — use `atob(parts[1].replace(/-/g,'+').replace(/_/g,'/'))` in `decodeToken` | `auth.service.ts` L35; `auth.service.spec.ts` (add 2 tests) | 15 min | **H** — will break all real JWT decoding without this |
| 2 | **`aria-current="page"`** — use `#rla="routerLinkActive"` template ref + `[attr.aria-current]="rla.isActive ? 'page' : null"` | `shell.component.html` L19-23 | 10 min | M — accessibility/WCAG 2.2 AA |
| 3 | **HTTP interceptor stub** — create `src/app/core/interceptors/auth.interceptor.ts`; register in `app.config.ts` via `provideHttpClient(withInterceptors([authInterceptor]))` | `app.config.ts`; new `auth.interceptor.ts` | 20 min | M — required for all downstream API tasks |
| 4 | **Validation Strategy items** — mark `[ ]` → `[x]` in task file | `task_001_angular-spa-scaffold-rbac.md` Implementation Validation Strategy section | 2 min | L |
| 5 | **Add missing unit tests** — `role-redirect.guard.spec.ts`, `shell.component.spec.ts`, `login.component.spec.ts` | New spec files | 45 min | L |

---

## Appendix

**Rules applied:**
- `rules/security-standards-owasp.md` — OWASP A01 (broken access control), A07 (auth failures)
- `rules/typescript-styleguide.md` — type narrowing, `computed()` usage
- `rules/web-accessibility-standards.md` — WCAG 2.2 AA, `aria-current`
- `rules/frontend-development-standards.md` — Angular 17 standalone patterns
- `rules/language-agnostic-standards.md` — KISS, named constants, no magic strings
- `rules/dry-principle-guidelines.md` — `NAV_MAP` / `ROLE_DASHBOARD` single-source

**Search Evidence:**

| Pattern | File | Result |
|---------|------|--------|
| `localStorage.getItem` | `auth.service.ts` L18 | Single entry point — correct |
| `aria-current` | `shell.component.html` L21 | Hardcoded null — gap |
| `http://` in prod bundle | `dist/clinical-hub/browser/*.js` | 0 matches — pass |
| `canActivateChild` | all `.ts` | 0 — child routes not guarded at child level (parent guard sufficient) |
| `JWT interceptor` | `app.config.ts` | Not registered — gap |
