# Unit Test Plan - TASK_005

## Requirement Reference

- **User Story**: US_005 — Deployment configuration for IIS, Netlify, and Windows Services
- **Story Location**: `.propel/context/tasks/EP-TECH/us_005/us_005.md`
- **Layer**: Infra
- **Related Test Plans**: `../us_002/unittest/test_plan_be_webapi-scaffold.md`
- **Acceptance Criteria Covered**:
  - AC-001: `netlify.toml` contains SPA fallback redirect (`/*` → `/index.html`, 200)
  - AC-002: `web.config` configures ASP.NET Core in-process hosting (`hostingModel="inprocess"`)
  - AC-003: `UseWindowsService()` present in `Program.cs` before `builder.Build()`
  - AC-004: HTTP → HTTPS redirect middleware (`UseHttpsRedirection()`) present in pipeline
  - AC-005: No secrets (connection strings, API keys, passwords) in any committed config file

---

## Test Plan Overview

Validates deployment configuration artefacts through static file inspection. No runtime
process is started — tests read `netlify.toml`, `web.config`, `Program.cs`, `appsettings*.json`,
and `.gitignore` from the repository checkout and assert their content correctness. This
approach guarantees the artefacts remain valid regardless of environment and provides a fast
CI safety net before any deployment occurs.

---

## Dependent Tasks

- TASK_001 (US_001) — Angular SPA scaffold provides the `netlify.toml` file
- TASK_002 (US_002) — .NET 8 Web API scaffold provides `Program.cs` and `appsettings.json`

---

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `netlify.toml` | config file | `clinical-hub/netlify.toml` | SPA redirect rule for Netlify CDN deployment |
| `web.config` | config file | `src/ClinicalHealthcare.Api/web.config` | IIS in-process hosting configuration |
| `Program.cs` | startup class | `src/ClinicalHealthcare.Api/Program.cs` | `UseWindowsService()` + `UseHttpsRedirection()` in pipeline |
| `appsettings.json` | config file | `src/ClinicalHealthcare.Api/appsettings.json` | No secrets; env var placeholders only |
| `appsettings.Production.json` | config file | `src/ClinicalHealthcare.Api/appsettings.Production.json` | Production overrides; no literal secrets |
| `.gitignore` | VCS config | `.gitignore` | Excludes local env override files from git tracking |

---

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 [SOURCE:INPUT] | positive | `netlify.toml` contains SPA fallback redirect rule | `netlify.toml` is read from repository | File content is inspected | `[[redirects]]` block with `from = "/*"`, `to = "/index.html"`, `status = 200` is present | `File.ReadAllText` contains `"[[redirects]]"`; contains `"/*"`; contains `"/index.html"`; contains `"200"` |
| TC-002 [SOURCE:INPUT] | positive | `web.config` specifies in-process IIS hosting | `web.config` is read from API project output | XML content is parsed | `hostingModel="inprocess"` and `processPath="dotnet"` attributes present on `<aspNetCore>` element | `XDocument.Parse` finds `aspNetCore` element; `hostingModel` attribute equals `"inprocess"`; `processPath` attribute equals `"dotnet"` |
| TC-003 [SOURCE:INPUT] | positive | `Program.cs` calls `UseWindowsService()` before `builder.Build()` | `Program.cs` is read as text | File content is scanned | `UseWindowsService()` call is present | `File.ReadAllText` contains `"UseWindowsService()"` |
| TC-004 [SOURCE:INPUT] | positive | `appsettings.json` contains no connection string literals | `appsettings.json` is read | Content is inspected for secret patterns | No `ConnectionString`, `Password`, `ApiKey`, `Secret`, `Token`, or `PrivateKey` literals in plain-text values | Regex `(?i)(password|secret|apikey|token|privatekey)\s*":\s*"[^${\s][^"]{3,}"` finds zero matches |
| TC-005 [SOURCE:INPUT] | positive | `UseHttpsRedirection()` is present in the middleware pipeline | `Program.cs` is read as text | File content is scanned | `UseHttpsRedirection()` call is present | `File.ReadAllText` contains `"UseHttpsRedirection()"` |
| EC-001 [SOURCE:INFERRED] | edge_case | `appsettings.Production.json` also free of literal secrets | `appsettings.Production.json` is read (if file exists) | Content is inspected with same secret-pattern regex | Zero regex matches | Same regex as TC-004 applied to production config; 0 matches; if file absent test is skipped with explicit assertion | Basis: AC-005 says "all `appsettings*.json`" must be free of secrets |
| ES-001 [SOURCE:INFERRED] | error | `.gitignore` excludes local environment override files | `.gitignore` is read | Content is inspected for override-file patterns | `.env`, `appsettings.*.local.json` patterns present | `File.ReadAllText` contains `".env"`; contains `"appsettings.*.local"` or equivalent pattern | Basis: AC-005 states `.gitignore` must exclude any `.env` file to prevent accidental secret commit |
| ES-002 [SOURCE:INFERRED] | error | `web.config` does not hard-code `ASPNETCORE_ENVIRONMENT` | `web.config` is parsed | `environmentVariables` child elements inspected | No `ASPNETCORE_ENVIRONMENT` element found | XDocument contains no `<environmentVariable>` with `name="ASPNETCORE_ENVIRONMENT"` | Basis: TASK_005 edge-case states environment must be set via IIS app-pool variables, not `web.config` |

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `tests/ClinicalHealthcare.DeploymentTests/ClinicalHealthcare.DeploymentTests.csproj` | xUnit project for static deployment-artefact inspection |
| CREATE | `tests/ClinicalHealthcare.DeploymentTests/NetlifyConfigTests.cs` | TC-001 — `netlify.toml` redirect rule |
| CREATE | `tests/ClinicalHealthcare.DeploymentTests/IisConfigTests.cs` | TC-002, ES-002 — `web.config` XML assertions |
| CREATE | `tests/ClinicalHealthcare.DeploymentTests/ProgramSourceTests.cs` | TC-003, TC-005 — `Program.cs` middleware calls |
| CREATE | `tests/ClinicalHealthcare.DeploymentTests/SecretAuditTests.cs` | TC-004, EC-001, ES-001 — secret-pattern and `.gitignore` checks |
| CREATE | `tests/ClinicalHealthcare.DeploymentTests/Helpers/RepoPathHelper.cs` | Resolves repository root relative to test binary output |

