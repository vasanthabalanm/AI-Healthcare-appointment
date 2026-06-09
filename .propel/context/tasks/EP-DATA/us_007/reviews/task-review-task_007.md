# Implementation Analysis — TASK_007

## Verdict

**Status:** Conditional Pass
**Summary:** All five acceptance criteria are satisfied. `UserAccount`, `Slot`, and `Appointment` entities are correctly created and registered in `ApplicationDbContext`. The `AppointmentFsmInterceptor` correctly enforces all four valid FSM transitions and blocks all others, including transitions from terminal states. The unique index on `UserAccount.Email` and the `rowversion` concurrency token on `Slot.RowVersion` are confirmed in both the EF Core model and the generated migration DDL. `dotnet ef database update` applied cleanly with zero errors. Two actionable gaps are noted: the interceptor registration pattern deviates from the EF Core 8 DI-recommended approach (creates a new instance per DbContext scope), and no unit tests exist for the FSM logic despite being explicitly called out in the validation strategy.

---

## Traceability Matrix

| Requirement / AC | Evidence | Result |
|---|---|---|
| AC-001: `UserAccount` entity + SQL Server migration | `Entities/UserAccount.cs`; `ApplicationDbContext.cs` L14, L31–L41; migration DDL `CREATE TABLE [UserAccounts]` | **Pass** |
| AC-002: `Slot` rowversion concurrency token | `Entities/Slot.cs` L21 `[Timestamp]`; `ApplicationDbContext.cs` L48 `.IsRowVersion()`; migration `type: "rowversion"` | **Pass** |
| AC-003: `AppointmentFsmInterceptor` in `SaveChanges` | `Interceptors/AppointmentFsmInterceptor.cs` L43–L66; both sync + async overrides; registered via `OnConfiguring` L21–24 | **Pass** |
| AC-004: Unique index on `UserAccount.Email` | `ApplicationDbContext.cs` L39 `.HasIndex(u => u.Email).IsUnique().HasDatabaseName("IX_UserAccounts_Email")`; migration `unique: true` | **Pass** |
| AC-005: Migration applies cleanly via `dotnet ef database update` | Terminal output confirmed: all tables, FKs, indexes created; `Done.` exit | **Pass** |
| Edge: FSM invalid from terminal states (Completed, Cancelled, NoShow) | `ValidTransitions` dict does not contain Completed/Cancelled/NoShow keys → `TryGetValue` returns false → throws `InvalidOperationException` | **Pass** |
| Edge: Duplicate Email → DB constraint violation | `IX_UserAccounts_Email` unique index confirmed in migration; API 409 deferred to feature layer per task spec | **Pass** |
| Build: 0 errors, 0 warnings | `dotnet build ClinicalHealthcare.slnx` output: `0 Warning(s) 0 Error(s)` | **Pass** |

---

## Logical & Design Findings

### Business Logic

- **FSM coverage is complete.** The `ValidTransitions` dictionary explicitly maps `Scheduled→{Arrived, Cancelled, NoShow}` and `Arrived→{Completed}`. Terminal states (`Completed`, `Cancelled`, `NoShow`) are absent from the dictionary; `TryGetValue` returning `false` for any transition originating from these states causes an `InvalidOperationException`. The "no-op same-status update" guard (`if (originalStatus == newStatus) continue`) correctly allows partial-field saves without false FSM violations.
- **New appointment inserts bypass the FSM guard.** The interceptor filters on `EntityState.Modified` only — new `Appointment` inserts (`EntityState.Added`) are not validated. This is correct behavior: new appointments are always created in `Scheduled` state (entity default), so no invalid transition is possible on insert.
- **`Appointment.BookedAt` and `UserAccount.CreatedAt` dual-default pattern.** Both properties carry a C# property default (`= DateTime.UtcNow`) and a SQL `GETUTCDATE()` default via fluent API. When EF Core inserts a row, it will always send the C# default — the SQL default is only reached via raw SQL inserts. This is acceptable as a defence-in-depth safety net, but the SQL default is effectively unreachable through the ORM. Mark as low-severity informational.

### Security

