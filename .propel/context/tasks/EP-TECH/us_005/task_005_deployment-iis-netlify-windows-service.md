# Task - TASK_005

## Requirement Reference

- **User Story**: US_005 — Deployment: IIS, Netlify, Windows Services
- **Story Location**: `.propel/context/tasks/EP-TECH/us_005/us_005.md`
- **Parent Epic**: EP-TECH

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `netlify.toml` contains SPA redirect rule (`/*` → `/index.html`, 200) |
| AC-002 | `web.config` configures ASP.NET Core in-process hosting on IIS |
| AC-003 | `UseWindowsService()` is called in `Program.cs` to support Windows Service hosting |
| AC-004 | TLS IIS binding documented in deployment runbook; HTTP → HTTPS 301 redirect configured |
| AC-005 | No secrets (connection strings, API keys) are committed to any config file in the repository |

### Edge Cases

- `netlify.toml` missing `[[redirects]]` rule → Angular deep links return 404; must be present before first Netlify deploy
- `web.config` must not contain hardcoded `ASPNETCORE_ENVIRONMENT`; environment is set via IIS application pool environment variables

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
| Infrastructure | IIS (Windows) | Built-in | ASP.NET Core in-process hosting per design.md |
| Infrastructure | ASP.NET Core Windows Service | 8.x | `Microsoft.Extensions.Hosting.WindowsServices` per design.md |
| Infrastructure | Netlify | N/A | Angular SPA CDN deployment per design.md |
| Infrastructure | GitHub Actions | N/A | CI/CD pipeline per design.md |

---

## Task Overview

Create deployment artifacts — `netlify.toml` for Angular SPA, `web.config` for IIS in-process hosting, and `UseWindowsService()` in `Program.cs`. Document TLS binding configuration in a deployment runbook. Ensure no secrets are present in any committed configuration file.

---

## Dependent Tasks

- **TASK_001 (us_001)** — Angular SPA scaffold (provides the Angular dist output that `netlify.toml` serves)
- **TASK_001 (us_002)** — .NET 8 Web API scaffold (`Program.cs` already exists)

---

## Impacted Components

- `src/clinical-healthcare-app/netlify.toml` — Angular SPA Netlify config
- `src/ClinicalHealthcare.Api/web.config` — IIS in-process hosting config
- `src/ClinicalHealthcare.Api/Program.cs` — `UseWindowsService()`
- `docs/deployment-runbook.md` — TLS + IIS binding documentation

---

## Implementation Plan

1. Add `[[redirects]]` rule to `netlify.toml`: `from = "/*"`, `to = "/index.html"`, `status = 200`.
2. Create `web.config` with `<aspNetCore processPath="dotnet" ... hostingModel="inprocess">`.
3. Add `UseWindowsService()` call in `Program.cs` (before `builder.Build()`).
4. Add `Microsoft.Extensions.Hosting.WindowsServices` NuGet to the API project.
5. Add HTTP → HTTPS redirect middleware (`UseHttpsRedirection()`) in `Program.cs`.
6. Audit all `appsettings*.json` files — move any literal secrets to environment variable references (e.g., `${ENV_VAR}`); add `.gitignore` rule for any local override files.
7. Create `docs/deployment-runbook.md` with IIS TLS binding and Windows Service install steps.
8. Verify `web.config` is included in publish output (set `CopyToPublishDirectory = Always`).

---

## Current Project State

```
src/
├── clinical-healthcare-app/
│   └── netlify.toml  (from us_001)
└── ClinicalHealthcare.Api/
    └── Program.cs
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | `src/clinical-healthcare-app/netlify.toml` | Add `[[redirects]]` SPA fallback rule |
| CREATE | `src/ClinicalHealthcare.Api/web.config` | IIS in-process hosting config |
| MODIFY | `src/ClinicalHealthcare.Api/Program.cs` | Add `UseWindowsService()` and `UseHttpsRedirection()` |
| CREATE | `docs/deployment-runbook.md` | TLS binding + Windows Service install documentation |
| MODIFY | `src/ClinicalHealthcare.Api/appsettings.json` | Remove any literal secrets; use env var placeholders |
| MODIFY | `.gitignore` | Ensure `appsettings.*.json` overrides not tracked |

---

## External References

- [ASP.NET Core IIS In-Process Hosting](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/)
- [ASP.NET Core Windows Service](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/windows-service)
- [Netlify Redirects](https://docs.netlify.com/routing/redirects/)

---

## Build Commands

```bash
dotnet add src/ClinicalHealthcare.Api package Microsoft.Extensions.Hosting.WindowsServices
dotnet publish src/ClinicalHealthcare.Api -c Release -o publish/api
# Verify web.config present in publish/api/
```

---

## Implementation Validation Strategy

- `dotnet publish` output contains `web.config`.
- `netlify.toml` contains `from = "/*"` redirect rule.
- Grep all `appsettings*.json` for connection strings / passwords → 0 results.
- `UseWindowsService()` present in `Program.cs`.
- `UseHttpsRedirection()` present in middleware pipeline.
- `docs/deployment-runbook.md` documents IIS TLS binding steps.

---

## Implementation Checklist

- [x] **[AC-001]** `netlify.toml` `[[redirects]]` SPA fallback rule present
- [x] **[AC-002]** `web.config` generated with `hostingModel="inprocess"` for IIS
- [x] **[AC-003]** `UseWindowsService()` called in `Program.cs`
- [x] **[AC-004]** `UseHttpsRedirection()` in middleware; IIS TLS steps documented in runbook
- [x] **[AC-005]** No literal secrets in any committed config file; `.gitignore` covers local overrides
- [x] `dotnet publish` output contains `web.config`
- [x] `docs/deployment-runbook.md` created with IIS + Windows Service steps
- [x] `dotnet build` passes with 0 errors
