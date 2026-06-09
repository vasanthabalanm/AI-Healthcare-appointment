# Task - TASK_001

## Requirement Reference
- **User Story:** us_001
- **Story Location:** `.propel/context/tasks/EP-TECH/us_001/us_001.md`
- **Acceptance Criteria:**
  - AC-001: Angular project initialises and builds with zero errors (`ng build --configuration production` → `dist/`)
  - AC-002: `AuthGuard` redirects unauthenticated navigation to `/login?reason=unauthorized`; no unhandled exceptions on malformed/empty JWT
  - AC-003: `RoleGuard` blocks cross-role route access; Patient JWT cannot reach `/staff/**`; violation logged
  - AC-004: Layout shell renders only role-correct nav links; cross-role links absent from the DOM (not just hidden)
  - AC-005: `environment.production.ts` API base URL uses `https://`; no `http://` origins in production bundle
  - AC-006: `netlify.toml` (or `vercel.json`) present with `/* → /index.html` SPA redirect rule; deep-linking works post-deploy
- **Edge Cases:**
  - Empty JWT payload with valid signature → `AuthGuard` must call `router.navigate(['/login'], { queryParams: { reason: 'unauthorized' } })` without throwing
  - Wildcard `**` route → redirects to role-appropriate dashboard (not a blank screen)
  - JWT expiry detected on route activation → redirect to `/login?reason=timeout` before any API call is made
  - Admin changes user role while session is live → next `canActivate` call re-reads role from decoded token and enforces updated permissions

---

## Design References
| Reference Type | Value |
|----------------|-------|
| **UI Impact** | No |
| **Figma URL** | N/A |
| **Wireframe Status** | N/A |
| **Wireframe Type** | N/A |
| **Wireframe Path/URL** | N/A |
| **Screen Spec** | N/A |
| **UXR Requirements** | N/A |
| **Design Tokens** | N/A |

---

## AI References
| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

---

## Mobile References
| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

---

## Applicable Technology Stack

| Layer | Technology | Version | Justification |
|-------|------------|---------|---------------|
| Frontend | Angular | 17.x LTS | TR-001: Angular 17.x mandated by BRD §5; Route Guard RBAC, reactive forms, standalone component model |
| Frontend | Angular CLI | 17.x | TR-001; `ng new` scaffold, `ng build`, `ng generate` toolchain |
| Frontend | TypeScript | 5.2.x (Angular 17 peer) | NFR-005; type-safe guard and service implementations; Angular 17 requires TypeScript 5.2 |
| Frontend | RxJS | 7.8.x (Angular 17 peer) | NFR-003; Observable-based route guard resolution; non-blocking async JWT validation |
| Auth | JWT (client-side decode) | N/A — no external JWT library; use `atob()` + JSON.parse | NFR-007; avoid third-party dependency for decode-only; production validation is server-side |
| Infrastructure | Netlify | Current | NFR-011; free static hosting for Angular SPA; `netlify.toml` configures SPA redirect rules |
| Deployment | GitHub Actions | Current | NFR-011; CI/CD pipeline triggers `ng build --configuration production` on push to `main` |

---

## Task Overview

Scaffold the Angular 17 LTS SPA as the frontend foundation for ClinicalHub. This task produces: the `ng new` project shell, two environment files (dev/prod with HTTPS base URL), `AuthGuard` and `RoleGuard` implementing client-side RBAC, the root `AppRoutingModule` with protected routes and wildcard redirect, and a role-aware layout shell that renders navigation items exclusively matched to the authenticated user's role. A `netlify.toml` (SPA redirect rule) is included to support deep-linking on Netlify static hosting. No feature modules are implemented — this task is the foundation all subsequent frontend stories depend on.

---

## Dependent Tasks

- None — this is the initial frontend scaffold. Uses mocked user data and stub JWT payloads for guard testing.

---

## Impacted Components

- `src/app/app.config.ts` — root application providers (router, HTTP client, provideHttpClient, JWT interceptor stub)
- `src/app/app.routes.ts` — top-level route definitions; protected routes with `AuthGuard` / `RoleGuard` applied via `canActivate`
- `src/app/core/guards/auth.guard.ts` — NEW: checks JWT presence and expiry; redirects to `/login?reason=unauthorized` or `/login?reason=timeout`
- `src/app/core/guards/role.guard.ts` — NEW: extracts role claim from decoded JWT; blocks cross-role access; logs violation to `console.warn` (Serilog-compatible structure for backend integration)
- `src/app/core/services/auth.service.ts` — NEW: JWT storage accessor (`localStorage`), decode helper, role/expiry extraction
- `src/app/layout/shell/shell.component.ts` — NEW: root layout shell; renders `<app-sidebar>` and `<app-navbar>` with role-scoped nav items driven by `AuthService.currentRole`
- `src/environments/environment.ts` — `apiBaseUrl: 'http://localhost:5000'` (development)
- `src/environments/environment.production.ts` — `apiBaseUrl: 'https://<production-domain>'` (production; HTTPS only)
- `netlify.toml` — NEW: SPA redirect rule `[[redirects]] from = "/*" to = "/index.html" status = 200`

