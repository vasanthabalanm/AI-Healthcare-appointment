# Unit Test Plan - TASK_002

## Requirement Reference

- **User Story**: US_002 — .NET 8 Web API vertical-slice scaffold with Swagger
- **Story Location**: `.propel/context/tasks/EP-TECH/us_002/us_002.md`
- **Layer**: BE
- **Related Test Plans**: None
- **Acceptance Criteria Covered**:
  - AC-001: API project compiles and starts without errors
  - AC-002: Swagger UI accessible in Development mode; absent in Production
  - AC-003: Vertical-slice `Features/` stub directories all present
  - AC-004: `GET /health` returns HTTP 200 `{"status":"Healthy"}` within 500 ms
  - AC-005: DI container resolves all registrations at startup without exceptions

---

## Test Plan Overview

Validates the .NET 8 ASP.NET Core Web API scaffold — health-check endpoint behaviour,
Swagger environment gating, vertical-slice folder structure integrity, and DI container
startup validation. All tests run against a `WebApplicationFactory<Program>` integration
host; no live database or Redis connection is required.

---

## Dependent Tasks

- None — scaffold is self-contained; no feature logic required

---

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `Program` | startup class | `src/ClinicalHealthcare.Api/Program.cs` | Service registration, middleware pipeline, environment gating |
| `HealthCheckEndpoint` | minimal-API route | `src/ClinicalHealthcare.Api/Program.cs` | `GET /health` → `{"status":"Healthy"}` |
| `SwaggerMiddleware` | middleware | `src/ClinicalHealthcare.Api/Program.cs` | Swagger UI at `/swagger` (Development only) |
| `Features/` stubs | directory structure | `src/ClinicalHealthcare.Api/Features/` | Vertical-slice placeholder directories |
| `EndpointDefinitionExtensions` | extension class | `src/ClinicalHealthcare.Api/Abstractions/EndpointDefinitionExtensions.cs` | Assembly-scan endpoint registration |

---

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 [SOURCE:INPUT] | positive | `GET /health` returns 200 with Healthy payload | `WebApplicationFactory` host is running | `GET /health` is called without authentication | HTTP 200 response with `{"status":"Healthy"}` JSON body | `response.StatusCode == 200`; body deserialises to `{ status: "Healthy" }`; `Content-Type: application/json` |
| TC-002 [SOURCE:INPUT] | positive | Swagger UI accessible in Development mode | Host configured with `ASPNETCORE_ENVIRONMENT=Development` | `GET /swagger/index.html` is called | HTTP 200; Swagger HTML page returned | `response.StatusCode == 200`; body contains `swagger-ui` |
| TC-003 [SOURCE:INPUT] | positive | DI container builds without unresolved-dependency exception | All services registered in `Program.cs` with `ValidateOnBuild=true` | `WebApplicationFactory.CreateClient()` invoked | Host builds and client is created with no `InvalidOperationException` | No exception thrown; client is not null |
| TC-004 [SOURCE:INPUT] | positive | Features/ contains required vertical-slice stub directories | API project is built | Project directory is inspected at build output path | All 8 stub directories exist | `Directory.Exists` for `Auth`, `Appointments`, `Intake`, `Staff`, `ClinicalDocs`, `Coding`, `Admin`, `Patients` all return `true` |
| TC-005 [SOURCE:INPUT] | negative | Swagger endpoint returns 404 in Production mode | Host configured with `ASPNETCORE_ENVIRONMENT=Production` | `GET /swagger/index.html` is called | HTTP 404; Swagger UI is not served | `response.StatusCode == 404` |
| EC-001 [SOURCE:INFERRED] | edge_case | DI circular dependency is surfaced at build time | A test service with a circular dependency is registered | `WebApplicationFactory.CreateClient()` invoked with circular registration | `InvalidOperationException` containing the affected type name is thrown | Exception thrown; message contains type name; not deferred to first resolve | Basis: `ValidateOnBuild=true` should surface errors eagerly |
| EC-002 [SOURCE:INFERRED] | edge_case | `/health` responds within 500 ms SLA | Host is running | `GET /health` is called and response time is measured | Response completes within 500 ms | `elapsed.TotalMilliseconds < 500` | Basis: AC-004 explicitly states 500 ms requirement |
| ES-001 [SOURCE:INFERRED] | error | All stub directories contain at least one placeholder file | Project directory is inspected post-build | Files inside each `Features/` subdirectory are listed | Each subdirectory has at least one file (e.g. `README.md`) ensuring it is not git-empty | `Directory.GetFiles(dir).Length >= 1` for each stub path | Basis: git does not track empty directories; placeholder file is required to preserve structure |

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `tests/ClinicalHealthcare.Api.Tests/ClinicalHealthcare.Api.Tests.csproj` | xUnit test project targeting net8.0 |
| CREATE | `tests/ClinicalHealthcare.Api.Tests/WebApi/HealthCheckTests.cs` | TC-001, EC-002 — health endpoint assertions |
| CREATE | `tests/ClinicalHealthcare.Api.Tests/WebApi/SwaggerTests.cs` | TC-002, TC-005 — Swagger environment gating |
| CREATE | `tests/ClinicalHealthcare.Api.Tests/Startup/DiContainerTests.cs` | TC-003, EC-001 — DI validation tests |
| CREATE | `tests/ClinicalHealthcare.Api.Tests/Structure/FeatureDirectoryTests.cs` | TC-004, ES-001 — directory/file structure |
| CREATE | `tests/ClinicalHealthcare.Api.Tests/Helpers/TestWebApplicationFactory.cs` | Shared `WebApplicationFactory<Program>` wrapper |

