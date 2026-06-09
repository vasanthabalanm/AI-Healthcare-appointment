# Unit Test Plan - TASK_018

## Requirement Reference
- **User Story**: us_018
- **Story Location**: `.propel/context/tasks/EP-001/us_018/us_018.md`
- **Layer**: BE (infrastructure security)
- **Related Test Plans**: None (single-layer feature)
- **Acceptance Criteria Covered**:
  - AC-001: HTTP → HTTPS redirect configured (UseHttpsRedirection in Program.cs)
  - AC-002: TLS 1.2 minimum enforced (OS/IIS-level; integration test scope — static config scan only)
  - AC-003: HSTS header configured with `max-age=31536000; includeSubDomains`
  - AC-004: PHI encryption at rest uses AES-256-CBC with fresh IV per operation
  - AC-005: No PHI encryption key, JWT secret, or connection string committed in config files

## Test Plan Overview
Covers the HIPAA transmission security and PHI encryption vertical slice. Two test layers: (1) `AesEncryptionService` pure unit tests — roundtrip encrypt/decrypt, IV uniqueness, key validation — using the test constructor that accepts a `byte[]` key directly; (2) static file scan tests (sourced from `DeploymentConfigTests.cs`) verifying `Program.cs` middleware, `appsettings.json` secret hygiene, and `.gitignore` exclusion. TLS 1.2 enforcement (AC-002) and PHI-over-HTTP integration assertion (AC-004 integration path) are IIS/transport-layer concerns and are out of scope for unit tests.

## Dependent Tasks
- TASK_038 — PHI document upload via AesEncryptionService (integration concern)

## Components Under Test

| Component | Type | File Path | Responsibilities |
|-----------|------|-----------|------------------|
| `AesEncryptionService` | service class | `src/ClinicalHealthcare.Infrastructure/Security/AesEncryptionService.cs` | AES-256-CBC encrypt/decrypt; key resolution from env var or DPAPI; fresh IV per call |
| `JwtTokenService` | service class | `src/ClinicalHealthcare.Infrastructure/Auth/JwtTokenService.cs` | Validates JWT_SECRET ≥ 32 bytes at construction; fail-fast secret hygiene |
| `Program.cs` | startup config | `src/ClinicalHealthcare.Api/Program.cs` | `UseHttpsRedirection()`, `AddHsts(maxAge=365d, includeSubDomains=true)`, `UseHsts()` |
| `appsettings.json` | configuration | `src/ClinicalHealthcare.Api/appsettings.json` | Must contain no hard-coded secrets, API keys, or passwords |
| `.gitignore` | repository config | `.gitignore` | Must exclude `.env` files to prevent secret leakage |

## Test Cases

| Test-ID | Type | Description | Given | When | Then | Assertions |
|---------|------|-------------|-------|------|------|------------|
| TC-001 [SOURCE:INPUT] | positive | AES-256 encrypt+decrypt roundtrip returns original plaintext | 32-byte test key via test constructor | `Encrypt(stream)` then `Decrypt(file)` via temp file | Decrypted bytes match original | `decryptedBytes.SequenceEqual(originalBytes) == true` — Basis: AC-004 |
| TC-002 [SOURCE:INPUT] | positive | Two Encrypt calls on same data produce different IVs | 32-byte test key | `Encrypt` called twice with identical input streams | IV bytes differ between calls | `call1.Iv.SequenceEqual(call2.Iv) == false` — Basis: AC-004 fresh IV per operation |
| TC-003 [SOURCE:INPUT] | positive | Program.cs calls UseHttpsRedirection() | Source file present at compile path | Static file text search | `UseHttpsRedirection()` literal found | `content.Contains("UseHttpsRedirection()")` — Basis: AC-001 |
| TC-004 [SOURCE:INPUT] | positive | Program.cs calls AddHsts with max-age=31536000 | Source file present | Static file text search | `AddHsts(` and `31536000` found | Both strings present in Program.cs — Basis: AC-003 HSTS configuration |
| TC-005 [SOURCE:INPUT] | positive | appsettings.json contains no hard-coded secrets | Source file present | Regex scan for secret patterns `(password|secret|apikey)\s*":\s*"[^${\s]` | Zero matches | `secretPattern.IsMatch(content) == false` — Basis: AC-005 |
| TC-006 [SOURCE:INPUT] | positive | JWT_SECRET shorter than 32 bytes throws InvalidOperationException | `JWT_SECRET` env var set to 8-byte string `"tooshort"` | `new JwtTokenService()` | `InvalidOperationException` thrown | `Assert.Throws<InvalidOperationException>(() => new JwtTokenService())` — Basis: AC-005 |
| EC-001 [SOURCE:INPUT] | edge_case | AesEncryptionService test constructor rejects key != 32 bytes | `byte[]` of length 16 passed to test constructor | `new AesEncryptionService(key16)` | `ArgumentException` thrown | `Assert.Throws<ArgumentException>(() => new AesEncryptionService(key16))` — Basis: AC-004 AES-256 requires exactly 256-bit key |
| EC-002 [SOURCE:INFERRED] | edge_case | appsettings.Production.json contains no secrets (if file exists) | Production config file may or may not exist | Conditional regex scan if file exists | Zero matches or file absent | `secretPattern.IsMatch(content) == false` when file exists — Basis: production config is equally sensitive |
| ES-001 [SOURCE:INPUT] | error | .gitignore includes a pattern excluding .env files | Repository .gitignore present | Static file text search | `.env` pattern found | `content.Contains(".env")` — Basis: AC-005 CLINICAL_AES_KEY and JWT_SECRET must not be committed |

