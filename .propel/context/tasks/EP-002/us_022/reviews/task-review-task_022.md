---
task_id: task_022
us_id: us_022
reviewed_by: GitHub Copilot
review_date: 2026-05-18
verdict: Pass
---

# Implementation Analysis — task_022_noshow-risk-score-booking.md

## Verdict

**Status:** Pass
**Summary:** TASK_022 is complete and all three post-review findings have been resolved. F1 — `INoShowRiskScoreService` extracted into its own file (`INoShowRiskScoreService.cs`), aligning with project convention; F2 — migration `AppointmentRiskScore` annotated with a dual-scope comment explaining it contains both TASK_021 `WaitlistEntry` and TASK_022 `Appointment` columns; F3 — `AppSettingsValidator` (`IValidateOptions<AppSettings>`) created and registered, causing the app to fail-fast at startup if `NoShowRiskThreshold` is outside 0–100. Build clean: 0 errors, 0 warnings. 256/256 tests pass.

---

## Traceability Matrix

| Requirement / AC | Evidence | Result |
|---|---|---|
| AC-001: Risk score (0–100) calculated at booking time | `riskService.CalculateAsync(patientId, slot.SlotTime, ct)` called in `HandleBookAppointment` before `SaveChangesAsync` | Pass |
| AC-001: Score capped at 100 | `Math.Min(noShowScore + leadScore + intakeScore, 100)` in `NoShowRiskScoreService.CalculateAsync` | Pass |
| AC-001: Score stored on `Appointment` | `appointment.NoShowRiskScore = riskScore` before `db.Appointments.Add(appointment)` | Pass |
| AC-002: Component 1 — prior no-shows (+20 per, max 60) | `CountAsync(NoShow) * 20`, `Math.Min(..., 60)` | Pass |
| AC-002: Component 2 — lead time bands | `leadHours < 24 ? 30 : leadHours <= 72 ? 15 : 0` | Pass |
| AC-002: Component 3 — intake completion (+10 if no intake) | `IntakeRecords.AnyAsync(i => i.PatientId == patientId)` → `hasIntake ? 0 : 10` | Pass |
| AC-003: `IsHighRisk = score >= NoShowRiskThreshold` | `IsHighRisk = riskScore >= appSettings.Value.NoShowRiskThreshold` | Pass |
| AC-003: Default threshold = 70 | `public int NoShowRiskThreshold { get; init; } = 70` in `AppSettings` | Pass |
| AC-004: Score not recalculated on updates | `NoShowRiskScore` only assigned in `HandleBookAppointment`; no PATCH/PUT path touches it | Pass |
| AC-005: `NoShowRiskScore` column in SQL Server | `migrationBuilder.AddColumn<int>("NoShowRiskScore", table: "Appointments")` in migration | Pass |
| AC-005: `IsHighRisk` column in SQL Server | `migrationBuilder.AddColumn<bool>("IsHighRisk", table: "Appointments")` in migration | Pass |
| `INoShowRiskScoreService` registered in DI | `builder.Services.AddScoped<INoShowRiskScoreService, NoShowRiskScoreService>()` in `Program.cs` | Pass |
| Convention test updated | `NoOpRiskService` stub added; `builder.Services.AddSingleton<INoShowRiskScoreService, NoOpRiskService>()` | Pass |
| `dotnet build` 0 errors | Confirmed | Pass |
| 256/256 tests pass | Confirmed | Pass |

---

## Logical & Design Findings

- **Patterns & Standards (F1 — Low) — RESOLVED:** ~~`INoShowRiskScoreService` and `NoShowRiskScoreService` co-located in one file...~~ Interface extracted to `INoShowRiskScoreService.cs`; implementation remains in `NoShowRiskScoreService.cs`. Aligns with `ICacheService`/`CacheService` and `IEmailService`/`MailKitEmailService` convention.

- **Infrastructure (F2 — Low) — RESOLVED:** ~~Migration name `AppointmentRiskScore` misleading...~~ A block comment has been added to `20260518075256_AppointmentRiskScore.cs` documenting that the migration contains both TASK_021 (`WaitlistEntry`) and TASK_022 (`Appointment`) changes, and that a rollback affects both feature sets.

- **Configuration (F3 — Low) — RESOLVED:** ~~`NoShowRiskThreshold` has no range validation...~~ `AppSettingsValidator` (`IValidateOptions<AppSettings>`) created in `src/ClinicalHealthcare.Infrastructure/Configuration/AppSettingsValidator.cs` and registered in `Program.cs` as `AddSingleton<IValidateOptions<AppSettings>, AppSettingsValidator>()`. Returns a validation failure if `NoShowRiskThreshold` is outside 0–100, causing fail-fast startup.

- **Security (OWASP A01):** `patientId` is always extracted from `JwtRegisteredClaimNames.Sub`; the risk score does not accept any patient-supplied component. No user input reaches the scoring logic directly. ✅

- **Security (OWASP A03):** All queries in `CalculateAsync` use EF Core parameterised LINQ. No raw SQL. ✅

