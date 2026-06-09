using ClinicalHealthcare.Api.Features.Coding;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for Trust-First code verification endpoints (TASK_047).
/// Covers AC-001 through AC-006.
/// </summary>
public sealed class CodeVerificationEndpointTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ClinicalDbContext BuildPgDb()
    {
        var opts = new DbContextOptionsBuilder<ClinicalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ClinicalDbContext(opts);
    }

    private static ApplicationDbContext BuildSqlDb()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(opts);
    }

    private static MedicalCodeSuggestion SeedSuggestion(
        ClinicalDbContext pgDb, int patientId = 1,
        CodeType codeType = CodeType.ICD10,
        SuggestionStatus status = SuggestionStatus.Pending,
        double confidence = 0.85)
    {
        var s = new MedicalCodeSuggestion
        {
            PatientId       = patientId,
            CodeType        = codeType,
            SuggestedCode   = codeType == CodeType.ICD10 ? "J18.9" : "99213",
            CodeDescription = "Test",
            ConfidenceScore = confidence,
            Status          = status
        };
        pgDb.MedicalCodeSuggestions.Add(s);
        pgDb.SaveChanges();
        return s;
    }

    private static UserAccount SeedPatient(ApplicationDbContext sqlDb, int id = 1)
    {
        var p = new UserAccount
        {
            Id        = id,
            FirstName = "Jane",
            LastName  = "Doe",
            Email     = $"jane{id}@test.com",
            Role      = "patient",
            IsDeleted = false
        };
        sqlDb.UserAccounts.Add(p);
        sqlDb.SaveChanges();
        return p;
    }

    // ── AC-001: GET returns grouped Pending suggestions ───────────────────────

    [Fact]
    public async Task GetCodeSuggestions_ReturnsGroupedPending()
    {
        await using var pgDb = BuildPgDb();
        SeedSuggestion(pgDb, patientId: 1, codeType: CodeType.ICD10);
        SeedSuggestion(pgDb, patientId: 1, codeType: CodeType.CPT);

        var result = await GetCodeSuggestionsEndpoint.HandleGetCodeSuggestions(
            1, pgDb, CancellationToken.None);

        var statusCode = GetStatusCode(result);
        Assert.Equal(200, statusCode);
    }

    // ── L-001 (fixed): calls the handler and inspects response body ───────────

    [Fact]
    public async Task GetCodeSuggestions_ExcludesNonPendingSuggestions()
    {
        await using var pgDb = BuildPgDb();
        SeedSuggestion(pgDb, patientId: 1, codeType: CodeType.ICD10, status: SuggestionStatus.Accepted);
        SeedSuggestion(pgDb, patientId: 1, codeType: CodeType.ICD10, status: SuggestionStatus.Pending);

        var result = await GetCodeSuggestionsEndpoint.HandleGetCodeSuggestions(
            1, pgDb, CancellationToken.None);

        Assert.Equal(200, GetStatusCode(result));

        // Inspect response body — only the Pending row should be in the grouping.
        var value = result.GetType().GetProperty("Value")?.GetValue(result);
        var json  = JsonSerializer.Serialize(value);
        using var doc = JsonDocument.Parse(json);
        var icd10 = doc.RootElement.GetProperty("suggestions").GetProperty("ICD10");
        Assert.Equal(1, icd10.GetArrayLength());
    }

    // ── AC-002: PATCH accept stores verifiedById + verifiedAt ─────────────────

    [Fact]
    public async Task PatchCodeSuggestion_Returns200_WhenAccepted()
    {
        await using var pgDb = BuildPgDb();
        await using var sqlDb = BuildSqlDb();
        var s = SeedSuggestion(pgDb);

        var result = await PatchCodeSuggestionEndpoint.HandlePatchCodeSuggestion(
            s.Id,
            new PatchCodeSuggestionEndpoint.PatchCodeSuggestionRequest("Accepted", 42, null),
            pgDb, sqlDb, NullLogger<PatchCodeSuggestionEndpoint>.Instance, CancellationToken.None);

        Assert.Equal(200, GetStatusCode(result));
        var updated = await pgDb.MedicalCodeSuggestions.FindAsync(s.Id);
        Assert.Equal(SuggestionStatus.Accepted, updated!.Status);
        Assert.Equal(42, updated.VerifiedById);
        Assert.NotNull(updated.VerifiedAt);
    }

    [Fact]
    public async Task PatchCodeSuggestion_SetsModified_WhenCommittedCodeDiffers()
    {
        await using var pgDb = BuildPgDb();
        await using var sqlDb = BuildSqlDb();
        var s = SeedSuggestion(pgDb);

        var result = await PatchCodeSuggestionEndpoint.HandlePatchCodeSuggestion(
            s.Id,
            new PatchCodeSuggestionEndpoint.PatchCodeSuggestionRequest("Accepted", 42, "Z00.0"),
            pgDb, sqlDb, NullLogger<PatchCodeSuggestionEndpoint>.Instance, CancellationToken.None);

        Assert.Equal(200, GetStatusCode(result));
        var updated = await pgDb.MedicalCodeSuggestions.FindAsync(s.Id);
        Assert.Equal(SuggestionStatus.Modified, updated!.Status);
        Assert.Equal("Z00.0", updated.CommittedCode);
    }

    // ── AC-003: PATCH without verifiedById → 422 ─────────────────────────────

    [Fact]
    public async Task PatchCodeSuggestion_Returns422_WhenVerifiedByIdMissing()
    {
        await using var pgDb = BuildPgDb();
        await using var sqlDb = BuildSqlDb();
        var s = SeedSuggestion(pgDb);

        var result = await PatchCodeSuggestionEndpoint.HandlePatchCodeSuggestion(
            s.Id,
            new PatchCodeSuggestionEndpoint.PatchCodeSuggestionRequest("Accepted", null, null),
            pgDb, sqlDb, NullLogger<PatchCodeSuggestionEndpoint>.Instance, CancellationToken.None);

        Assert.Equal(422, GetStatusCode(result));
    }

    // ── Already-Accepted PATCH → 409 ─────────────────────────────────────────

    [Fact]
    public async Task PatchCodeSuggestion_Returns409_WhenAlreadyAccepted()
    {
        await using var pgDb = BuildPgDb();
        await using var sqlDb = BuildSqlDb();
        var s = SeedSuggestion(pgDb, status: SuggestionStatus.Accepted);

        var result = await PatchCodeSuggestionEndpoint.HandlePatchCodeSuggestion(
            s.Id,
            new PatchCodeSuggestionEndpoint.PatchCodeSuggestionRequest("Accepted", 42, null),
            pgDb, sqlDb, NullLogger<PatchCodeSuggestionEndpoint>.Instance, CancellationToken.None);

        Assert.Equal(409, GetStatusCode(result));
    }

    // ── M-001 regression: PATCH with invalid status → 400 ─────────────────────

    [Fact]
    public async Task PatchCodeSuggestion_Returns400_WhenStatusIsInvalid()
    {
        await using var pgDb = BuildPgDb();
        await using var sqlDb = BuildSqlDb();
        var s = SeedSuggestion(pgDb);

        var result = await PatchCodeSuggestionEndpoint.HandlePatchCodeSuggestion(
            s.Id,
            new PatchCodeSuggestionEndpoint.PatchCodeSuggestionRequest("FOO", 42, null),
            pgDb, sqlDb, NullLogger<PatchCodeSuggestionEndpoint>.Instance, CancellationToken.None);

        Assert.Equal(400, GetStatusCode(result));
    }

    // ── L-002: PATCH Rejected path ────────────────────────────────────────────

    [Fact]
    public async Task PatchCodeSuggestion_Returns200_WhenRejected()
    {
        await using var pgDb = BuildPgDb();
        await using var sqlDb = BuildSqlDb();
        var s = SeedSuggestion(pgDb);

        var result = await PatchCodeSuggestionEndpoint.HandlePatchCodeSuggestion(
            s.Id,
            new PatchCodeSuggestionEndpoint.PatchCodeSuggestionRequest("Rejected", null, null),
            pgDb, sqlDb, NullLogger<PatchCodeSuggestionEndpoint>.Instance, CancellationToken.None);

        Assert.Equal(200, GetStatusCode(result));
        var updated = await pgDb.MedicalCodeSuggestions.FindAsync(s.Id);
        Assert.Equal(SuggestionStatus.Rejected, updated!.Status);
    }

    // ── AC-004: POST accept-all transitions all Pending ───────────────────────

    [Fact]
    public async Task AcceptAll_Returns200_AndSetsAllPendingToAccepted()
    {
        await using var pgDb = BuildPgDb();
        await using var sqlDb = BuildSqlDb();
        SeedSuggestion(pgDb, patientId: 1);
        SeedSuggestion(pgDb, patientId: 1, codeType: CodeType.CPT);

        var result = await AcceptAllCodeSuggestionsEndpoint.HandleAcceptAll(
            1,
            new AcceptAllCodeSuggestionsEndpoint.AcceptAllRequest(99),
            pgDb, sqlDb, NullLogger<AcceptAllCodeSuggestionsEndpoint>.Instance, CancellationToken.None);

        Assert.Equal(200, GetStatusCode(result));

        var pending = await pgDb.MedicalCodeSuggestions
            .CountAsync(s => s.PatientId == 1 && s.Status == SuggestionStatus.Pending);
        Assert.Equal(0, pending);
    }

    [Fact]
    public async Task AcceptAll_Returns422_WhenVerifiedByIdMissing()
    {
        await using var pgDb = BuildPgDb();
        await using var sqlDb = BuildSqlDb();

        var result = await AcceptAllCodeSuggestionsEndpoint.HandleAcceptAll(
            1,
            new AcceptAllCodeSuggestionsEndpoint.AcceptAllRequest(null),
            pgDb, sqlDb, NullLogger<AcceptAllCodeSuggestionsEndpoint>.Instance, CancellationToken.None);

        Assert.Equal(422, GetStatusCode(result));
    }

    // ── L-004a: AcceptAll writes AuditLog ─────────────────────────────────────

    [Fact]
    public async Task AcceptAll_WritesAuditLog()
    {
        await using var pgDb = BuildPgDb();
        await using var sqlDb = BuildSqlDb();
        SeedSuggestion(pgDb, patientId: 1);

        await AcceptAllCodeSuggestionsEndpoint.HandleAcceptAll(
            1,
            new AcceptAllCodeSuggestionsEndpoint.AcceptAllRequest(55),
            pgDb, sqlDb, NullLogger<AcceptAllCodeSuggestionsEndpoint>.Instance, CancellationToken.None);

        var log = await sqlDb.Set<AuditLog>().SingleAsync();
        Assert.Equal(nameof(MedicalCodeSuggestion), log.EntityType);
        Assert.Equal(55, log.ActorId);
        Assert.Equal("ACCEPT_ALL", log.Action);
    }

    // ── AC-005: POST coding-complete → 409 when Pending remain ───────────────

    [Fact]
    public async Task CodingComplete_Returns409_WhenPendingRemain()
    {
        await using var pgDb = BuildPgDb();
        await using var sqlDb = BuildSqlDb();
        SeedSuggestion(pgDb, patientId: 1);

        var result = await CodingCompleteEndpoint.HandleCodingComplete(
            1, new DefaultHttpContext(), pgDb, sqlDb, CancellationToken.None);

        Assert.Equal(409, GetStatusCode(result));
    }

    // ── L-003: CodingComplete → 404 when patient not found ───────────────────

    [Fact]
    public async Task CodingComplete_Returns404_WhenPatientNotFound()
    {
        await using var pgDb = BuildPgDb();
        await using var sqlDb = BuildSqlDb();
        // No patient seeded, no pending suggestions.

        var result = await CodingCompleteEndpoint.HandleCodingComplete(
            999, new DefaultHttpContext(), pgDb, sqlDb, CancellationToken.None);

        Assert.Equal(404, GetStatusCode(result));
    }

    [Fact]
    public async Task CodingComplete_Returns200_AndSetsCodingStatusComplete()
    {
        await using var pgDb = BuildPgDb();
        await using var sqlDb = BuildSqlDb();
        SeedPatient(sqlDb, 1);
        // No pending suggestions.

        var result = await CodingCompleteEndpoint.HandleCodingComplete(
            1, new DefaultHttpContext(), pgDb, sqlDb, CancellationToken.None);

        Assert.Equal(200, GetStatusCode(result));

        var patient = await sqlDb.UserAccounts.FindAsync(1);
        Assert.Equal(CodingStatus.Complete, patient!.CodingStatus);
    }

    // ── L-004b: CodingComplete writes AuditLog ────────────────────────────────

    [Fact]
    public async Task CodingComplete_WritesAuditLog()
    {
        await using var pgDb = BuildPgDb();
        await using var sqlDb = BuildSqlDb();
        SeedPatient(sqlDb, 1);

        await CodingCompleteEndpoint.HandleCodingComplete(
            1, new DefaultHttpContext(), pgDb, sqlDb, CancellationToken.None);

        var log = await sqlDb.Set<AuditLog>().SingleAsync();
        Assert.Equal(nameof(UserAccount), log.EntityType);
        Assert.Equal("CODING_COMPLETE", log.Action);
    }

    // ── M-003 regression: CodingComplete AuditLog has ActorId from JWT ────────

    [Fact]
    public async Task CodingComplete_AuditLog_HasActorId_FromJwt()
    {
        await using var pgDb = BuildPgDb();
        await using var sqlDb = BuildSqlDb();
        SeedPatient(sqlDb, 1);

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(JwtRegisteredClaimNames.Sub, "7") }));

        await CodingCompleteEndpoint.HandleCodingComplete(
            1, httpContext, pgDb, sqlDb, CancellationToken.None);

        var log = await sqlDb.Set<AuditLog>().SingleAsync();
        Assert.Equal(7, log.ActorId);
    }

    // ── AC-006: AuditLog written for PATCH action ─────────────────────────────

    [Fact]
    public async Task PatchCodeSuggestion_WritesAuditLog()
    {
        await using var pgDb = BuildPgDb();
        await using var sqlDb = BuildSqlDb();
        var s = SeedSuggestion(pgDb);

        await PatchCodeSuggestionEndpoint.HandlePatchCodeSuggestion(
            s.Id,
            new PatchCodeSuggestionEndpoint.PatchCodeSuggestionRequest("Accepted", 5, null),
            pgDb, sqlDb, NullLogger<PatchCodeSuggestionEndpoint>.Instance, CancellationToken.None);

        var log = await sqlDb.Set<AuditLog>().SingleAsync();
        Assert.Equal(nameof(MedicalCodeSuggestion), log.EntityType);
        Assert.Equal(5, log.ActorId);
        Assert.Equal("ACCEPTED", log.Action);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int? GetStatusCode(IResult result)
    {
        // IResult implementations expose StatusCode via reflection.
        return (int?)result.GetType().GetProperty("StatusCode")?.GetValue(result);
    }
}
