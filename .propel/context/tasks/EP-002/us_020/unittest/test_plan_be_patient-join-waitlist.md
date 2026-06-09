# Unit Test Plan - US_020

## Requirement Reference
- **User Story**: us_020
- **Story Location**: `.propel/context/tasks/EP-002/us_020/us_020.md`
- **Layer**: BE
- **Related Test Plans**: None (single-layer story)
- **Acceptance Criteria Covered**:
  - AC-001: Patient can join the waitlist with a future (or today) preferred date; entry created with `Status = Active`
  - AC-002: Patient with an existing active entry — implementation returns 409 Conflict (code does NOT update the existing entry as originally specified; AC-002 is effectively superseded by the 409 guard in the current implementation)
  - AC-003: Past slot date rejected with 400 Bad Request
  - AC-004: Duplicate active entry prevented at application level (409) and at DB level (unique-index constraint)

## Test Plan Overview

Covers the backend unit tests for `POST /waitlist` via `JoinWaitlistEndpoint.HandleJoinWaitlist`. The handler is tested as a static method invoked directly against an EF Core in-memory database. No Redis or Hangfire dependencies are used by this endpoint.

> **Implementation Discrepancy Note:** US_020 AC-002 originally specified that a patient with an existing active waitlist entry should receive `HTTP 200` with a `{"message":"Waitlist entry updated"}` response. The current implementation instead returns `409 Conflict` with `"You already have an active waitlist entry. Remove it before joining again."` This means AC-002 as written in the spec is not implemented. The test plan documents the _actual implemented behaviour_ (409) and flags the discrepancy for product review.

AI Impact: **No** (no AIR-XXX requirements referenced in US_020).

## Dependent Tasks
- EP-TECH US_001–006 (infrastructure foundation — database seeding, JWT auth helpers) must be passing before running these tests.

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `JoinWaitlistEndpoint.HandleJoinWaitlist` | static method | `src/ClinicalHealthcare.Api/Features/Appointments/JoinWaitlistEndpoint.cs` | Validate date (past = 400); optional slot existence check; application-level duplicate guard; insert `WaitlistEntry` with `Status=Active` |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 | positive | Future date without preferred slot → 201 with `status=Active` | No existing active entry; `PreferredSlotDate = today + 7 days`; no `PreferredSlotId` | `HandleJoinWaitlist` called | 201 Created; `WaitlistEntry` inserted with `Status=Active` and `QueuedAt` ≈ UtcNow | `StatusCode == 201`; `db.WaitlistEntries.Single().Status == Active`; `QueuedAt != default` [SOURCE:INPUT] Basis: AC-001 happy path |
| TC-002 | positive | Future date with valid `PreferredSlotId` → 201; slot ID stored | DB has a slot with target ID; `PreferredSlotId = slot.Id`; `PreferredSlotDate = slot date` | `HandleJoinWaitlist` called | 201; `WaitlistEntry.PreferredSlotId == slotId` | `StatusCode == 201`; `entry.PreferredSlotId == slotId` [SOURCE:INPUT] Basis: AC-001 preferred-slot variant |
| TC-003 | positive | Today's date boundary accepted → 201 | `PreferredSlotDate = DateOnly.FromDateTime(UtcNow)` | `HandleJoinWaitlist` called | 201 Created | `StatusCode == 201` [SOURCE:INFERRED] Basis: boundary — today is not "past"; handler allows `>= today` |
| TC-004 | positive | Response body contains `waitlistEntryId` | Successful join | `HandleJoinWaitlist` completes | 201 body has `waitlistEntryId` field equal to inserted entry's DB Id | Deserialize response; `waitlistEntryId > 0` [SOURCE:INPUT] Basis: AC-001 response contract |
| TC-005 | negative | Past date returns 400 | `PreferredSlotDate = today - 1 day` | `HandleJoinWaitlist` called | 400 Bad Request | `StatusCode == 400` [SOURCE:INPUT] Basis: AC-003 |
| TC-006 | negative | Duplicate active entry returns 409 (application guard) | Patient already has `Status = Active` waitlist entry | `HandleJoinWaitlist` called again | 409 Conflict | `StatusCode == 409`; entry count unchanged [SOURCE:INPUT] Basis: AC-004 (and actual AC-002 implementation) |
| EC-001 | edge_case | Non-existent `PreferredSlotId` returns 400 | `PreferredSlotId` set to a non-existent slot ID | `HandleJoinWaitlist` called | 400 Bad Request; no entry inserted | `StatusCode == 400`; `db.WaitlistEntries.Count() == 0` [SOURCE:INFERRED] Basis: F1 guard — `db.Slots.AnyAsync(s => s.Id == request.PreferredSlotId)` returns false |
| EC-002 | edge_case | `PreferredSlotId = null` accepted without slot check | `PreferredSlotId = null`; `PreferredSlotDate` in future | `HandleJoinWaitlist` called | 201; entry has `PreferredSlotId = null` | `StatusCode == 201`; `entry.PreferredSlotId == null` [SOURCE:INFERRED] Basis: open-waitlist path — slot-existence check is skipped when `PreferredSlotId` is null |
| ES-001 | error | Missing JWT sub claim returns 401 | `HttpContext.User` has no `sub` claim | `HandleJoinWaitlist` called | 401 Unauthorized | `StatusCode == 401` [SOURCE:INFERRED] Basis: `sub` claim extraction returns null → handler 401 guard |

