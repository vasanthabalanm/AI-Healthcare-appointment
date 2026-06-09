# Task - TASK_002

## Requirement Reference

- **User Story**: US_002 — .NET 8 Web API vertical-slice scaffold + Swagger
- **Story Location**: `.propel/context/tasks/EP-TECH/us_002/us_002.md`
- **Parent Epic**: EP-TECH

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `GET /health` returns HTTP 200 `{"status":"Healthy"}` |
| AC-002 | Swagger UI is available at `/swagger` in Development only |
| AC-003 | DI container validates all registrations at startup (no runtime resolution errors) |
| AC-004 | `Features/` directory structure contains vertical-slice stubs (one folder per future feature) |
| AC-005 | All Feature endpoint classes derive from a common base; no top-level MVC controllers exist |

### Edge Cases

- Startup fails if any DI registration is unresolvable → `ValidateOnBuild = true`
- Swagger must be disabled in Production (environment guard)

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
| Backend | .NET / ASP.NET Core Web API | 8 LTS | Target runtime per design.md |
| Backend | Swashbuckle.AspNetCore | 6.x | OpenAPI/Swagger docs per design.md |
| Backend | Microsoft.Extensions.DependencyInjection | 8.x (built-in) | DI container with `ValidateOnBuild` |
| Backend | ASP.NET Core Health Checks | 8.x (built-in) | `/health` endpoint |

---

## Task Overview

Scaffold a greenfield .NET 8 ASP.NET Core Web API project using vertical-slice architecture. The project must expose a `/health` endpoint, Swagger at `/swagger` (Development only), pass DI startup validation, and contain empty feature stub folders under `Features/` for all planned epics.

---

## Dependent Tasks

- **TASK_001 (us_001)** — Angular SPA scaffold (provides `netlify.toml`; API CORS origin is derived from Netlify deploy URL)

---

## Impacted Components

- `ClinicalHealthcare.Api/` — new ASP.NET Core Web API project
- `ClinicalHealthcare.Api/Program.cs` — service registration + middleware pipeline
- `ClinicalHealthcare.Api/Features/` — vertical-slice stub directories
- `ClinicalHealthcare.Api/ClinicalHealthcare.Api.csproj` — project file

---

## Implementation Plan

1. Create the solution and Web API project using `dotnet new webapi --name ClinicalHealthcare.Api`.
2. Delete the default `WeatherForecast` controller/model; configure minimal API or feature endpoint base class.
3. Add `AddHealthChecks()` + `MapHealthChecks("/health")` in `Program.cs`.
4. Add Swashbuckle; gate Swagger registration/UI behind `app.Environment.IsDevelopment()`.
5. Enable `ValidateOnBuild = true` on the service provider options.
6. Create `Features/` subdirectories: `Auth/`, `Appointments/`, `Intake/`, `Staff/`, `ClinicalDocs/`, `Coding/`, `Admin/`, `Patients/` — each with a `README.md` placeholder.
7. Add `IEndpointDefinition` interface + `EndpointDefinitionExtensions` to auto-register all feature endpoints.
8. Verify `dotnet build` succeeds and `/health` returns 200 in local run.

---

## Current Project State

```
d:\BRD-Healthcare\Clinical-Healthcare\
└── BRD.md
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Api/ClinicalHealthcare.Api.csproj` | Web API project file targeting net8.0 |
| CREATE | `src/ClinicalHealthcare.Api/Program.cs` | Startup: health checks, Swagger (dev-only), DI validate, endpoint scan |
| CREATE | `src/ClinicalHealthcare.Api/Abstractions/IEndpointDefinition.cs` | Interface for vertical-slice endpoint registration |
| CREATE | `src/ClinicalHealthcare.Api/Abstractions/EndpointDefinitionExtensions.cs` | Assembly scan + MapEndpoints helper |
| CREATE | `src/ClinicalHealthcare.Api/Features/Auth/README.md` | Stub placeholder |
| CREATE | `src/ClinicalHealthcare.Api/Features/Appointments/README.md` | Stub placeholder |
| CREATE | `src/ClinicalHealthcare.Api/Features/Intake/README.md` | Stub placeholder |
| CREATE | `src/ClinicalHealthcare.Api/Features/Staff/README.md` | Stub placeholder |
| CREATE | `src/ClinicalHealthcare.Api/Features/ClinicalDocs/README.md` | Stub placeholder |
| CREATE | `src/ClinicalHealthcare.Api/Features/Coding/README.md` | Stub placeholder |
| CREATE | `src/ClinicalHealthcare.Api/Features/Admin/README.md` | Stub placeholder |
| CREATE | `src/ClinicalHealthcare.Api/Features/Patients/README.md` | Stub placeholder |
| CREATE | `ClinicalHealthcare.sln` | Solution file referencing the API project |

---

## External References

- [ASP.NET Core Health Checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks)
- [Swashbuckle.AspNetCore](https://github.com/domaindrivendev/Swashbuckle.AspNetCore)
- [Vertical Slice Architecture](https://www.jimmybogard.com/vertical-slice-architecture/)

---

## Build Commands

```bash
dotnet new sln -n ClinicalHealthcare
dotnet new webapi -n ClinicalHealthcare.Api -o src/ClinicalHealthcare.Api
dotnet sln add src/ClinicalHealthcare.Api/ClinicalHealthcare.Api.csproj
dotnet build
dotnet run --project src/ClinicalHealthcare.Api
```

---

## Implementation Validation Strategy

- Run `dotnet build` → 0 errors, 0 warnings.
- Run `dotnet run` and `curl http://localhost:5000/health` → `{"status":"Healthy"}`.
- Open `http://localhost:5000/swagger` in Development → Swagger UI loads.
- Set `ASPNETCORE_ENVIRONMENT=Production` and confirm `/swagger` returns 404.
- Add a deliberately unregistered service and confirm startup throws on `ValidateOnBuild`.
- All 8 `Features/` subdirectories present.

---

## Implementation Checklist

- [x] **[AC-001]** `MapHealthChecks("/health")` registered; returns 200 `{"status":"Healthy"}`
- [x] **[AC-002]** Swagger registered and served only when `IsDevelopment()` is true
- [x] **[AC-003]** `ValidateOnBuild = true` configured; unresolvable services fail startup
- [x] **[AC-004]** `Features/` directories created for all 8 planned feature areas
- [x] **[AC-005]** `IEndpointDefinition` interface + assembly-scan extension replace top-level MVC controllers
- [x] Build passes with `dotnet build` (0 errors)
- [x] `/health` curl smoke-test passes locally
- [x] Solution file created and references the API project
