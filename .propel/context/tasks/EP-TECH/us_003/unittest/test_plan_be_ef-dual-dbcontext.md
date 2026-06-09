# Unit Test Plan - TASK_003

## Requirement Reference

- **User Story**: US_003 — Dual-database EF Core setup SQL Server and PostgreSQL
- **Story Location**: `.propel/context/tasks/EP-TECH/us_003/us_003.md`
- **Layer**: BE
- **Related Test Plans**: `../us_002/unittest/test_plan_be_webapi-scaffold.md`
- **Acceptance Criteria Covered**:
  - AC-001: `ApplicationDbContext` targets SQL Server; resolved from DI
  - AC-002: `ClinicalDbContext` targets PostgreSQL (Npgsql); resolved from DI
  - AC-003: Two contexts cannot share a `TransactionScope`
  - AC-004: Connection strings sourced exclusively from environment variables; absent = startup failure
  - AC-005: Migration scaffolding requires explicit `--context` flag; separate projects per context

---

## Test Plan Overview

Validates dual EF Core DbContext registration — provider isolation, environment-variable
connection string enforcement, startup fail-fast on missing configuration, cross-context
transaction prevention, and design-time factory correctness. Tests use an in-process
`WebApplicationFactory` for DI resolution tests and standalone xUnit fixtures for unit
tests on DbContext factories. No real database connections are made; EF Core provider
registration and connection string routing are asserted at the DI/configuration layer.

---

## Dependent Tasks

- TASK_002 (US_002) — Web API scaffold must exist (`Program.cs` DI container)

---

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `ApplicationDbContext` | DbContext class | `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs` | SQL Server EF Core context |
| `ClinicalDbContext` | DbContext class | `src/ClinicalHealthcare.Infrastructure/Data/ClinicalDbContext.cs` | PostgreSQL EF Core context |
| `ApplicationDbContextFactory` | design-time factory | `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContextFactory.cs` | Design-time `dotnet ef` context creation |
| `ClinicalDbContextFactory` | design-time factory | `src/ClinicalHealthcare.Infrastructure/Data/ClinicalDbContextFactory.cs` | Design-time `dotnet ef` context creation |
| `Program` (DI wiring) | startup class | `src/ClinicalHealthcare.Api/Program.cs` | Registers both contexts; reads env vars |

---

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 [SOURCE:INPUT] | positive | `ApplicationDbContext` resolves from DI with SQL Server provider | `SQLSERVER_CONNECTION_STRING` env var set; host started | `IServiceProvider.GetRequiredService<ApplicationDbContext>()` called | Context resolved; database provider is SQL Server | `context != null`; `context.Database.ProviderName == "Microsoft.EntityFrameworkCore.SqlServer"` |
| TC-002 [SOURCE:INPUT] | positive | `ClinicalDbContext` resolves from DI with Npgsql provider | `POSTGRES_CONNECTION_STRING` env var set; host started | `IServiceProvider.GetRequiredService<ClinicalDbContext>()` called | Context resolved; database provider is Npgsql | `context != null`; `context.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL"` |
| TC-003 [SOURCE:INPUT] | positive | Missing `SQLSERVER_CONNECTION_STRING` throws at host build | Env var `SQLSERVER_CONNECTION_STRING` is not set | `WebApplicationFactory.CreateClient()` invoked | `InvalidOperationException` thrown at startup containing the env var name | Exception type `InvalidOperationException`; message contains `"SQLSERVER_CONNECTION_STRING"` |
| TC-004 [SOURCE:INPUT] | positive | Missing `POSTGRES_CONNECTION_STRING` throws at host build | Env var `POSTGRES_CONNECTION_STRING` is not set | `WebApplicationFactory.CreateClient()` invoked | `InvalidOperationException` thrown at startup containing the env var name | Exception type `InvalidOperationException`; message contains `"POSTGRES_CONNECTION_STRING"` |
| EC-001 [SOURCE:INPUT] | edge_case | Enlisting both contexts in same `TransactionScope` throws | Both contexts resolved from DI | `new TransactionScope()` opened; `ApplicationDbContext` and `ClinicalDbContext` both queried inside scope | `InvalidOperationException` (or provider-level exception) is thrown | Exception thrown before or at second context query; no silent success |
| EC-002 [SOURCE:INFERRED] | edge_case | Empty-string connection string treated same as null | Env var `SQLSERVER_CONNECTION_STRING=""` set | Host build attempted | Startup exception thrown with descriptive message | Exception thrown; message distinguishes null vs empty to aid diagnosis | Basis: empty string is not a valid connection string; fail-fast rule (AC-004) applies equally |
| EC-003 [SOURCE:INFERRED] | edge_case | `ApplicationDbContextFactory` creates context without `IServiceProvider` | No DI host running | `ApplicationDbContextFactory.CreateDbContext(new string[0])` called directly | `ApplicationDbContext` instance returned | Context is non-null; provider is SQL Server | Basis: design-time factory must work in isolation for `dotnet ef migrations add` |
| ES-001 [SOURCE:INFERRED] | error | `ClinicalDbContextFactory` creates context without `IServiceProvider` | No DI host running | `ClinicalDbContextFactory.CreateDbContext(new string[0])` called directly | `ClinicalDbContext` instance returned | Context is non-null; provider is Npgsql | Basis: symmetric coverage for both design-time factories |

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `tests/ClinicalHealthcare.Infrastructure.Tests/ClinicalHealthcare.Infrastructure.Tests.csproj` | xUnit test project for infrastructure layer |
| CREATE | `tests/ClinicalHealthcare.Infrastructure.Tests/Data/ApplicationDbContextTests.cs` | TC-001, TC-003, EC-002, EC-003 |
| CREATE | `tests/ClinicalHealthcare.Infrastructure.Tests/Data/ClinicalDbContextTests.cs` | TC-002, TC-004, ES-001 |
| CREATE | `tests/ClinicalHealthcare.Infrastructure.Tests/Data/CrossContextTransactionTests.cs` | EC-001 |
| CREATE | `tests/ClinicalHealthcare.Infrastructure.Tests/Helpers/TestHostFactory.cs` | Shared in-process host helper with env-var override |

