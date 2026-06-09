# Implementation Analysis — `.propel/context/tasks/EP-TECH/us_003/task_003_dual-database-efcore-sqlserver-postgresql.md`

## Verdict

**Status:** Conditional Pass
**Summary:** All five acceptance criteria are implemented and verified. `ApplicationDbContext` (SQL Server) and `ClinicalDbContext` (PostgreSQL/Npgsql) are correctly registered in DI with fail-fast startup validation on missing environment variables, confirmed by a live exception smoke test. Both migration projects contain `InitialCreate` migrations with correct model snapshots and distinct namespaces. No cross-context transactions exist in the codebase (grep: 0 matches). Two gaps prevent a full Pass: (1) the `IDesignTimeDbContextFactory` implementations in the `ClinicalHealthcare.Infrastructure` project are dead code — EF tooling uses the factories in the migration projects, not the Infrastructure project, making the Infrastructure factories a DRY violation; (2) the PostgreSQL design-time fallback connection string contains a hardcoded default password (`Password=postgres`) in source code, which is an OWASP A07 finding even for design-time-only code.

---

## Traceability Matrix

| Requirement / AC | Evidence (file : function / line) | Result |
|---|---|---|
| AC-001 — `ApplicationDbContext` targets SQL Server; conn string from `SQLSERVER_CONNECTION_STRING` | `Program.cs` L18 `RequireConnectionString("SQLSERVER_CONNECTION_STRING")`; L22-24 `AddDbContext<ApplicationDbContext>` + `UseSqlServer` | **Pass** |
| AC-001 — `MigrationsAssembly` points to SQL migrations project | `Program.cs` L23: `sql.MigrationsAssembly("ClinicalHealthcare.Infrastructure.SqlMigrations")` | **Pass** |
| AC-002 — `ClinicalDbContext` targets PostgreSQL via Npgsql; conn string from `POSTGRES_CONNECTION_STRING` | `Program.cs` L19 `RequireConnectionString("POSTGRES_CONNECTION_STRING")`; L27-29 `AddDbContext<ClinicalDbContext>` + `UseNpgsql` | **Pass** |
| AC-002 — `MigrationsAssembly` points to PG migrations project | `Program.cs` L28: `npgsql.MigrationsAssembly("ClinicalHealthcare.Infrastructure.PgMigrations")` | **Pass** |
| AC-003 — No cross-context transactions | `grep TransactionScope\|BeginTransaction\|IDbContextTransaction` → 0 matches; separate `DbContext` classes, no shared state | **Pass** |
| AC-004 — Both contexts registered in DI; resolve at startup | `AddDbContext<ApplicationDbContext>` + `AddDbContext<ClinicalDbContext>` in `Program.cs`; `ValidateOnBuild = true` | **Pass** |
| AC-004 — `ValidateScopes = true` | `Program.cs` L48: `options.ValidateScopes = true` | **Pass** |
| AC-005 — Separate migration assembly for SQL Server | `SqlMigrations/Migrations/20260513164432_InitialCreate.cs` + `ApplicationDbContextModelSnapshot.cs` ✓ | **Pass** |
| AC-005 — Separate migration assembly for PostgreSQL | `PgMigrations/Migrations/20260513164450_InitialCreate.cs` + `ClinicalDbContextModelSnapshot.cs` ✓ | **Pass** |
| Edge — Missing env var fails fast with descriptive error | Smoke test: `System.InvalidOperationException: Required environment variable 'SQLSERVER_CONNECTION_STRING' is not set.` | **Pass** |
| Edge — Cross-context transaction architecturally prevented | Two independent `DbContext` subclasses; no `IDbContextTransaction` shared scope possible | **Pass** |
| Infrastructure factories used by EF tooling | `ApplicationDbContextFactory` / `ClinicalDbContextFactory` in `Infrastructure` project — NOT picked up by EF tooling | **Gap** — dead code |
| Credentials in source code | `ClinicalDbContextFactory.cs` L18; `ClinicalDbContextMigrationFactory.cs` L16: `Password=postgres` | **Gap** — OWASP A07 |

---

## Logical & Design Findings

**Business Logic:**
- `RequireConnectionString` is a module-level static local function called before `builder.Services` — this is the correct pattern for minimal API. Connection strings are captured in closures and never re-evaluated at request time. Correct behaviour: connection strings are immutable for the lifetime of the process.
- Both contexts use `DbContextOptions<T>` (strongly typed) rather than the untyped `DbContextOptions` — correct, prevents cross-context option injection.
- `ValidateOnBuild` + `ValidateScopes` are set — these verify DI graph correctness including scoped-service-from-singleton mismatches.

**Security:**
- `Password=postgres` is hardcoded as the design-time fallback in two files: `Infrastructure/Data/ClinicalDbContextFactory.cs` L18 and `PgMigrations/ClinicalDbContextMigrationFactory.cs` L16. Even though this is design-time only (never used at runtime), default credentials in source code violate OWASP A07 and will trigger secret-scanning tools (GitHub secret scanning, Dependabot). Fix: replace with a placeholder string that is clearly unusable (e.g., `"Host=localhost;Database=clinical_dev;Username=postgres;Password=REPLACE_ME_DEV_ONLY"`) or remove the password field and rely on `pg_hba.conf` trust for local dev.
- `Server=(localdb)\\mssqllocaldb` fallback in SQL Server factories is Windows-only (`localdb`) and does not contain credentials. Safe.
- No connection string ever written to logs — correct.

**Error Handling:**
- `RequireConnectionString` throws `InvalidOperationException` with a clear message naming the missing variable. Correct fail-fast behaviour.
- No retry logic on `AddDbContext` — correct for connection setup (retries belong in the repository/command handler layer).

