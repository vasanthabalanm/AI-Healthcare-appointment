# Test Plan: UserAccount, Slot & Appointment EF Core Migrations

## Requirement Reference

| Field | Value |
|---|---|
| Epic | EP-DATA |
| User Story | US_007 |
| Layer | BE |
| AC Coverage | AC-001, AC-002, AC-003, AC-004, AC-005 |
| AI Impact | No |

## Test Plan Overview

**Purpose:** Verify that `ApplicationDbContext` correctly registers the `UserAccount`, `Slot`, and `Appointment` EF Core model configurations (indexes, concurrency tokens, FK relationships) and that `AppointmentFsmInterceptor` enforces valid appointment status transitions at the EF Core intercept layer.

**Scope:** Model metadata assertions and in-memory DbContext behavior. SQL Server–level DDL and index enforcement are integration-test concerns and are out of scope here.

## Dependent Tasks

| Task | Plan |
|---|---|
| TASK_007 | UserAccount, Slot, Appointment schema implementation |

## Components Under Test

| Component | Type | File Path | Responsibilities |
|---|---|---|---|
| `ApplicationDbContext` | EF Core DbContext | `src/ClinicalHealthcare.Infrastructure/Data/ApplicationDbContext.cs` | Registers entity model: unique index on UserAccount.Email, RowVersion on Slot, FK relationships on Appointment |
| `AppointmentFsmInterceptor` | `SaveChangesInterceptor` | `src/ClinicalHealthcare.Infrastructure/Interceptors/AppointmentFsmInterceptor.cs` | Validates appointment status transitions before DB write; throws `InvalidOperationException` on invalid transitions |
| `UserAccount` | Entity | `src/ClinicalHealthcare.Infrastructure/Entities/UserAccount.cs` | Domain entity with Email, PasswordHash, Role, PHI fields |
| `Slot` | Entity | `src/ClinicalHealthcare.Infrastructure/Entities/Slot.cs` | Scheduling slot with RowVersion concurrency token |
| `Appointment` | Entity | `src/ClinicalHealthcare.Infrastructure/Entities/Appointment.cs` | Appointment with `AppointmentStatus` enum and FK to UserAccount and Slot |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---|---|---|---|---|---|---|
| TC-001 | positive | UserAccount Email unique index declared in EF Core model | InMemory ApplicationDbContext created | Model metadata for `UserAccount` inspected | Email index exists and is unique | `entityType.GetIndexes().Any(i => i.IsUnique && i.Properties.Any(p => p.Name == "Email"))` is `true` [SOURCE:INPUT] |
| TC-002 | positive | Slot RowVersion concurrency token registered in model | InMemory ApplicationDbContext created | `Slot` model metadata inspected | RowVersion property has concurrency token flag set | `entityType.FindProperty("RowVersion")!.IsConcurrencyToken` is `true` [SOURCE:INPUT] |
| TC-003 | positive | Appointment can be inserted with valid PatientId and SlotId | InMemory context; UserAccount + Slot already saved | New Appointment added and SaveChanges called | Appointment is retrieved from Appointments DbSet | Retrieved appointment.PatientId matches inserted PatientId; Id > 0 [SOURCE:INPUT] |
| TC-004 | positive | Valid FSM transition Scheduled→Arrived accepted | Appointment with Status=Scheduled saved to InMemory DB with interceptor | Status changed to Arrived; SaveChangesAsync called | No exception thrown | `db.Appointments.First().Status == AppointmentStatus.Arrived` [SOURCE:INPUT] |
| TC-005 | positive | Valid FSM transition Arrived→Completed accepted | Appointment with Status=Arrived saved | Status changed to Completed; SaveChangesAsync called | No exception thrown | `db.Appointments.First().Status == AppointmentStatus.Completed` [SOURCE:INPUT] |
| TC-006 | negative | Invalid FSM Scheduled→Completed throws InvalidOperationException | Appointment with Status=Scheduled saved | Status changed to Completed; SaveChangesAsync called | `InvalidOperationException` thrown with message containing "Scheduled" | `ex.Message` contains "Scheduled" [SOURCE:INPUT] |
| EC-001 | edge_case | New Appointment INSERT (State=Added) does not trigger FSM guard | Empty InMemory DB with interceptor | New Appointment with Status=Scheduled saved for the first time | SaveChanges completes without exception | No exception; row count in Appointments == 1 [SOURCE:INFERRED] |
| EC-002 | edge_case | Cancelled is terminal: Cancelled→Arrived throws InvalidOperationException | Appointment with Status=Cancelled saved | Status changed to Arrived; SaveChangesAsync called | `InvalidOperationException` thrown | `ex.Message` contains "Cancelled" [SOURCE:INPUT] |