---

## Implementation Plan

1. **Scaffold Angular 17 project** — run `ng new clinical-hub --routing --style scss --standalone` (standalone component model, no NgModules). Confirm `package.json` shows Angular `^17.x`.
2. **Configure environments** — update `src/environments/environment.ts` with `apiBaseUrl: 'http://localhost:5000'`; update `src/environments/environment.production.ts` with `apiBaseUrl: 'https://<production-domain>'`. Validate no `http://` origin in the production file.
3. **Implement `AuthService`** — pure TypeScript class (`@Injectable({ providedIn: 'root' })`). Methods: `getToken(): string | null` (reads `localStorage.getItem('access_token')`), `decodeToken(token: string): Record<string, unknown> | null` (try/catch around `JSON.parse(atob(token.split('.')[1]))`; returns `null` on malformed input — no throw), `isTokenExpired(token: string): boolean` (compare decoded `exp` × 1000 against `Date.now()`), `getCurrentRole(): string | null`.
4. **Implement `AuthGuard`** — `canActivate()` function: (1) get token via `AuthService.getToken()`; if null → redirect to `/login?reason=unauthorized`, return `false`; (2) if `AuthService.isTokenExpired(token)` → redirect to `/login?reason=timeout`, return `false`; (3) return `true`. Must never throw — all JWT decode wrapped in try/catch in `AuthService`.
5. **Implement `RoleGuard`** — `canActivate(route: ActivatedRouteSnapshot)` function: reads `route.data['roles']` (string[]); calls `AuthService.getCurrentRole()`; if role not in allowed list → `console.warn('[RoleGuard] Cross-role access blocked', { route: route.url, userRole })` → redirect to `/login?reason=unauthorized`, return `false`; else return `true`.
6. **Configure `app.routes.ts`** — define routes: `/login` (public), `/register` (public), `/patient/**` (canActivate: `[AuthGuard, RoleGuard]`, data: `{ roles: ['patient'] }`), `/staff/**` (data: `{ roles: ['staff'] }`), `/admin/**` (data: `{ roles: ['admin'] }`), `''` → redirect to `/patient/dashboard`, `'**'` → redirect to role-appropriate dashboard via `RoleRedirectGuard` (reads current role → returns `UrlTree` for role root).
7. **Implement `ShellComponent`** — standalone layout component; inject `AuthService`; bind `navItems` computed from current role (patient nav: Dashboard, Book, Appointments, Documents; staff nav: Schedule, Walk-In, Queue, Patients; admin nav: Users, Audit Log); use `@if (navItems.includes(item))` to gate DOM presence (not `[hidden]`).
8. **Add `netlify.toml` and verify production build** — create `netlify.toml` at repo root with `[[redirects]] from = "/*" to = "/index.html" status = 200`; run `ng build --configuration production`; confirm `dist/` produced, no `http://` in `main.js`, build exits 0.

---

## Current Project State

```
clinical-hub/                ← Angular 17 project (to be created via ng new)
├── src/
│   ├── app/
│   │   ├── app.config.ts
│   │   ├── app.routes.ts
│   │   ├── core/
│   │   │   ├── guards/
│   │   │   │   ├── auth.guard.ts
│   │   │   │   └── role.guard.ts
│   │   │   └── services/
│   │   │       └── auth.service.ts
│   │   └── layout/
│   │       └── shell/
│   │           ├── shell.component.ts
│   │           ├── shell.component.html
│   │           └── shell.component.scss
│   └── environments/
│       ├── environment.ts
│       └── environment.production.ts
├── netlify.toml
├── angular.json
├── package.json
└── tsconfig.json
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `clinical-hub/` | Angular 17 project root — output of `ng new clinical-hub --routing --style scss --standalone` |
| CREATE | `clinical-hub/src/app/core/guards/auth.guard.ts` | `AuthGuard`: JWT presence + expiry check; redirects to `/login?reason=unauthorized` or `reason=timeout` |
| CREATE | `clinical-hub/src/app/core/guards/role.guard.ts` | `RoleGuard`: role claim check against route `data.roles`; violation logged and redirect fired |
| CREATE | `clinical-hub/src/app/core/guards/role-redirect.guard.ts` | `RoleRedirectGuard`: reads current role; returns `UrlTree` to role-appropriate dashboard for wildcard `**` route |
| CREATE | `clinical-hub/src/app/core/services/auth.service.ts` | JWT accessor, decoder (no-throw), expiry checker, role extractor |
| CREATE | `clinical-hub/src/app/layout/shell/shell.component.ts` | Root layout shell with role-scoped `navItems`; DOM-gated nav rendering |
| CREATE | `clinical-hub/src/app/layout/shell/shell.component.html` | Shell template: `<app-navbar>`, `<aside>` nav, `<router-outlet>` |
| CREATE | `clinical-hub/src/app/layout/shell/shell.component.scss` | Shell layout styles (56px navbar, 240px sidebar — aligns with design token `--nh` / `--sw`) |
| MODIFY | `clinical-hub/src/app/app.routes.ts` | Add protected routes with `canActivate: [AuthGuard, RoleGuard]`, `data: { roles: [...] }`, wildcard redirect |
| MODIFY | `clinical-hub/src/environments/environment.ts` | Set `apiBaseUrl: 'http://localhost:5000'` |
| MODIFY | `clinical-hub/src/environments/environment.production.ts` | Set `apiBaseUrl: 'https://<production-domain>'` — no `http://` |
| CREATE | `clinical-hub/netlify.toml` | SPA redirect: `[[redirects]] from = "/*" to = "/index.html" status = 200` |