- **Performance:** `CalculateAsync` executes two DB queries (one `CountAsync`, one `AnyAsync`). Both are targeted single-column scans with indexed foreign keys (`PatientId`). No N+1 concern. ✅

- **`IntakeRecords` global query filter:** `_db.IntakeRecords.AnyAsync(...)` automatically applies the `IsLatest = true` filter via the global query filter configured in `ApplicationDbContext`. Only the current intake version is checked. This is the correct semantic — a stale archived intake should not suppress the +10 risk component. ✅

- **Score immutability post-booking:** `NoShowRiskScore` has no `[ConcurrencyCheck]` or update guard at the EF level. However, since no other path writes to it and the field is not exposed in any PATCH endpoint, immutability is enforced by convention rather than schema constraint. Acceptable for the current scope. ✅

---

## Test Review

**Tests added:** 10 new tests

| Test Class | File | Tests |
|---|---|---|
| `NoShowRiskScoreServiceTests` | `tests/.../Services/NoShowRiskScoreServiceTests.cs` | 8 |
| `BookAppointmentEndpointTests` (new: AC-001/AC-003) | `tests/.../Features/BookAppointmentEndpointTests.cs` | 2 |

**Coverage (TASK_022 scope):**

- [x] `Calculate_3NoShows_LeadTime12h_NoIntake_Returns100` — validation strategy case 1
- [x] `Calculate_NoHistory_LongLead_IntakeComplete_Returns0` — validation strategy case 2
- [x] `Calculate_FiveNoShows_NoShowScoreCappedAt60` — component 1 cap enforcement
- [x] `Calculate_LeadTime48h_AddsOnly15Points` — component 2 middle band
- [x] `Calculate_LeadTimeLessThan24h_Adds30Points` — component 2 short band
- [x] `Calculate_NoIntakeRecord_Adds10Points` — component 3 isolation
- [x] `Calculate_ScoreNeverExceeds100` — total cap enforcement (AC-001)
- [x] `Calculate_ScoreAtThreshold_IsHighRisk` — AC-003 threshold firing
- [x] `BookAppointment_StoresRiskScoreOnAppointment` — AC-001/AC-005 endpoint integration
- [x] `BookAppointment_ScoreAboveThreshold_SetsIsHighRisk` — AC-003 endpoint integration

**Missing tests (informational only — not blocking):**

- [ ] Score not recalculated after appointment status change (AC-004 — covered by code inspection: no update path writes to `NoShowRiskScore`, but no explicit test asserts this)
- [ ] `AppSettings.NoShowRiskThreshold` value of 0 (every booking is high-risk) and 101 (no booking ever high-risk) — boundary of F3

---

## Validation Results

**Commands Executed:**

```bash
dotnet ef migrations add AppointmentRiskScore --project src/ClinicalHealthcare.Infrastructure.SqlMigrations --startup-project src/ClinicalHealthcare.Api --context ApplicationDbContext
dotnet build
dotnet test
```

| Command | Result |
|---------|--------|
| `dotnet ef migrations add AppointmentRiskScore` | ✅ Migration created — `20260518075256_AppointmentRiskScore.cs` |
| `dotnet build` | ✅ Build succeeded — 0 errors, 0 warnings |
| `dotnet test` | ✅ 256/256 pass (13 Api.Tests + 243 Infrastructure.Tests) |

---

## Fix Plan (Prioritized)

1. **F1 — RESOLVED** — `INoShowRiskScoreService` extracted to `INoShowRiskScoreService.cs`. Project convention restored.

2. **F2 — RESOLVED** — Dual-scope comment added to `20260518075256_AppointmentRiskScore.cs`. Rollback implications documented.

3. **F3 — RESOLVED** — `AppSettingsValidator` created and registered. Startup fails with a descriptive message if `NoShowRiskThreshold` is outside 0–100.

---

## Appendix

**Rules Applied:**

- `rules/security-standards-owasp.md` — A01 identity from JWT, A03 parameterised LINQ
- `rules/backend-development-standards.md` — service abstraction via interface, scoped DI lifetime
- `rules/dotnet-architecture-standards.md` — `public static` handler, `IEndpointDefinition`, `IOptions<T>` pattern
- `rules/code-anti-patterns.md` — early returns, no nested conditionals; score cap via `Math.Min`
- `rules/language-agnostic-standards.md` — KISS; two small queries preferred over one complex join
- `rules/database-standards.md` — migration naming, column defaults

**Search Evidence:**

| Pattern | File | Lines |
|---------|------|-------|
| `CalculateAsync` | `NoShowRiskScoreService.cs` | L38–58 |
| `riskService.CalculateAsync` | `BookAppointmentEndpoint.cs` | L104 |
| `NoShowRiskThreshold` | `AppSettings.cs` | L22 |
| `AppointmentRiskScore` migration | `20260518075256_AppointmentRiskScore.cs` | L13–60 |
| `AddScoped<INoShowRiskScoreService` | `Program.cs` | L112 |