---

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| File system | none (real) | Tests read actual repository files relative to solution root | Real file content |
| XML parsing | `System.Xml.Linq.XDocument` (real) | Parses `web.config` | Parsed XML tree |
| Regex secret scanner | `System.Text.RegularExpressions.Regex` (real) | Pattern match on config file content | Match collection |
| Repo root path | `RepoPathHelper.GetSolutionRoot()` | Walks up from test binary to locate `.sln` file | Absolute path string |

---

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Valid `netlify.toml` | File with `[[redirects]]` block | Assertions pass |
| Valid `web.config` | XML with `hostingModel="inprocess"` | Assertions pass |
| Secret present in `appsettings.json` | `"Password": "hunter2"` | Regex returns ≥1 match → test FAILS |
| Secret absent | `"Password": "${DB_PASSWORD}"` | Regex returns 0 matches → test PASSES |
| `web.config` hardcodes env | `<environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />` | Assertion FAILS |

---

## Test Commands

- **Run Tests**: `dotnet test tests/ClinicalHealthcare.DeploymentTests/ --no-build`
- **Run with Coverage**: `dotnet test tests/ClinicalHealthcare.DeploymentTests/ --collect:"XPlat Code Coverage"`
- **Run Single Test**: `dotnet test tests/ClinicalHealthcare.DeploymentTests/ --filter "FullyQualifiedName~SecretAuditTests"`

---

## Coverage Target

- **Line Coverage**: 95%
- **Branch Coverage**: 85%
- **Critical Paths**: Secret-pattern regex (TC-004/EC-001); `web.config` XML parse (TC-002); `.gitignore` pattern check (ES-001)

---

## Documentation References

- **Netlify Redirects**: <https://docs.netlify.com/routing/redirects/>
- **ASP.NET Core IIS In-Process Hosting**: <https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/>
- **ASP.NET Core Windows Service**: <https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/windows-service>

---

## Implementation Checklist

- [x] Create `ClinicalHealthcare.DeploymentTests` xUnit project with `RepoPathHelper`
- [x] Implement TC-001 — `netlify.toml` SPA redirect rule content assertions
- [x] Implement TC-002/ES-002 — `web.config` XML: `hostingModel`, `processPath`, no hardcoded env
- [x] Implement TC-003/TC-005 — `Program.cs` source contains `UseWindowsService()` + `UseHttpsRedirection()`
- [x] Implement TC-004/EC-001 — secret-pattern regex on `appsettings.json` + `appsettings.Production.json`
- [x] Implement ES-001 — `.gitignore` excludes `.env` and local override patterns
- [x] Run test suite; validate all 7 test cases pass