**Data Access:**
- Both `DbContext` classes have empty `OnModelCreating` with placeholder comments — correct for scaffold phase. Entity configurations will be added per feature task.
- No `DbSet<T>` properties yet — correct; this task is scaffold only.
- No `SaveChangesAsync` overrides for audit logging — expected to be added in a dedicated audit task.

**Patterns & Standards:**
- Factory duplication: `ApplicationDbContextFactory` in Infrastructure + `ApplicationDbContextMigrationFactory` in SqlMigrations perform identical work. EF Core tooling uses the factory in `--startup-project`, which is the migration project. The Infrastructure factory is never invoked by tooling or DI. **Dead code** — should be deleted to avoid future confusion about which factory is canonical.
- `#nullable disable` in generated migration files — this is EF-generated, expected and acceptable.
- `TreatWarningsAsErrors = true` present in all three affected `.csproj` files — correct.

**Performance:**
- `AddDbContext<T>` registers contexts with scoped lifetime (default) — correct for web API request-per-scope pattern.
- No connection pooling configuration — default EF Core connection pooling applies. Acceptable for scaffold.

---

## Test Review

**Existing Tests:** None — no test project exists.

**Missing Tests (must add):**

- [ ] **Integration — DI resolution**: Use `WebApplicationFactory` with both env vars set → `app.Services.GetRequiredService<ApplicationDbContext>()` and `GetRequiredService<ClinicalDbContext>()` resolve without throwing.
- [ ] **Integration — fail-fast `SQLSERVER_CONNECTION_STRING`**: Start host without `SQLSERVER_CONNECTION_STRING` env var → `InvalidOperationException` with message containing `"SQLSERVER_CONNECTION_STRING"`.
- [ ] **Integration — fail-fast `POSTGRES_CONNECTION_STRING`**: Same pattern for Postgres env var.
- [ ] **Unit — `ApplicationDbContext` uses SQL Server provider**: `context.Database.ProviderName` equals `"Microsoft.EntityFrameworkCore.SqlServer"`.
- [ ] **Unit — `ClinicalDbContext` uses Npgsql provider**: `context.Database.ProviderName` equals `"Npgsql.EntityFrameworkCore.PostgreSQL"`.
- [ ] **Architecture — no cross-context transactions**: ArchUnit-style grep or Roslyn analyser confirming no `IDbContextTransaction` used across both contexts in the same scope.

---

## Validation Results

**Commands Executed:**

| Command | Outcome |
|---------|---------|
| `dotnet build` | **Pass** — "Build succeeded. 0 Warning(s) 0 Error(s)" |
| Start without env vars | **Pass** — `InvalidOperationException: Required environment variable 'SQLSERVER_CONNECTION_STRING' is not set.` |
| `dotnet ef migrations add InitialCreate` (SQL Server) | **Pass** — `Done. To undo this action, use 'ef migrations remove'` |
| `dotnet ef migrations add InitialCreate` (PostgreSQL) | **Pass** — `Done. To undo this action, use 'ef migrations remove'` |
| `grep TransactionScope\|BeginTransaction\|IDbContextTransaction` | **Pass** — 0 matches |
| `list_dir Migrations/` (both projects) | **Pass** — `InitialCreate.cs`, `InitialCreate.Designer.cs`, `ModelSnapshot.cs` present |

---

## Fix Plan (Prioritized)

| # | Fix | Files / Functions | Effort | Risk |
|---|-----|-------------------|--------|------|
| 1 | **Remove dead Infrastructure factories** — delete `ApplicationDbContextFactory.cs` and `ClinicalDbContextFactory.cs` from `src/ClinicalHealthcare.Infrastructure/Data/`; EF tooling uses migration-project factories only | `Infrastructure/Data/ApplicationDbContextFactory.cs`, `Infrastructure/Data/ClinicalDbContextFactory.cs` | 5 min | L — no runtime impact; clarifies canonical factory location |
| 2 | **Replace hardcoded `Password=postgres`** — change to `Password=REPLACE_ME_DEV_ONLY` in both migration factories so secret scanners don't flag | `Infrastructure/Data/ClinicalDbContextFactory.cs` L18; `PgMigrations/ClinicalDbContextMigrationFactory.cs` L16 | 5 min | **M** — OWASP A07; will block GitHub secret scanning on push |
| 3 | **Add integration test project** — `tests/ClinicalHealthcare.Api.IntegrationTests` with `WebApplicationFactory` covering DI resolution and fail-fast validation | New project | 45 min | L |

---

## Appendix

**Rules applied:**
- `rules/security-standards-owasp.md` — OWASP A07 (default credentials in source)
- `rules/language-agnostic-standards.md` — KISS, single source of truth
- `rules/backend-development-standards.md` — EF Core DI patterns, DbContext lifetime
- `rules/dry-principle-guidelines.md` — Duplicate factory implementations
- `rules/code-anti-patterns.md` — Dead code, duplicated logic

**Search Evidence:**

| Pattern | Scope | Result |
|---------|-------|--------|
| `TransactionScope\|BeginTransaction\|IDbContextTransaction` | `src/**/*.cs` | 0 matches — no cross-context transactions |
| `ApplicationDbContext\|ClinicalDbContext` | `Program.cs` | Lines 22-29 — both registered |
| `RequireConnectionString` | `Program.cs` | Lines 12-19 — fail-fast before builder |
| `InitialCreate.cs` + `ModelSnapshot.cs` | Both migration projects | Present in `Migrations/` dirs |
| `IDesignTimeDbContextFactory` | `src/**/*.cs` | 4 implementations — 2 in Infrastructure (dead), 2 in migration projects (active) |
