# Unit Test Plan - TASK_006

## Requirement Reference

- **User Story**: US_006 — Serilog structured logging, Seq integration, and OSS licence audit
- **Story Location**: `.propel/context/tasks/EP-TECH/us_006/us_006.md`
- **Layer**: BE
- **Related Test Plans**: `../us_002/unittest/test_plan_be_webapi-scaffold.md`
- **Acceptance Criteria Covered**:
  - AC-001: Serilog rolling file sink — daily rotation, 30-day retention
  - AC-002: Seq CE sink receives structured log events with `CorrelationId` property
  - AC-003: `CorrelationIdMiddleware` generates/propagates `X-Correlation-ID` per request
  - AC-004: `PhiRedactingDestructuringPolicy` replaces PHI fields with `[REDACTED]`
  - AC-005: OSS licence audit script exits non-zero on disallowed licence

---

## Test Plan Overview

Validates structured-logging infrastructure in two layers. `CorrelationIdMiddleware` is
tested through a lightweight `HttpContext` pipeline using `WebApplicationFactory` — no Seq
or file sink is invoked. `PhiRedactingDestructuringPolicy` is unit-tested directly against
Serilog's `IDestructuringPolicy` interface using real `LogEvent` objects constructed in
memory. All Serilog sinks are replaced with an in-memory `TestSink` (from
`Serilog.Sinks.InMemory`) so no disk or network I/O occurs during tests.

---

## Dependent Tasks

- TASK_002 (US_002) — Web API scaffold provides `Program.cs` middleware registration

---

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `CorrelationIdMiddleware` | ASP.NET Core middleware | `src/ClinicalHealthcare.Api/Middleware/CorrelationIdMiddleware.cs` | Read/generate `X-Correlation-ID`; push to `LogContext`; echo in response |
| `PhiRedactingDestructuringPolicy` | Serilog destructuring policy | `src/ClinicalHealthcare.Infrastructure/Logging/PhiRedactingDestructuringPolicy.cs` | Replace PHI property values with `[REDACTED]` |

---

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 [SOURCE:INPUT] | positive | Middleware generates GUID when `X-Correlation-ID` header absent | `DefaultHttpContext` with no `X-Correlation-ID` request header | Middleware `InvokeAsync` called | New GUID attached to response header | `response.Headers["X-Correlation-ID"]` is a valid GUID string; `Guid.TryParse` returns `true` |
| TC-002 [SOURCE:INPUT] | positive | Middleware propagates existing `X-Correlation-ID` when header present | `DefaultHttpContext` with `X-Correlation-ID: test-corr-123` request header | Middleware `InvokeAsync` called | Same value echoed in response header | `response.Headers["X-Correlation-ID"]` equals `"test-corr-123"` |
| TC-003 [SOURCE:INPUT] | positive | `X-Correlation-ID` is present in response header in all cases | `DefaultHttpContext` (with and without header) | Middleware `InvokeAsync` called | Response always contains `X-Correlation-ID` header | `response.Headers.ContainsKey("X-Correlation-ID")` is `true` for both with-header and without-header scenarios |
| TC-004 [SOURCE:INPUT] | positive | `PhiRedactingDestructuringPolicy` redacts `Email` field | Serilog `LogEvent` object created with property `Email = "patient@example.com"` | Policy `TryDestructure` called | `Email` value replaced with `"[REDACTED]"` | Resulting log event property `Email` value equals `"[REDACTED]"`; original value not present anywhere in output |
| TC-005 [SOURCE:INPUT] | positive | Policy redacts all known PHI property names | Log events with properties `DateOfBirth`, `PhoneNumber`, `FirstName`, `LastName`, `SSN`, `Address` | Policy applied to each | All PHI property values replaced with `"[REDACTED]"` | Each property value equals `"[REDACTED]"` after destructuring |
| TC-006 [SOURCE:INPUT] | positive | Non-PHI properties pass through unmodified | Log event with properties `UserId = 42`, `RequestPath = "/api/health"`, `StatusCode = 200` | Policy applied | Non-PHI properties unchanged | Property values identical after policy application; no `[REDACTED]` substitution |
| EC-001 [SOURCE:INFERRED] | edge_case | Null PHI field value → replaced with `[REDACTED]` (not left as null) | Log event with `Email = null` | Policy applied | Value replaced with `"[REDACTED]"` string | Property `Email` has scalar value `"[REDACTED]"`; is not null | Basis: null PHI is still PHI; redaction must not expose absent-value metadata |
| ES-001 [SOURCE:INFERRED] | error | Empty-string `X-Correlation-ID` header treated as absent | `DefaultHttpContext` with `X-Correlation-ID: ""` request header | Middleware `InvokeAsync` called | New GUID generated (empty string not propagated) | `response.Headers["X-Correlation-ID"]` is a valid GUID; `Guid.TryParse` returns `true` | Basis: empty correlation ID provides no tracing value; middleware should generate a replacement GUID |

