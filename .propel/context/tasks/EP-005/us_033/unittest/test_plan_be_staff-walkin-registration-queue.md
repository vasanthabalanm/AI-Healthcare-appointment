# Unit Test Plan - TASK_033

## Requirement Reference
- **User Story**: us_033
- **Story Location**: `.propel/context/tasks/EP-005/us_033/us_033.md`
- **Layer**: BE
- **Related Test Plans**: None (single-layer BE story)
- **Acceptance Criteria Covered**:
  - AC-001: `GET /staff/patients/search?q=&dob=` returns matching patients (max 50 results)
  - AC-002: Walk-in patient added to queue returns 201 with position
  - AC-003: Queue at capacity without override returns 409
  - AC-004: Override flag bypasses capacity and writes AuditLog entry
  - AC-005: Minimal walk-in profile created with `WalkIn=true`, `Role="patient"`

## Test Plan Overview

Tests `SearchPatientsEndpoint.HandleSearch` and `RegisterWalkInEndpoint.HandleRegisterWalkIn`. `HandleSearch` performs case-sensitive EF.Functions.Like on In-Memory database (test data uses exact case matches). `HandleRegisterWalkIn` reads staff ID from JWT sub claim (OWASP A01), deduplicates by FirstName+LastName+DOB, enforces queue capacity via `AppSettings.QueueCapacity`, and writes an AuditLog on capacity override. Two edge-case tests (EC-001, EC-002) document gaps between US requirements and current source — minimum-3-char search and duplicate-queue-entry prevention are not yet implemented.

## Dependent Tasks

- TASK_001 (us_007) — `UserAccount` entity
- TASK_001 (us_011) — `AuditLog` entity

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `SearchPatientsEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Staff/SearchPatientsEndpoint.cs` | Case-insensitive LIKE patient search by name and/or DOB |
| `RegisterWalkInEndpoint` | class | `src/ClinicalHealthcare.Api/Features/Staff/RegisterWalkInEndpoint.cs` | Create walk-in profile, capacity check, queue entry insertion, AuditLog on override |
| `QueueEntry` | entity | `src/ClinicalHealthcare.Infrastructure/Entities/QueueEntry.cs` | Queue position, status, date scope |
| `AppSettings` | class | `src/ClinicalHealthcare.Infrastructure/Configuration/AppSettings.cs` | `QueueCapacity` default 20 |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | Search by name returns ordered matches `[SOURCE:INPUT]` | Seed `UserAccount(FirstName="John",LastName="Smith",Role="patient")` and `UserAccount(FirstName="Jane",LastName="Doe",Role="patient")` | `HandleSearch(q:"Smith", dob:null, db, ct)` | HTTP 200; list contains John Smith only; ordered by LastName | `StatusCode==200`; `results.Count==1`; `results[0].FullName=="John Smith"` |
| TC-002 | positive | Search by name + DOB narrows results `[SOURCE:INPUT]` | Seed two patients with same last name "Brown" but different DOBs | `HandleSearch(q:"Brown", dob:new DateOnly(1985,4,12), db, ct)` | HTTP 200; only patient with matching DOB returned | `results.Count==1`; `results[0].Dob==new DateOnly(1985,4,12)` |
| TC-003 | positive | Neither q nor dob → 200 empty array `[SOURCE:INPUT]` | Seeded patients in DB | `HandleSearch(q:null, dob:null, db, ct)` | HTTP 200; empty array (no full-table scan) | `StatusCode==200`; `results.Count==0` |
| TC-004 | positive | New walk-in creates UserAccount(WalkIn=true) + QueueEntry → 201 `[SOURCE:INPUT]` | Empty DB; staff JWT (sub="99"); `WalkInRequest{FirstName="Alice",LastName="Brown",DateOfBirth=1990-03-15}` | `HandleRegisterWalkIn(request, httpContext, db, appSettings, ct)` | HTTP 201; one `UserAccount` in DB with `WalkIn=true`,`Role="patient"`; `QueueEntry` with `Position=1`, `IsWalkIn=true`, `AddedByStaffId=99` | `StatusCode==201`; `db.UserAccounts.Single().WalkIn==true`; `db.QueueEntries.Single().Position==1`; `result.Position==1` |
| TC-005 | positive | Existing patient linked; no duplicate `UserAccount` `[SOURCE:INPUT]` | Seed `UserAccount(FirstName="Bob",LastName="Lee",DateOfBirth=1970-01-01,Role="patient")` in DB; same names+DOB in request | `HandleRegisterWalkIn(request, httpContext, db, appSettings, ct)` | HTTP 201; `db.UserAccounts.Count()==1`; QueueEntry has existing patientId | `StatusCode==201`; `db.UserAccounts.Count()==1`; `queueEntry.PatientId==seededPatient.Id` |
| TC-006 | negative | Queue at capacity without override → 409 `[SOURCE:INPUT]` | Seed 20 `QueueEntry` rows for today (`QueueDate=DateOnly.FromDateTime(DateTime.UtcNow)`) with `Status=Waiting`; request `Override=false` | `HandleRegisterWalkIn(request, httpContext, db, appSettings, ct)` | HTTP 409; no new `QueueEntry` | `StatusCode==409`; `db.QueueEntries.Count()==20` |
| TC-007 | positive | Override bypasses capacity → 201 + AuditLog `[SOURCE:INPUT]` | Seed 20 `QueueEntry` rows for today; request `Override=true`; staff JWT (sub="5") | `HandleRegisterWalkIn(request, httpContext, db, appSettings, ct)` | HTTP 201; 21 `QueueEntry` rows; `AuditLog.Action=="QUEUE_OVERRIDE"`, `AuditLog.ActorId==5` | `StatusCode==201`; `db.QueueEntries.Count()==21`; `db.AuditLogs.Single().Action=="QUEUE_OVERRIDE"`; `db.AuditLogs.Single().ActorId==5` |
| EC-001 | edge_case | Search query < 3 chars → 422 `[SOURCE:INPUT]` **Gap:** current source does not enforce minimum search length — returns results instead. Basis: US_033 edge case: "minimum 3-character search enforced". | Seeded patients | `HandleSearch(q:"Jo", dob:null, db, ct)` | HTTP 422 | `StatusCode==422`. **Note:** test will fail until minimum-length guard is implemented in `HandleSearch`. |
| EC-002 | edge_case | Patient already in today's queue → 409 `[SOURCE:INPUT]` **Gap:** current source does not prevent a second QueueEntry for the same patient on the same day. Basis: US_033 edge case: "duplicate queue entry is prevented". | Seed patient + `QueueEntry(Status=Waiting,QueueDate=today)` for same patient | `HandleRegisterWalkIn(request{same patientId}, httpContext, db, appSettings, ct)` | HTTP 409 "Patient already in today's queue"; only one QueueEntry | `StatusCode==409`; `db.QueueEntries.Count()==1`. **Note:** test will fail until duplicate-queue guard is added. |
| ES-001 | error | No JWT sub → 401; no QueueEntry created `[SOURCE:INFERRED]` Basis: standard OWASP A01 guard present in all staff endpoints. | `DefaultHttpContext` with no claims | `HandleRegisterWalkIn(request, DefaultHttpContext, db, appSettings, ct)` | HTTP 401 | `StatusCode==401`; `db.QueueEntries.Count()==0` |
| ES-002 | error | Missing FirstName → 422 validation error `[SOURCE:INFERRED]` Basis: `WalkInRequest.FirstName` carries `[Required]`; validation gate fires before DB access. | `WalkInRequest{FirstName="", LastName="Lee"}` | `HandleRegisterWalkIn(request, httpContext, db, appSettings, ct)` | HTTP 422; no UserAccount created | `StatusCode==422`; `db.UserAccounts.Count()==0` |

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/StaffWalkInEndpointTests.cs` | TC-001 through ES-002 test methods |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ApplicationDbContext` | In-Memory EF Core | `UseInMemoryDatabase(Guid.NewGuid().ToString())`; `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` | Per-test isolated store |
| `IOptions<AppSettings>` | `Options.Create<AppSettings>` | Wraps `new AppSettings { QueueCapacity = 20 }` (or override per test) | Configurable capacity |
| `HttpContext` (staff) | `DefaultHttpContext` | JWT sub claim set via `ClaimsPrincipal` with `JwtRegisteredClaimNames.Sub` = staffId string | StaffId as integer via `int.TryParse` |
| `HttpContext` (no claims) | `DefaultHttpContext` | Empty `ClaimsPrincipal` (no sub claim) | 401 path |

