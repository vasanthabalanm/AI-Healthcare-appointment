# Task - TASK_018

## Requirement Reference

- **User Story**: US_018 — HIPAA TLS + PHI encryption in transit
- **Story Location**: `.propel/context/tasks/EP-001/us_018/us_018.md`
- **Parent Epic**: EP-001

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | HTTP requests are redirected to HTTPS with 301 |
| AC-002 | TLS 1.2 minimum enforced; TLS 1.0 and 1.1 disabled |
| AC-003 | HSTS header `max-age=31536000` added to all HTTPS responses |
| AC-004 | No secrets (API keys, passwords, connection strings) committed to any config file |
| AC-005 | IIS cipher suite policy documented; weak ciphers disabled in deployment runbook |

### Edge Cases

- HTTP→HTTPS redirect must be permanent (301), not temporary (302)
- HSTS header must not be set on HTTP responses (only HTTPS); `excludedHosts` should include `localhost`

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
| Infrastructure | ASP.NET Core HTTPS | 8.x | `UseHttpsRedirection`, `UseHsts` per design.md |
| Infrastructure | IIS | Windows built-in | TLS cipher suite policy |
| Backend | ASP.NET Core Web API | 8 LTS | Middleware pipeline |

---

## Task Overview

Configure `UseHttpsRedirection()` (301 permanent), `UseHsts()` with `max-age=31536000`, and TLS 1.2 minimum in `Program.cs` / Kestrel settings. Document IIS cipher suite hardening in the deployment runbook. Audit all config files for committed secrets.

---

## Dependent Tasks

- **TASK_001 (us_005)** — `Program.cs` and IIS deployment config exist

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Program.cs` — HTTPS redirect + HSTS middleware
- `src/ClinicalHealthcare.Api/appsettings.json` — Kestrel TLS min version
- `docs/deployment-runbook.md` — cipher suite policy section

---

## Implementation Plan

1. Add `UseHttpsRedirection()` to the middleware pipeline (permanent 301 via `HttpsRedirectionOptions.RedirectStatusCode = StatusCodes.Status301MovedPermanently`).
2. Add `UseHsts()` with `HstsOptions { MaxAge = TimeSpan.FromDays(365), IncludeSubDomains = true, Preload = false }`; exclude `localhost` in development.
3. Configure Kestrel TLS minimum version in `appsettings.json` or `Program.cs`: `SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13`.
4. Audit all `appsettings*.json`, `.env*`, config files — grep for connection strings, passwords, API keys; remove any found; document env var names only.
5. Add `.gitignore` entries for `*.env`, `appsettings.Development.json`, `secrets.json`.
6. Update `docs/deployment-runbook.md`: add IIS cipher suite section disabling RC4, 3DES, DES, export ciphers; document TLS 1.2+ enforcement via IIS Crypto or registry.

---

## Current Project State

```
src/ClinicalHealthcare.Api/Program.cs  (existing)
docs/deployment-runbook.md  (existing)
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | `src/ClinicalHealthcare.Api/Program.cs` | Add `UseHttpsRedirection` (301) + `UseHsts` (max-age=1yr) |
| MODIFY | `src/ClinicalHealthcare.Api/appsettings.json` | Configure Kestrel TLS 1.2 minimum |
| MODIFY | `docs/deployment-runbook.md` | Add IIS cipher suite hardening section |
| MODIFY | `.gitignore` | Add entries for local secret/override files |

---

## External References

- [ASP.NET Core HTTPS Enforcement](https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl)
- [HSTS](https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Strict-Transport-Security)
- [IIS Crypto Tool](https://www.nartac.com/Products/IISCrypto)

---

## Build Commands

```bash
dotnet build
# Grep audit:
Select-String -Path "src/**/*.json" -Pattern "password|connectionstring|apikey" -CaseSensitive:$false
```

---

## Implementation Validation Strategy

- HTTP request → 301 redirect to HTTPS.
- HTTPS response includes `Strict-Transport-Security: max-age=31536000; includeSubDomains`.
- Kestrel logs show TLS 1.2+ negotiation; TLS 1.0 handshake refused.
- Grep all config files for secrets → 0 results.
- `docs/deployment-runbook.md` contains cipher suite section.

---

## Implementation Checklist

- [x] **[AC-001]** `UseHttpsRedirection()` configured for 301 permanent redirect
- [x] **[AC-002]** Kestrel `SslProtocols` set to TLS 1.2 minimum (TLS 1.0/1.1 excluded)
- [x] **[AC-003]** `UseHsts()` with `max-age=31536000; includeSubDomains`; excluded from localhost
- [x] **[AC-004]** All config files audited; no committed secrets; `.gitignore` updated
- [x] **[AC-005]** IIS cipher suite hardening documented in deployment runbook
- [x] HTTPS redirect status code explicitly set to 301
- [x] HSTS header not served on HTTP responses
- [x] `dotnet build` passes with 0 errors
