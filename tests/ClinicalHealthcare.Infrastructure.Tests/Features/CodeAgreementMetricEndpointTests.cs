using ClinicalHealthcare.Api.Features.Admin;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for <see cref="CodeAgreementMetricEndpoint.HandleGetCodeAgreement"/> (TASK_048).
/// Covers AC-001 through AC-005.
/// </summary>
public sealed class CodeAgreementMetricEndpointTests
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

    private static void SeedSuggestion(
        ClinicalDbContext pgDb,
        SuggestionStatus  status,
        string            suggestedCode  = "J18.9",
        string?           committedCode  = null,
        DateTime?         verifiedAt     = null,
        int               patientId      = 1)
    {
        pgDb.MedicalCodeSuggestions.Add(new MedicalCodeSuggestion
        {
            PatientId       = patientId,
            CodeType        = CodeType.ICD10,
            SuggestedCode   = suggestedCode,
            CommittedCode   = committedCode,
            CodeDescription = "Test",
            ConfidenceScore = 0.85,
            Status          = status,
            VerifiedAt      = verifiedAt ?? DateTime.UtcNow
        });
        pgDb.SaveChanges();
    }

    private static JsonElement GetBody(IResult result)
    {
        var value = result.GetType().GetProperty("Value")?.GetValue(result);
        return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(value));
    }

    private static int? GetStatusCode(IResult result) =>
        (int?)result.GetType().GetProperty("StatusCode")?.GetValue(result);

    // ── AC-001: returns correct DTO fields ────────────────────────────────────

    [Fact]
    public async Task GetCodeAgreement_ReturnsDtoWithAllFields()
    {
        await using var pgDb = BuildPgDb();
        SeedSuggestion(pgDb, SuggestionStatus.Accepted);
        SeedSuggestion(pgDb, SuggestionStatus.Rejected);

        var result = await CodeAgreementMetricEndpoint.HandleGetCodeAgreement(
            pgDb, NullLogger<CodeAgreementMetricEndpoint>.Instance, days: 30);

        Assert.Equal(200, GetStatusCode(result));
        var body = GetBody(result);
        Assert.True(body.TryGetProperty("agreementRate",  out _));
        Assert.True(body.TryGetProperty("totalActioned",  out _));
        Assert.True(body.TryGetProperty("accepted",       out _));
        Assert.True(body.TryGetProperty("modified",       out _));
        Assert.True(body.TryGetProperty("rejected",       out _));
        Assert.True(body.TryGetProperty("windowDays",     out _));
    }

    // ── AC-002: agreementRate = accepted-unmodified / totalActioned ───────────

    [Fact]
    public async Task GetCodeAgreement_ComputesAgreementRate_WhenAllAcceptedUnmodified()
    {
        await using var pgDb = BuildPgDb();
        // 2 accepted (no committedCode), 1 rejected
        SeedSuggestion(pgDb, SuggestionStatus.Accepted);
        SeedSuggestion(pgDb, SuggestionStatus.Accepted);
        SeedSuggestion(pgDb, SuggestionStatus.Rejected);

        var result = await CodeAgreementMetricEndpoint.HandleGetCodeAgreement(
            pgDb, NullLogger<CodeAgreementMetricEndpoint>.Instance, days: 30);

        var body = GetBody(result);
        // agreementRate = 2/3 ≈ 0.6667
        Assert.Equal(0.6667, body.GetProperty("agreementRate").GetDouble(), 4);
        Assert.Equal(3, body.GetProperty("totalActioned").GetInt32());
        Assert.Equal(2, body.GetProperty("accepted").GetInt32());
        Assert.Equal(1, body.GetProperty("rejected").GetInt32());
    }

    [Fact]
    public async Task GetCodeAgreement_CountsModifiedAsNotAgreed()
    {
        await using var pgDb = BuildPgDb();
        // 1 accepted-unmodified, 1 accepted-with-committed-different
        SeedSuggestion(pgDb, SuggestionStatus.Accepted, suggestedCode: "J18.9", committedCode: null);
        SeedSuggestion(pgDb, SuggestionStatus.Modified,  suggestedCode: "J18.9", committedCode: "Z00.0");

        var result = await CodeAgreementMetricEndpoint.HandleGetCodeAgreement(
            pgDb, NullLogger<CodeAgreementMetricEndpoint>.Instance, days: 30);

        var body = GetBody(result);
        // agreementRate = 1/2 = 0.5
        Assert.Equal(0.5, body.GetProperty("agreementRate").GetDouble(), 4);
        Assert.Equal(1, body.GetProperty("modified").GetInt32());
    }

    [Fact]
    public async Task GetCodeAgreement_ExcludesSuggestionsOutsideWindow()
    {
        await using var pgDb = BuildPgDb();
        // Old suggestion — outside 30-day window
        SeedSuggestion(pgDb, SuggestionStatus.Accepted,
            verifiedAt: DateTime.UtcNow.AddDays(-60));
        // Recent suggestion inside window
        SeedSuggestion(pgDb, SuggestionStatus.Rejected,
            verifiedAt: DateTime.UtcNow.AddDays(-5));

        var result = await CodeAgreementMetricEndpoint.HandleGetCodeAgreement(
            pgDb, NullLogger<CodeAgreementMetricEndpoint>.Instance, days: 30);

        var body = GetBody(result);
        Assert.Equal(1, body.GetProperty("totalActioned").GetInt32());
        Assert.Equal(0, body.GetProperty("accepted").GetInt32());
        Assert.Equal(1, body.GetProperty("rejected").GetInt32());
    }

    [Fact]
    public async Task GetCodeAgreement_ExcludesPendingSuggestions()
    {
        await using var pgDb = BuildPgDb();
        SeedSuggestion(pgDb, SuggestionStatus.Pending);
        SeedSuggestion(pgDb, SuggestionStatus.Accepted);

        var result = await CodeAgreementMetricEndpoint.HandleGetCodeAgreement(
            pgDb, NullLogger<CodeAgreementMetricEndpoint>.Instance, days: 30);

        var body = GetBody(result);
        // Only 1 actioned (Accepted), Pending excluded
        Assert.Equal(1, body.GetProperty("totalActioned").GetInt32());
    }

    // ── AC-004: zero actioned → agreementRate:null ────────────────────────────

    [Fact]
    public async Task GetCodeAgreement_ReturnsNullRate_WhenNoActionedSuggestions()
    {
        await using var pgDb = BuildPgDb();

        var result = await CodeAgreementMetricEndpoint.HandleGetCodeAgreement(
            pgDb, NullLogger<CodeAgreementMetricEndpoint>.Instance, days: 30);

        Assert.Equal(200, GetStatusCode(result));
        var body = GetBody(result);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("agreementRate").ValueKind);
        Assert.Equal(0, body.GetProperty("totalActioned").GetInt32());
        Assert.Equal(0, body.GetProperty("accepted").GetInt32());
        Assert.Equal(0, body.GetProperty("modified").GetInt32());
        Assert.Equal(0, body.GetProperty("rejected").GetInt32());
        Assert.True(body.TryGetProperty("message", out _));
    }

    // ── AC-005: days validation ───────────────────────────────────────────────

    [Fact]
    public async Task GetCodeAgreement_Returns422_WhenDaysIsZero()
    {
        await using var pgDb = BuildPgDb();

        var result = await CodeAgreementMetricEndpoint.HandleGetCodeAgreement(
            pgDb, NullLogger<CodeAgreementMetricEndpoint>.Instance, days: 0);

        Assert.Equal(422, GetStatusCode(result));
    }

    [Fact]
    public async Task GetCodeAgreement_Returns422_WhenDaysIsNegative()
    {
        await using var pgDb = BuildPgDb();

        var result = await CodeAgreementMetricEndpoint.HandleGetCodeAgreement(
            pgDb, NullLogger<CodeAgreementMetricEndpoint>.Instance, days: -5);

        Assert.Equal(422, GetStatusCode(result));
    }

    [Fact]
    public async Task GetCodeAgreement_CapsDaysAt365_WhenDaysExceedsMax()
    {
        await using var pgDb = BuildPgDb();
        SeedSuggestion(pgDb, SuggestionStatus.Accepted,
            verifiedAt: DateTime.UtcNow.AddDays(-200));

        var result = await CodeAgreementMetricEndpoint.HandleGetCodeAgreement(
            pgDb, NullLogger<CodeAgreementMetricEndpoint>.Instance, days: 400);

        Assert.Equal(200, GetStatusCode(result));
        var body = GetBody(result);
        // windowDays should be capped to 365
        Assert.Equal(365, body.GetProperty("windowDays").GetInt32());
        // Suggestion at -200 days is within 365-day window
        Assert.Equal(1, body.GetProperty("totalActioned").GetInt32());
    }

    [Fact]
    public async Task GetCodeAgreement_DefaultsDaysTo30_WhenNotProvided()
    {
        await using var pgDb = BuildPgDb();
        // Suggestion older than 30 days — should be excluded from default window
        SeedSuggestion(pgDb, SuggestionStatus.Accepted,
            verifiedAt: DateTime.UtcNow.AddDays(-40));

        var result = await CodeAgreementMetricEndpoint.HandleGetCodeAgreement(
            pgDb, NullLogger<CodeAgreementMetricEndpoint>.Instance);

        var body = GetBody(result);
        // Default is 30; suggestion at -40 days is outside
        Assert.Equal(0, body.GetProperty("totalActioned").GetInt32());
        Assert.Equal(30, body.GetProperty("windowDays").GetInt32());
    }

    // ── Mixed ICD-10 + CPT — both counted together ────────────────────────────

    [Fact]
    public async Task GetCodeAgreement_CountsBothIcd10AndCpt()
    {
        await using var pgDb = BuildPgDb();
        pgDb.MedicalCodeSuggestions.Add(new MedicalCodeSuggestion
        {
            PatientId = 1, CodeType = CodeType.ICD10,
            SuggestedCode = "J18.9", CodeDescription = "Pneumonia",
            ConfidenceScore = 0.9, Status = SuggestionStatus.Accepted,
            VerifiedAt = DateTime.UtcNow
        });
        pgDb.MedicalCodeSuggestions.Add(new MedicalCodeSuggestion
        {
            PatientId = 1, CodeType = CodeType.CPT,
            SuggestedCode = "99213", CodeDescription = "Office Visit",
            ConfidenceScore = 0.8, Status = SuggestionStatus.Accepted,
            VerifiedAt = DateTime.UtcNow
        });
        pgDb.SaveChanges();

        var result = await CodeAgreementMetricEndpoint.HandleGetCodeAgreement(
            pgDb, NullLogger<CodeAgreementMetricEndpoint>.Instance, days: 30);

        var body = GetBody(result);
        Assert.Equal(2, body.GetProperty("totalActioned").GetInt32());
        Assert.Equal(2, body.GetProperty("accepted").GetInt32());
        Assert.Equal(1.0, body.GetProperty("agreementRate").GetDouble());
    }
}