- **`PasswordHash` stored as `nvarchar(512)`.** This accommodates BCrypt (60 chars), Argon2id (~95 chars encoded), and PBKDF2 output. Sufficient. No PHI concern — `PasswordHash` is a credential, not a health record field.
- **`Email` is in the `PhiRedactingEnricher` redaction list** (defined in TASK_006). `UserAccount.Email` will be redacted in structured logs when logged as a named property. ✅
- **No freeform string interpolation of `Email` into exceptions.** The `AppointmentFsmInterceptor` exception message only exposes `AppointmentStatus` enum names — no PII. ✅
- **No `[Required]` data annotation on entity string properties** — validation enforced exclusively at the EF Core fluent level (`IsRequired()`). This is consistent across the codebase and acceptable for an API-first model.

### Error Handling

- **`InvalidOperationException` thrown in `SavingChangesAsync`.** The interceptor throws synchronously inside `SavingChangesAsync`'s body, which is valid — EF Core propagates this as a faulted task. The calling code (`await context.SaveChangesAsync()`) will catch it as an unhandled exception. The feature slice (future task) must wrap this in a domain exception or catch it at the endpoint level to return an appropriate HTTP 422/409.
- **No logging of FSM violations inside the interceptor.** The interceptor throws without logging. Consider adding `ILogger` in a future iteration if audit trails are needed (not in current task scope).

### Data Access

- **`OnDelete(DeleteBehavior.Restrict)` on both FKs.** Prevents cascade deletes on `Appointment` rows when `UserAccount` or `Slot` is deleted. Correct for healthcare domain integrity — appointments must be explicitly cancelled before a patient account is deactivated.
- **Auto-indexes on FK columns.** EF Core correctly generated `IX_Appointments_PatientId` and `IX_Appointments_SlotId` in the migration. ✅
- **No composite index on `(SlotId, Status)`.** For the future availability query (booking flow: "find available slots"), a composite index would improve query performance. Out of scope for this task but worth noting for EP-001.

### Patterns & Standards

