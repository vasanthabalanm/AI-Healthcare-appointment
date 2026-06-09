---
task: TASK_005
us: us_005
epic: EP-TECH
reviewed: 2026-05-13
reviewer: GitHub Copilot (Claude Sonnet 4.6)
verdict: Pass
---

# Implementation Analysis — TASK_005: Deployment — IIS, Netlify, Windows Services

## Verdict

**Status:** Pass
**Summary:** All five acceptance criteria are fully implemented. `netlify.toml` carries the required SPA fallback redirect. `web.config` uses `hostingModel="inprocess"` and does not hardcode `ASPNETCORE_ENVIRONMENT`. `UseWindowsService()` is registered before the builder builds. `UseHttpsRedirection()` is in the middleware pipeline and the deployment runbook documents IIS TLS binding with a 301 HTTP→HTTPS rewrite rule. No secrets appear in any committed config file and `.gitignore` covers `appsettings.*.local.json` overrides. The build compiles with 0 errors and 0 warnings. No gaps requiring remediation were identified.

---

## Traceability Matrix

| Requirement / Acceptance Criterion | Evidence | Result |
|---|---|---|
| **AC-001** `netlify.toml` `[[redirects]]` SPA fallback (`/*` → `/index.html`, 200) | `clinical-hub/netlify.toml` lines 1–4 — `from = "/*"`, `to = "/index.html"`, `status = 200` | **Pass** |
| **AC-002** `web.config` with `hostingModel="inprocess"` | `src/ClinicalHealthcare.Api/web.config` — `<aspNetCore ... hostingModel="inprocess" />` | **Pass** |
| **AC-002 edge** `ASPNETCORE_ENVIRONMENT` not hardcoded in `web.config` | File contains no `ASPNETCORE_ENVIRONMENT` attribute; comment directs operators to IIS App Pool env vars | **Pass** |
| **AC-003** `UseWindowsService()` in `Program.cs` | `Program.cs` line 17 — `builder.Host.UseWindowsService();` | **Pass** |
| **AC-004** `UseHttpsRedirection()` in middleware pipeline | `Program.cs` line 106 — `app.UseHttpsRedirection();` | **Pass** |
| **AC-004** TLS IIS binding documented in runbook | `docs/deployment-runbook.md` §2 "TLS Binding (AC-004)" — cert import, HTTPS binding, URL Rewrite 301 rule | **Pass** |
| **AC-005** No literal secrets in committed config files | `appsettings.json` — Logging + AllowedHosts only; `web.config` — no secrets; grep for `Password=\|ConnectionString\|ApiKey` → 0 matches | **Pass** |
| **AC-005** `.gitignore` covers local overrides | `.gitignore` line 141 — `appsettings.*.local.json` | **Pass** |
| `web.config` in publish output | `ClinicalHealthcare.Api.csproj` — `<Content Update="web.config" CopyToPublishDirectory="Always" />` | **Pass** |
| `Microsoft.Extensions.Hosting.WindowsServices` NuGet added | `ClinicalHealthcare.Api.csproj` — `<PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" Version="8.*" />` | **Pass** |
| `dotnet build` 0 errors | Terminal: `Build succeeded. 0 Warning(s) 0 Error(s)` | **Pass** |

---

## Logical & Design Findings

- **Business Logic:** `UseWindowsService()` is registered unconditionally before `builder.Build()` as intended — it is a no-op on Linux and when not running as a Windows Service. This is the correct placement; calling it after `Build()` would have no effect.

- **Security:**
  - `web.config` correctly omits `ASPNETCORE_ENVIRONMENT` and all connection strings. Operators are directed to IIS App Pool environment variable configuration. ✅ OWASP A02 compliant.
  - `stdoutLogEnabled="false"` in production `web.config` — prevents sensitive startup output from accumulating in log files on disk. ✅
  - `appsettings.Development.json` is not explicitly in `.gitignore` but contains only `AllowedOrigins: ["http://localhost:4200"]` — no secrets. ✅
  - The HTTP→HTTPS 301 rewrite rule in the runbook uses `{HTTP_HOST}` (preserves hostname) and `{R:1}` (preserves path) — correct. ✅