## Expected Changes

| Action | File Path | Description |
|---|---|---|
| Create | `tests/ClinicalHealthcare.Infrastructure.Tests/Migrations/UserAccountSlotAppointmentMigrationTests.cs` | xUnit test class covering TC-001 through EC-002 |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|---|---|---|---|
| `ApplicationDbContext` | Real (InMemory) | `UseInMemoryDatabase(Guid.NewGuid().ToString())` with `AppointmentFsmInterceptor` added via `AddInterceptors` | Real EF Core in-memory store |
| `AppointmentFsmInterceptor` | Real | Instantiated directly; added to DbContextOptionsBuilder | Validates transitions; throws on invalid |

> No Moq mocks required — all tests use the real EF Core InMemory provider.

## Test Data

| Scenario | Input Data | Expected Output |
|---|---|---|
| TC-003 insert | `UserAccount { Email="test@test.com", PasswordHash="hash", Role="Patient", FirstName="A", LastName="B" }`, `Slot { ... }`, `Appointment { PatientId=1, SlotId=1, Status=Scheduled }` | Appointment.Id > 0; PatientId == 1 |
| TC-004 valid FSM | Saved appointment with Status=Scheduled; update to Arrived | No exception; DB status == Arrived |
| TC-006 invalid FSM | Saved appointment with Status=Scheduled; update to Completed | `InvalidOperationException("...Scheduled → Completed...")` |
| EC-002 terminal state | Saved appointment with Status=Cancelled; update to Arrived | `InvalidOperationException("...Cancelled...")` |

## Test Commands

```bash
# Run all tests in this plan
dotnet test ClinicalHealthcare.slnx --filter "FullyQualifiedName~UserAccountSlotAppointmentMigration"

# Run single test by name
dotnet test ClinicalHealthcare.slnx --filter "FullyQualifiedName=ClinicalHealthcare.Infrastructure.Tests.Migrations.UserAccountSlotAppointmentMigrationTests.TC006_InvalidFsm_Scheduled_To_Completed_Throws"

# Coverage
dotnet test ClinicalHealthcare.slnx --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~UserAccountSlotAppointmentMigration"
```

## Coverage Target

| Metric | Target |
|---|---|
| Line Coverage | ≥ 85% |
| Branch Coverage | ≥ 80% |
| Critical Paths | All 6 `ValidTransitions` paths in `AppointmentFsmInterceptor`; Email uniqueness model registration; RowVersion token model registration |

## Documentation References

- [EF Core InMemory Testing](https://learn.microsoft.com/en-us/ef/core/testing/testing-without-the-database)
- [SaveChanges Interceptors](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors)
- [xUnit Test Patterns](https://xunit.net/docs/getting-started/netcore/cmdline)
- Existing pattern: `tests/ClinicalHealthcare.Infrastructure.Tests/Data/ApplicationDbContextTests.cs`

## Implementation Checklist

- [x] Create `tests/ClinicalHealthcare.Infrastructure.Tests/Migrations/` folder
- [x] Add `UseInMemoryDatabase(Guid.NewGuid().ToString())` + `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` to each test
- [x] Register `AppointmentFsmInterceptor` via `optionsBuilder.AddInterceptors(new AppointmentFsmInterceptor())`
- [x] Seed prerequisite UserAccount + Slot entities before Appointment insert tests
- [x] Use `await using var db = ...` for all DbContext instances
- [x] Assert FSM valid transitions (TC-004, TC-005) produce no exception using `await db.SaveChangesAsync()` directly
- [x] Assert FSM invalid transitions (TC-006, EC-002) with `await Assert.ThrowsAsync<InvalidOperationException>(...)`
- [x] Verify model metadata tests access `db.Model.FindEntityType(typeof(UserAccount))` for index/property checks