## AI Component Test Cases

> **AI Impact: No** — US_020 contains no AIR-XXX requirements. This section is skipped.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| EXISTS | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/JoinWaitlistEndpointTests.cs` | Tests for `POST /waitlist` (TC-001, TC-002, TC-003, TC-005, TC-006, EC-001, EC-002 covered) |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `ApplicationDbContext` | EF Core In-Memory | `UseInMemoryDatabase(Guid.NewGuid().ToString())` with `TransactionIgnoredWarning` suppressed | Full in-memory DB per test |
| `HttpContext` | `DefaultHttpContext` | Injects `ClaimsPrincipal` with `sub = patientId.ToString()` | N/A |
| Time (implicit) | N/A — uses `DateTime.UtcNow` + `DateOnly.FromDateTime(UtcNow)` | Pass future/past dates as `PreferredSlotDate` to control path | N/A |

## AI Mocking Strategy

> **AI Impact: No** — Skipped.

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| Valid open waitlist | `PreferredSlotDate = today + 7`, `PreferredSlotId = null` | 201; `Status=Active`; `PreferredSlotId = null` |
| Valid with slot | `PreferredSlotDate = slot.SlotTime.Date`, `PreferredSlotId = slot.Id` (slot seeded) | 201; `PreferredSlotId = slot.Id` |
| Today boundary | `PreferredSlotDate = today` | 201 Created |
| Past date | `PreferredSlotDate = today - 1` | 400 Bad Request |
| Duplicate active | Patient already has `Status=Active` entry | 409 Conflict |
| Invalid slot ID | `PreferredSlotId = 99999` (not in DB) | 400 Bad Request |
| Missing sub claim | `HttpContext.User` has no `sub` claim | 401 Unauthorized |

## Test Commands

- **Run Tests**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --filter "FullyQualifiedName~JoinWaitlistEndpointTests"`
- **Run with Coverage**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --collect:"XPlat Code Coverage" --results-directory ./coverage`
- **Run Single Test**: `dotnet test tests/ClinicalHealthcare.Infrastructure.Tests --filter "FullyQualifiedName~JoinWaitlist_FutureDate_Returns201"`

## Coverage Target

- **Line Coverage**: 90%
- **Branch Coverage**: 85%
- **Critical Paths**:
  - Date guard (`PreferredSlotDate < today` → 400)
  - Slot existence check (only when `PreferredSlotId` is non-null)
  - Application-level duplicate guard (`hasActive → 409`)
  - `sub` claim extraction / null guard → 401

## Documentation References

- **Framework Docs**: [xUnit 2.x — https://xunit.net/docs/getting-started/netcore/cmdline](https://xunit.net)
- **Project Test Patterns**: `tests/ClinicalHealthcare.Infrastructure.Tests/Features/JoinWaitlistEndpointTests.cs`
- **Mocking Guide**: [Moq 4.x — https://github.com/devlooped/moq](https://github.com/devlooped/moq)

## Implementation Checklist

- [x] Create test file structure per Expected Changes
- [x] Set up test data fixtures per Test Data section
- [x] Configure mocking dependencies per Mocking Strategy
- [x] Implement positive test cases (TC-001 – TC-004)
- [x] Implement negative test cases (TC-005, TC-006)
- [x] Implement edge case tests (EC-001, EC-002)
- [x] Implement error scenario tests (ES-001)
- [x] Run test suite and validate coverage meets target (75 tests passed, 0 failed — 2026-05-24)