- **Error Handling:** `stdoutLogFile=".\logs\stdout"` path is relative to the site root. The `logs\` directory must exist before first startup or IIS will not create it. This is a minor operational note — not a code defect — and is acceptable for a runbook-level concern.

- **Data Access:** N/A — infrastructure task only.

- **Frontend:** `netlify.toml` SPA redirect covers all routes including those with query strings and fragments. ✅

- **Performance:** In-process hosting (`hostingModel="inprocess"`) is the correct choice for lowest latency in IIS — avoids out-of-process reverse-proxy overhead. ✅

- **Patterns & Standards:**
  - `web.config` wrapped in `<location path="." inheritInChildApplications="false">` — prevents IIS child application config inheritance. ✅
  - `CopyToPublishDirectory="Always"` ensures `web.config` is never omitted from a `dotnet publish` output regardless of SDK auto-transform behaviour. ✅

---

## Test Review

- **Existing Tests:** None for TASK_005 scope — infrastructure/configuration task. Unit tests are not applicable.

- **Missing Tests (must add):**
  - [ ] Integration / smoke: `dotnet publish` output directory contains `web.config` (can be a CI pipeline assertion step)
  - [ ] Integration / smoke: `GET /health` via HTTPS returns `{"status":"Healthy"}` after IIS deployment
  - [ ] Runbook validation: manual walkthrough of IIS TLS binding steps in staging before production deploy

---

## Validation Results

- **Commands Executed:**
  ```bash
  dotnet build ClinicalHealthcare.slnx
  grep -rn "Password=|ConnectionString|ApiKey" src/ --include="*.json" --include="*.config"
  ```
- **Outcomes:**
  - `Build succeeded. 0 Warning(s) 0 Error(s)` ✅
  - Secrets grep → 0 matches ✅
  - `netlify.toml` redirect rule confirmed ✅
  - `UseWindowsService()` confirmed at `Program.cs:17` ✅
  - `UseHttpsRedirection()` confirmed at `Program.cs:106` ✅
  - `web.config` `CopyToPublishDirectory="Always"` confirmed in `.csproj` ✅

---

## Fix Plan (Prioritized)

No fixes required. All acceptance criteria pass.

> **Operational note (not a code fix):** Ensure the `logs\` directory exists at the IIS site root before first startup, or pre-create it in the deployment script. `web.config` references `.\logs\stdout` for `stdoutLogFile`. Recommend adding to the runbook's publish step:
> ```powershell
> New-Item -ItemType Directory -Force -Path "C:\inetpub\clinicalhealthcare\api\logs"
> ```

---

## Checklist Status

- [x] **[AC-001]** `netlify.toml` `[[redirects]]` SPA fallback rule present
- [x] **[AC-002]** `web.config` with `hostingModel="inprocess"`; no hardcoded `ASPNETCORE_ENVIRONMENT`
- [x] **[AC-003]** `UseWindowsService()` called in `Program.cs`
- [x] **[AC-004]** `UseHttpsRedirection()` in middleware; IIS TLS steps + 301 redirect documented in runbook
- [x] **[AC-005]** No literal secrets in any committed config file; `.gitignore` covers local overrides
- [x] `dotnet publish` output contains `web.config` (`CopyToPublishDirectory="Always"`)
- [x] `docs/deployment-runbook.md` created with IIS, TLS, and Windows Service steps
- [x] `dotnet build` passes with 0 errors

---

## Appendix

- **Search Evidence:**
  - `clinical-hub/netlify.toml` — `[[redirects]]` rule verified
  - `src/ClinicalHealthcare.Api/web.config` — full file reviewed
  - `src/ClinicalHealthcare.Api/Program.cs` lines 1–25, 106 — `UseWindowsService()` and `UseHttpsRedirection()` verified
  - `src/ClinicalHealthcare.Api/ClinicalHealthcare.Api.csproj` — package and publish entries verified
  - `src/ClinicalHealthcare.Api/appsettings.json` — no secrets present
  - `.gitignore` line 141 — `appsettings.*.local.json` confirmed
  - `docs/deployment-runbook.md` §2 TLS Binding — reviewed
