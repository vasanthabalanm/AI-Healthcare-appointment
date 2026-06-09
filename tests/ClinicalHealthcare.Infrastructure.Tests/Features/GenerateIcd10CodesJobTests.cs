using ClinicalHealthcare.Infrastructure.AI;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for <see cref="GenerateIcd10CodesJob.ExecuteAsync"/> (TASK_045 AC-002, AC-003, AC-005).
/// </summary>
public sealed class GenerateIcd10CodesJobTests
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

    private static ApplicationDbContext BuildAppDb()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(opts);
    }

    private static IOllamaCodeGenerationService BuildOllamaService(
        IReadOnlyList<CodeSuggestionDto> returnValue)
    {
        var mock = new Mock<IOllamaCodeGenerationService>();
        mock.Setup(s => s.GenerateIcd10Async(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnValue);
        return mock.Object;
    }

    private static GenerateIcd10CodesJob BuildJob(
        IOllamaCodeGenerationService ollama,
        ClinicalDbContext pgDb) =>
        new(ollama, pgDb, BuildAppDb(), NullLogger<GenerateIcd10CodesJob>.Instance);

    // ── AC-002: inserts codeType=ICD10, status=Pending ────────────────────────

    [Fact]
    public async Task ExecuteAsync_InsertsIcd10PendingRows_ForValidSuggestions()
    {
        await using var pgDb = BuildPgDb();
        var suggestions = new List<CodeSuggestionDto>
        {
            new() { SuggestedCode = "J18.9", CodeDescription = "Pneumonia",    ConfidenceScore = 0.92 },
            new() { SuggestedCode = "I10",   CodeDescription = "Hypertension", ConfidenceScore = 0.88 }
        };
        var job = BuildJob(BuildOllamaService(suggestions), pgDb);

        await job.ExecuteAsync(1, CancellationToken.None);

        var rows = await pgDb.MedicalCodeSuggestions.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal(CodeType.ICD10,          r.CodeType);
            Assert.Equal(SuggestionStatus.Pending, r.Status);
        });
    }

    // ── AC-003: LowConfidenceFlag set when confidence < threshold ─────────────

    [Fact]
    public async Task ExecuteAsync_SetsLowConfidenceFlag_WhenConfidenceBelowThreshold()
    {
        await using var pgDb = BuildPgDb();
        var suggestions = new List<CodeSuggestionDto>
        {
            new() { SuggestedCode = "F32.9", CodeDescription = "Depression", ConfidenceScore = 0.45 }
        };
        var job = BuildJob(BuildOllamaService(suggestions), pgDb);

        await job.ExecuteAsync(1, CancellationToken.None);

        var row = await pgDb.MedicalCodeSuggestions.SingleAsync();
        Assert.True(row.LowConfidenceFlag);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotSetLowConfidenceFlag_WhenConfidenceAtOrAboveThreshold()
    {
        await using var pgDb = BuildPgDb();
        var suggestions = new List<CodeSuggestionDto>
        {
            new() { SuggestedCode = "Z00.0", CodeDescription = "Exam", ConfidenceScore = 0.60 },
            new() { SuggestedCode = "I10",   CodeDescription = "HTN",  ConfidenceScore = 0.85 }
        };
        var job = BuildJob(BuildOllamaService(suggestions), pgDb);

        await job.ExecuteAsync(1, CancellationToken.None);

        var rows = await pgDb.MedicalCodeSuggestions.ToListAsync();
        Assert.All(rows, r => Assert.False(r.LowConfidenceFlag));
    }

    // ── AC-005: no rows inserted when Ollama returns zero suggestions ──────────

    [Fact]
    public async Task ExecuteAsync_InsertsNoRows_WhenOllamaReturnsEmpty()
    {
        await using var pgDb = BuildPgDb();
        var job = BuildJob(BuildOllamaService([]), pgDb);

        await job.ExecuteAsync(1, CancellationToken.None);

        Assert.Empty(await pgDb.MedicalCodeSuggestions.ToListAsync());
    }

    // ── AC-005: rollback on SaveChanges failure — no partial rows ─────────────

    [Fact]
    public async Task ExecuteAsync_ThrowsAndLeavesNoRows_WhenSaveChangesFails()
    {
        var dbName = Guid.NewGuid().ToString();
        var opts = new DbContextOptionsBuilder<ClinicalDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var mockPgDb = new Mock<ClinicalDbContext>(opts) { CallBase = true };
        mockPgDb.Setup(db => db.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Simulated DB failure"));

        var suggestions = new List<CodeSuggestionDto>
        {
            new() { SuggestedCode = "J18.9", CodeDescription = "Pneumonia", ConfidenceScore = 0.90 }
        };
        var job = new GenerateIcd10CodesJob(
            BuildOllamaService(suggestions),
            mockPgDb.Object,
            BuildAppDb(),
            NullLogger<GenerateIcd10CodesJob>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => job.ExecuteAsync(1, CancellationToken.None));
    }
}