### Helper Patterns

```csharp
private static ApplicationDbContext BuildDb() =>
    new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

private static HttpContext BuildStaffContext(int staffId) {
    var ctx = new DefaultHttpContext();
    ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, staffId.ToString()),
    }));
    return ctx;
}

private static IOptions<AppSettings> Settings(int capacity = 20) =>
    Options.Create(new AppSettings { QueueCapacity = capacity });
```

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Name search match | `UserAccount(FirstName="John",LastName="Smith",Role="patient")` | `results[0].FullName=="John Smith"` |
| Name+DOB search | Two "Brown" patients with different DOBs | Only DOB-matching patient returned |
| New walk-in | `WalkInRequest{FirstName="Alice",LastName="Brown",DateOfBirth=1990-03-15}` | `UserAccount.WalkIn==true`; `QueueEntry.Position==1` |
| Duplicate patient | Seed existing same name+DOB | `db.UserAccounts.Count()==1`; linked patientId in result |
| Full capacity | 20 seeded QueueEntries for today | 409; count unchanged |
| Override | 20 seeded QueueEntries + `Override=true` | 201; count=21; AuditLog present |

## Test Commands

- **Run Tests**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~StaffWalkInEndpointTests"`
- **Run with Coverage**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~StaffWalkInEndpointTests"`
- **Run Single Test**: `dotnet test tests\ClinicalHealthcare.Infrastructure.Tests\ --filter "FullyQualifiedName~StaffWalkInEndpointTests.TC_001"`

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**: Capacity guard (both true/false branches); override branch; duplicate-patient link vs create; JWT guard (401 path)

## Documentation References

- **Framework Docs**: [xUnit.net](https://xunit.net/docs/getting-started/netcore/cmdline)
- **Mocking Guide**: [Moq Quickstart](https://github.com/moq/moq4/wiki/Quickstart)
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/AiIntakeEndpointTests.cs`
- **EF Core InMemory**: [InMemory Database Provider](https://learn.microsoft.com/en-us/ef/core/providers/in-memory/)

## Implementation Checklist

- [x] Create test file `tests/.../Features/StaffWalkInEndpointTests.cs`
- [x] Set up `BuildDb()`, `BuildStaffContext()`, `Settings()` helpers
- [x] Implement positive test cases (TC-001 to TC-005, TC-007)
- [x] Implement negative test cases (TC-006)
- [x] Implement edge case tests (EC-001, EC-002)
- [x] Implement error scenario tests (ES-001, ES-002)
- [x] Document gaps: EC-001 (3-char minimum) and EC-002 (duplicate queue guard) as known failures
- [x] Run test suite and validate coverage meets target
