# Task - TASK_003

## Requirement Reference

- **User Story**: US_003 — Dual-database EF Core: SQL Server + PostgreSQL
- **Story Location**: `.propel/context/tasks/EP-TECH/us_003/us_003.md`
- **Parent Epic**: EP-TECH

### Acceptance Criteria Addressed

| AC ID | Description |
|-------|-------------|
| AC-001 | `ApplicationDbContext` targets SQL Server; connection string read from env var `SQLSERVER_CONNECTION_STRING` |
| AC-002 | `ClinicalDbContext` targets PostgreSQL via Npgsql; connection string read from env var `POSTGRES_CONNECTION_STRING` |
| AC-003 | No cross-context transactions (each context is an independent unit of work) |
| AC-004 | Both contexts are registered in DI and resolve without errors at startup |
| AC-005 | EF Core migrations are in separate migration assemblies for each context |

### Edge Cases

- Missing env vars → application fails fast at startup with a descriptive error, not a null-reference at query time
- Cross-context query attempted in same transaction → must be architecturally prevented by design (separate DbContexts, no shared transaction scope)

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
| Backend | EF Core | 8.x | ORM per design.md |
| Database | SQL Server | 2022 / Express | Operational DB per design.md |
| Database | PostgreSQL | 16.x | Clinical data DB per design.md |
| Backend | Npgsql.EntityFrameworkCore.PostgreSQL | 8.x | EF Core provider for PostgreSQL per design.md |
| Backend | Microsoft.EntityFrameworkCore.SqlServer | 8.x | EF Core provider for SQL Server |

---

## Task Overview

Register two EF Core DbContexts — `ApplicationDbContext` (SQL Server) and `ClinicalDbContext` (PostgreSQL/Npgsql) — in `Program.cs`. Both connection strings are sourced exclusively from environment variables. Separate migration projects are created for each context. No cross-context transactions are architecturally possible.

---

## Dependent Tasks

- **TASK_001 (us_002)** — .NET 8 Web API scaffold must exist before DbContext registration

---

## Impacted Components

- `src/ClinicalHealthcare.Api/Program.cs` — DbContext DI registration
- `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs` — SQL Server DbContext
- `src/ClinicalHealthcare.Infrastructure/Data/ClinicalDbContext.cs` — PostgreSQL DbContext
- `src/ClinicalHealthcare.Infrastructure.SqlMigrations/` — SQL Server migration project
- `src/ClinicalHealthcare.Infrastructure.PgMigrations/` — PostgreSQL migration project

---

## Implementation Plan

1. Create `ClinicalHealthcare.Infrastructure` class library; add `ApplicationDbContext` and `ClinicalDbContext` classes.
2. `ApplicationDbContext` uses `UseSqlServer()`; `ClinicalDbContext` uses `UseNpgsql()`.
3. Read connection strings from `Environment.GetEnvironmentVariable("SQLSERVER_CONNECTION_STRING")` and `POSTGRES_CONNECTION_STRING`; throw `InvalidOperationException` at startup if either is null/empty.
4. Register both contexts in `Program.cs` via `AddDbContext<T>()`.
5. Create separate migration projects (`SqlMigrations`, `PgMigrations`) each referencing the infrastructure project.
6. Add `DesignTimeDbContextFactory` for each context (needed for `dotnet ef migrations add`).
7. Run `dotnet ef migrations add InitialCreate` for each context in its respective migration project.
8. Validate `dotnet build` succeeds and contexts resolve via `ValidateOnBuild`.

---

## Current Project State

```
src/
└── ClinicalHealthcare.Api/
    └── Program.cs
    └── Features/
```

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `src/ClinicalHealthcare.Infrastructure/ClinicalHealthcare.Infrastructure.csproj` | Infrastructure class library |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs` | SQL Server DbContext |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Data/ClinicalDbContext.cs` | PostgreSQL DbContext |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContextFactory.cs` | Design-time factory for SQL Server migrations |
| CREATE | `src/ClinicalHealthcare.Infrastructure/Data/ClinicalDbContextFactory.cs` | Design-time factory for PostgreSQL migrations |
| CREATE | `src/ClinicalHealthcare.Infrastructure.SqlMigrations/ClinicalHealthcare.Infrastructure.SqlMigrations.csproj` | SQL Server migration project |
| CREATE | `src/ClinicalHealthcare.Infrastructure.PgMigrations/ClinicalHealthcare.Infrastructure.PgMigrations.csproj` | PostgreSQL migration project |
| MODIFY | `src/ClinicalHealthcare.Api/Program.cs` | Register `ApplicationDbContext` + `ClinicalDbContext` in DI |
| MODIFY | `ClinicalHealthcare.sln` | Add infrastructure + migration projects |

---

## External References

- [EF Core Multiple DbContexts](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/)
- [Npgsql EF Core Provider](https://www.npgsql.org/efcore/)
- [EF Core Design-Time Factories](https://learn.microsoft.com/en-us/ef/core/cli/dbcontext-creation)

---

## Build Commands

```bash
dotnet add src/ClinicalHealthcare.Api reference src/ClinicalHealthcare.Infrastructure
dotnet ef migrations add InitialCreate --project src/ClinicalHealthcare.Infrastructure.SqlMigrations --context ApplicationDbContext
dotnet ef migrations add InitialCreate --project src/ClinicalHealthcare.Infrastructure.PgMigrations --context ClinicalDbContext
dotnet build
```

---

## Implementation Validation Strategy

- `dotnet build` → 0 errors.
- Remove `SQLSERVER_CONNECTION_STRING` env var → startup throws `InvalidOperationException`.
- Both contexts appear in DI via `ValidateOnBuild`.
- Migration folders exist and contain `InitialCreate` snapshot.
- No shared `TransactionScope` across contexts anywhere in codebase (grep check).

---

## Implementation Checklist

- [x] **[AC-001]** `ApplicationDbContext` created targeting SQL Server; conn string from env var
- [x] **[AC-002]** `ClinicalDbContext` created targeting PostgreSQL via Npgsql; conn string from env var
- [x] **[AC-003]** No cross-context transactions — separate project boundaries enforce isolation
- [x] **[AC-004]** Both contexts registered in DI; `ValidateOnBuild` confirms resolution
- [x] **[AC-005]** Separate migration projects created for SQL Server and PostgreSQL contexts
- [x] Missing env vars cause fast startup failure with descriptive error
- [x] `dotnet build` passes with 0 errors
- [x] `dotnet ef migrations add InitialCreate` succeeds for both contexts