## AI Component Test Cases
> Skipped — AI Impact = No (no AIR-XXX requirements in scope for this story).

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | `tests/ClinicalHealthcare.Infrastructure.Tests/Security/AesEncryptionServiceTests.cs` | Unit tests for AES-256 encrypt/decrypt roundtrip, IV uniqueness, key validation |
| CREATE (exists) | `tests/ClinicalHealthcare.Api.Tests/Deployment/DeploymentConfigTests.cs` | Static config scan tests — TC-003, TC-004, TC-005, EC-002, ES-001 already implemented |
| CREATE (exists) | `tests/ClinicalHealthcare.Infrastructure.Tests/Features/JwtSessionTests.cs` | TC-006 (JWT_SECRET validation) — already covered |
| CREATE (mock) | `tests/ClinicalHealthcare.Infrastructure.Tests/Security/` | Directory for AES security tests |

## Mocking Strategy

| Dependency | Mock Type | Mock Behavior | Return Value |
|------------|-----------|---------------|--------------|
| `AesEncryptionService._key` | Test constructor `AesEncryptionService(byte[])` | Pre-validated 32-byte key injected directly | AES key bytes |
| File system (Decrypt) | Temp file via `Path.GetTempFileName()` | Write ciphertext blob to temp file; delete after test | Temporary file path |
| `Environment.GetEnvironmentVariable` | Direct env var set/restore | Set `JWT_SECRET` to test value; restore in test teardown | Controlled string |
| `appsettings.json` | File read via `File.ReadAllText` | Source tree traversal to solution root | File content string |

## Test Data

| Scenario | Input Data | Expected Output |
|----------|------------|-----------------|
| AES roundtrip | `byte[] plaintext = Encoding.UTF8.GetBytes("PHI:DOB=1990-01-01")` | Decrypted bytes == original |
| IV uniqueness | Same 20-byte input, two calls | `iv1.SequenceEqual(iv2) == false` |
| Short key | `new byte[16]` | `ArgumentException` |
| JWT_SECRET too short | `JWT_SECRET = "tooshort"` | `InvalidOperationException` |
| Program.cs HTTPS | Source file text | Contains `UseHttpsRedirection()` |
| Program.cs HSTS | Source file text | Contains `AddHsts(` and `31536000` |
| appsettings secret scan | JSON config file | Regex finds no matches |
| .gitignore | Repository root .gitignore | Contains `.env` |

## Test Commands
- **Run Tests**: `dotnet test ClinicalHealthcare.slnx --filter "FullyQualifiedName~AesEncryptionServiceTests|FullyQualifiedName~DeploymentConfigTests" -v q`
- **Run with Coverage**: `dotnet test ClinicalHealthcare.slnx --collect:"XPlat Code Coverage" --filter "FullyQualifiedName~AesEncryptionServiceTests"`
- **Run Single Test**: `dotnet test ClinicalHealthcare.slnx --filter "FullyQualifiedName~AesEncryptionServiceTests.Encrypt_Decrypt_Roundtrip_ReturnsOriginalPlaintext"`

## Coverage Target
- **Line Coverage**: 85%
- **Branch Coverage**: 80%
- **Critical Paths**: `AesEncryptionService.Encrypt` + `Decrypt` paths; test-constructor key-length guard; `JwtTokenService` constructor secret-length guard must have 100% coverage

## Documentation References
- **Framework Docs**: xUnit 2.x — https://xunit.net/docs; System.Security.Cryptography.Aes
- **Project Test Patterns**: `tests/ClinicalHealthcare.Api.Tests/Deployment/DeploymentConfigTests.cs` (static scan patterns)
- **Mocking Guide**: Use `AesEncryptionService(byte[] key)` test constructor — no env var setup needed; write ciphertext to `Path.GetTempFileName()` for Decrypt tests

## Implementation Checklist
- [x] Create `tests/ClinicalHealthcare.Infrastructure.Tests/Security/AesEncryptionServiceTests.cs`
- [x] Set up test data fixtures per Test Data section (inline `byte[]` plaintext; `Path.GetTempFileName()` for Decrypt)
- [x] Configure mocking dependencies per Mocking Strategy (test constructor injection; file system via temp files)
- [x] Implement positive test cases (TC-001, TC-002; TC-003–TC-006 in existing files)
- [x] Implement negative test cases (none — TC-006 is constructor validation, covered in JwtSessionTests)
- [x] Implement edge case tests (EC-001 — key length guard; EC-002 in DeploymentConfigTests)
- [x] Implement error scenario tests (ES-001 — in DeploymentConfigTests)
- [x] Run test suite and validate coverage meets target (101/101 tests green — 2026-05-24)