---

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `tests/ClinicalHealthcare.Api.Tests/Middleware/CorrelationIdMiddlewareTests.cs` | TC-001, TC-002, TC-003, ES-001 |
| CREATE | `tests/ClinicalHealthcare.Infrastructure.Tests/Logging/PhiRedactingDestructuringPolicyTests.cs` | TC-004, TC-005, TC-006, EC-001 |
| CREATE | `tests/ClinicalHealthcare.Infrastructure.Tests/Helpers/LogEventBuilder.cs` | Builds `LogEvent` with named scalar properties for policy tests |

---

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `HttpContext` (middleware tests) | `DefaultHttpContext` (real) | Constructed with mock `RequestDelegate` that completes synchronously | `Task.CompletedTask` from next delegate |
| `RequestDelegate` | `Mock<RequestDelegate>` (Moq) | Captures invocation; completes without side effects | `Task.CompletedTask` |
| Serilog sinks (policy tests) | `Serilog.Sinks.InMemory.InMemorySink` | Captures emitted log events in memory; inspected post-emission | In-memory event list |
| `ILogEventPropertyFactory` | real `MessageTemplateParser` + `LogEventProperty` | Constructs Serilog property objects for policy input | Real Serilog objects |

---

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| No correlation header | `DefaultHttpContext`, no `X-Correlation-ID` | New GUID string in response header |
| Existing correlation header | `X-Correlation-ID: test-corr-123` | `test-corr-123` echoed in response |
| Empty correlation header | `X-Correlation-ID: ""` | New GUID generated |
| PHI Email field | `Email = "patient@example.com"` | `Email = "[REDACTED]"` in log output |
| PHI null field | `Email = null` | `Email = "[REDACTED]"` in log output |
| Non-PHI field | `UserId = 42` | `UserId = 42` unchanged in log output |

---

## Test Commands

- **Run Tests**: `dotnet test tests/ --filter "Category=Middleware|Category=Logging" --no-build`
- **Run with Coverage**: `dotnet test tests/ --collect:"XPlat Code Coverage" --results-directory ./coverage`
- **Run Single Test**: `dotnet test tests/ClinicalHealthcare.Api.Tests/ --filter "FullyQualifiedName~CorrelationIdMiddlewareTests"`

---

## Coverage Target

- **Line Coverage**: 95%
- **Branch Coverage**: 90%
- **Critical Paths**: `CorrelationIdMiddleware` — header absent / present / empty branches; `PhiRedactingDestructuringPolicy` — all PHI field names enumerated; null-value branch

---

## Documentation References

- **Serilog Destructuring Policies**: <https://github.com/serilog/serilog/wiki/Destructuring>
- **Serilog.Sinks.InMemory**: <https://github.com/serilog-contrib/serilog-sinks-inmemory>
- **ASP.NET Core Middleware Testing**: <https://learn.microsoft.com/en-us/aspnet/core/test/middleware>

---

## Implementation Checklist

- [x] Create `LogEventBuilder` helper for constructing Serilog `LogEvent` objects in tests
- [x] Implement TC-001/TC-002/TC-003/ES-001 — `CorrelationIdMiddleware` header generation/propagation/echo
- [x] Implement TC-004/TC-005 — `PhiRedactingDestructuringPolicy` redacts Email + all PHI field names
- [x] Implement TC-006 — non-PHI fields pass through unmodified
- [x] Implement EC-001 — null PHI value replaced with `[REDACTED]` string
- [x] Run test suite; validate all 8 test cases pass
- [x] Verify branch coverage ≥ 90% on `CorrelationIdMiddleware.cs` and `PhiRedactingDestructuringPolicy.cs`