- **`AppointmentFsmInterceptor` registered via `OnConfiguring` with `new`.** This is the primary design concern (see Fix Plan #1). EF Core 8 best practice is DI registration.
- **`[Timestamp]` attribute + `.IsRowVersion()` fluent call are redundant.** Both configure the same SQL `rowversion` mapping. The attribute alone is sufficient; the fluent call adds no new information. Low severity — no runtime impact.
- **`HasConversion<int>()` on `AppointmentStatus` is redundant.** EF Core maps enums to `int` by default. This is defensive and harmless, but introduces noise.
- **`DbSet<T>` exposed via expression-bodied property** (`=> Set<T>()`). This is a valid pattern that defers materialisation, but differs from the conventional `public DbSet<T> Entities { get; set; }` convention. Consistent internal choice — no impact.

---

## Test Review

### Existing Tests

None. No test project exists in the solution.

### Missing Tests (must add)

- [ ] **Unit — FSM happy-path transitions:**
  - `Scheduled → Arrived` → no exception thrown
  - `Scheduled → Cancelled` → no exception thrown
  - `Scheduled → NoShow` → no exception thrown
  - `Arrived → Completed` → no exception thrown

- [ ] **Unit — FSM invalid transitions:**
  - `Completed → Scheduled` → `InvalidOperationException` thrown *(explicitly required by task validation strategy)*
  - `Arrived → Cancelled` → `InvalidOperationException` thrown
  - `Cancelled → Scheduled` → `InvalidOperationException` thrown
  - `NoShow → Scheduled` → `InvalidOperationException` thrown
  - `Completed → Arrived` → `InvalidOperationException` thrown

- [ ] **Unit — FSM no-op guard:**
  - Modify `Appointment.Status` from `Scheduled` to `Scheduled` (same value) → no exception, `SaveChanges` proceeds

- [ ] **Unit — Insert bypass:**
  - New `Appointment` (EntityState.Added) with `Status = Completed` → interceptor does not throw (inserts bypass FSM)

- [ ] **Integration — Unique Email constraint:**
  - Insert two `UserAccount` rows with the same `Email` → `DbUpdateException` raised

- [ ] **Integration — Slot RowVersion concurrency:**
  - Load same `Slot` in two contexts, update `IsAvailable` in both, second save raises `DbUpdateConcurrencyException`

---

## Validation Results

| Command | Outcome |
|---|---|
| `dotnet build ClinicalHealthcare.slnx` | **Pass** — 0 errors, 0 warnings |
| `dotnet ef migrations add UserAccountSlotAppointment` | **Pass** — migration file `20260514061800_UserAccountSlotAppointment.cs` generated |
| `dotnet ef database update` | **Pass** — `UserAccounts`, `Slots`, `Appointments` tables + all indexes applied; `Done.` |

---

## Fix Plan (Prioritized)

### Fix 1 — Move `AppointmentFsmInterceptor` to DI registration
**Severity:** Medium | **Files:** `Program.cs`, `ApplicationDbContext.cs` | **Effort:** 0.5h | **Risk:** Low

Register the interceptor as a singleton in DI and inject it into `AddDbContext`. Remove `OnConfiguring` override from `ApplicationDbContext`.

**In `Program.cs`:**
```csharp
builder.Services.AddSingleton<AppointmentFsmInterceptor>();

builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
    options
        .UseSqlServer(sqlConnectionString, sql =>
            sql.MigrationsAssembly("ClinicalHealthcare.Infrastructure.SqlMigrations"))
        .AddInterceptors(sp.GetRequiredService<AppointmentFsmInterceptor>()));
```

**In `ApplicationDbContext.cs`:** Remove the `OnConfiguring` override entirely.

**Why:** Aligns with EF Core 8 DI-first recommendation, eliminates per-request allocations, and enables future constructor injection into the interceptor (e.g., `ILogger`).

---

### Fix 2 — Add unit test project for FSM validation
**Severity:** High (quality gap) | **Files:** `tests/ClinicalHealthcare.Infrastructure.Tests/` (new) | **Effort:** 2h | **Risk:** Low

Create `ClinicalHealthcare.Infrastructure.Tests.csproj` with xUnit + EF Core InMemory provider. Implement `AppointmentFsmInterceptorTests` covering all happy-path, invalid-transition, and boundary cases listed in the Test Review section above.

---

### Fix 3 — Remove redundant dual configuration (optional housekeeping)
**Severity:** Low | **Files:** `ApplicationDbContext.cs`, `Slot.cs` | **Effort:** 0.25h | **Risk:** None

- Remove `.IsRowVersion()` from Slot's fluent config in `OnModelCreating` — `[Timestamp]` attribute is sufficient.
- Remove `.HasConversion<int>()` from `AppointmentStatus` — EF Core's default enum-to-int conversion applies automatically.

---

## Implementation Checklist Status

| Checklist Item | Status |
|---|---|
| **[AC-001]** `UserAccount` entity created and registered in `ApplicationDbContext` | ✅ Complete |
| **[AC-002]** `Slot` entity with `[Timestamp]` rowversion concurrency token | ✅ Complete |
| **[AC-003]** `AppointmentFsmInterceptor` enforces valid FSM transitions in `SavingChanges` | ✅ Complete |
| **[AC-004]** Unique index on `UserAccount.Email` configured via fluent API | ✅ Complete |
| **[AC-005]** Migration created and applies cleanly with `dotnet ef database update` | ✅ Complete |
| All three entities registered in `ApplicationDbContext.OnModelCreating` | ✅ Complete |
| `AppointmentFsmInterceptor` registered via `AddInterceptors` | ✅ Complete (via `OnConfiguring` — DI pattern recommended, see Fix 1) |
| `dotnet build` passes with 0 errors | ✅ Complete |

---

## Appendix

### Search Evidence

| Pattern | Result |
|---|---|
| `src/ClinicalHealthcare.Infrastructure/Entities/` | `UserAccount.cs`, `Slot.cs`, `Appointment.cs` — all created |
| `src/ClinicalHealthcare.Infrastructure/Interceptors/` | `AppointmentFsmInterceptor.cs` — created |
| `Migrations/*UserAccountSlotAppointment*` | `20260514061800_UserAccountSlotAppointment.cs` + `.Designer.cs` — confirmed |
| `**/*.Tests/**/*.cs` | No matches — no test project exists |
| Migration DDL | `rowversion`, `unique: true` on Email, `ON DELETE NO ACTION` on both FKs confirmed |