---

## External References

- Angular 17 Route Guards: https://angular.dev/guide/routing/common-router-tasks#preventing-unauthorized-access
- Angular 17 Standalone Components: https://angular.dev/guide/components/importing
- Angular 17 Environment Configuration: https://angular.dev/tools/cli/environments
- Angular 17 `canActivate` functional guards: https://angular.dev/api/router/CanActivateFn
- Netlify SPA Redirects: https://docs.netlify.com/routing/redirects/rewrites-proxies/#history-pushstate-and-single-page-apps
- OWASP JWT Security Cheat Sheet: https://cheatsheetseries.owasp.org/cheatsheets/JSON_Web_Token_for_Java_Cheat_Sheet.html

---

## Build Commands

Refer to Angular 17 build commands in `.propel/build/frontend.md` (to be created). Key commands:
- `npm install` — install dependencies
- `ng build --configuration production` — production build to `dist/`
- `ng serve` — development server (`http://localhost:4200`)
- `ng test` — run Karma/Jasmine unit tests

---

## Implementation Validation Strategy

- [x] Unit tests: `AuthGuard` — test cases for (a) null token → redirect unauthorized, (b) expired token → redirect timeout, (c) empty payload with valid signature → no unhandled exception, (d) valid token → returns `true`
- [x] Unit tests: `RoleGuard` — test cases for (a) patient role accessing `/staff/schedule` → redirect + log, (b) staff role accessing `/patient/dashboard` → redirect + log, (c) correct role → returns `true`
- [x] Unit tests: `AuthService.decodeToken` — test malformed JWT, empty string, expired token — none must throw
- [x] Integration: `ng build --configuration production` exits with code 0; `dist/` folder exists
- [x] Integration: grep `dist/main*.js` for `http://` strings → zero matches (AC-005)

---

## Implementation Checklist

- [x] Scaffold Angular 17 project with `ng new clinical-hub --routing --style scss --standalone`; confirm `package.json` Angular version is `^17.x` (AC-001)
- [x] Configure `environment.ts` (dev `http://localhost:5000`) and `environment.production.ts` (prod `https://`; no `http://`) (AC-001, AC-005)
- [x] Implement `AuthService` with no-throw `decodeToken()`, `isTokenExpired()`, `getCurrentRole()`, and `getToken()` reading from `localStorage` (AC-002, AC-003)
- [x] Implement `AuthGuard` (`CanActivateFn`): absent token → `/login?reason=unauthorized`; expired token → `/login?reason=timeout`; valid token → `true` (AC-002)
- [x] Implement `RoleGuard` (`CanActivateFn`): cross-role access → `console.warn` violation log + `/login?reason=unauthorized`; matching role → `true` (AC-003)
- [x] Configure `app.routes.ts` with public (`/login`, `/register`), role-protected (`/patient/**`, `/staff/**`, `/admin/**`) routes and wildcard `**` redirect to role-appropriate dashboard (AC-002, AC-003)
- [x] Implement `ShellComponent` with `navItems` array driven by `AuthService.getCurrentRole()`; bind with `@if` (not `[hidden]`) to exclude cross-role links from the DOM (AC-004)
- [x] Create `netlify.toml` with `[[redirects]] from = "/*" to = "/index.html" status = 200`; run `ng build --configuration production` and confirm zero errors in `dist/` (AC-001, AC-006)