---

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `IWebHost` / `Program` | `WebApplicationFactory<Program>` | Spins up in-process test host | Real ASP.NET Core pipeline |
| Environment (`ASPNETCORE_ENVIRONMENT`) | `WithWebHostBuilder` override | Sets environment variable per test | `"Development"` or `"Production"` |
| File system (structure tests) | none (real) | Reads actual project output directory | Real `Directory.Exists` / `Directory.GetFiles` |

---

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Healthy state | None — health check has no external deps | `{"status":"Healthy"}` |
| Production env | `ASPNETCORE_ENVIRONMENT=Production` header/env override | HTTP 404 on `/swagger` |
| Circular dep | Test-only `CircularServiceA` → `CircularServiceB` → `CircularServiceA` registration | `InvalidOperationException` at host build |

---

## Test Commands

- **Run Tests**: `dotnet test tests/ClinicalHealthcare.Api.Tests/ --no-build`
- **Run with Coverage**: `dotnet test tests/ClinicalHealthcare.Api.Tests/ --collect:"XPlat Code Coverage" --results-directory ./coverage`
- **Run Single Test**: `dotnet test tests/ClinicalHealthcare.Api.Tests/ --filter "FullyQualifiedName~HealthCheckTests"`

---

## Coverage Target

- **Line Coverage**: 85%
- **Branch Coverage**: 80%
- **Critical Paths**: `Program.cs` DI registration block; Swagger environment gate; health-check response shape

---

## Documentation References

- **Framework Docs**: <https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests>
- **Project Test Patterns**: `tests/ClinicalHealthcare.Api.Tests/Helpers/TestWebApplicationFactory.cs`
- **Health Checks**: <https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks>

---

## Implementation Checklist

- [x] Create test project `ClinicalHealthcare.Api.Tests` with xUnit 2.x + WebApplicationFactory
- [x] Implement health-check tests: TC-001 (200 + JSON body), EC-002 (500 ms SLA)
- [x] Implement Swagger gating tests: TC-002 (Development → 200), TC-005 (Production → 404)
- [x] Implement DI validation tests: TC-003 (clean build), EC-001 (circular dep exception)
- [x] Implement structure tests: TC-004 (8 stub dirs), ES-001 (placeholder files present)
- [x] Run test suite; validate all 8 test cases pass
- [x] Verify line coverage ≥ 85% and branch coverage ≥ 80% on `Program.cs`