---

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| SQL Server database | none (EF Core provider check only) | Test does NOT call `SaveChanges`; only checks DI resolution and `ProviderName` | N/A |
| PostgreSQL database | none (EF Core provider check only) | Same as above — no actual Npgsql connection established | N/A |
| Environment variables | `Environment.SetEnvironmentVariable` / `WithWebHostBuilder` | Per-test env var injection | Test-specific connection string stubs (e.g. `"Server=stub;Database=stub"`) |
| `IServiceProvider` | `WebApplicationFactory` | In-process host scope | Real DI container scoped per test |

---

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Valid SQL Server env | `SQLSERVER_CONNECTION_STRING=Server=stub;Database=stub` | `ApplicationDbContext` resolved; provider = SqlServer |
| Valid PostgreSQL env | `POSTGRES_CONNECTION_STRING=Host=stub;Database=stub` | `ClinicalDbContext` resolved; provider = Npgsql |
| Missing SQL conn string | `SQLSERVER_CONNECTION_STRING` not set | `InvalidOperationException` at host build |
| Missing PG conn string | `POSTGRES_CONNECTION_STRING` not set | `InvalidOperationException` at host build |
| Empty SQL conn string | `SQLSERVER_CONNECTION_STRING=""` | `InvalidOperationException` at host build |

---

## Test Commands

- **Run Tests**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests/ --no-build`
- **Run with Coverage**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests/ --collect:"XPlat Code Coverage" --results-directory ./coverage`
- **Run Single Test**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests/ --filter "FullyQualifiedName~ApplicationDbContextTests"`

---

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**: Connection string null/empty guard in `Program.cs`; both `DesignTimeDbContextFactory` `CreateDbContext` methods; cross-context transaction guard

---

## Documentation References

- **Framework Docs**: <https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/>
- **EF Core Testing**: <https://learn.microsoft.com/en-us/ef/core/testing/>
- **Npgsql EF Core**: <https://www.npgsql.org/efcore/>

---

## Implementation Checklist

- [x] Create `ClinicalHealthcare.Infrastructure.Tests` xUnit project; reference infrastructure + API projects
- [x] Implement TC-001/TC-002 — DI resolution + provider name assertions for both contexts
- [x] Implement TC-003/TC-004/EC-002 — startup fail-fast on missing/empty connection strings
- [x] Implement EC-001 — cross-context `TransactionScope` exception
- [x] Implement EC-003/ES-001 — design-time factory isolation (no host required)
- [x] Run test suite; validate all 8 test cases pass
- [x] Verify line coverage ≥ 90% on `ApplicationDbContext.cs`, `ClinicalDbContext.cs`, and factories
