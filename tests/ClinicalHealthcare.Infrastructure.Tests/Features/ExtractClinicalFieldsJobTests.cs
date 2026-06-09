using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Jobs;
using ClinicalHealthcare.Infrastructure.NLP;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for <see cref="ExtractClinicalFieldsJob.ExecuteAsync"/> and
/// <see cref="ClinicalFieldExtractor.Extract"/> (TASK_041 AC-001–AC-004).
/// </summary>
public sealed class ExtractClinicalFieldsJobTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ApplicationDbContext BuildSqlDb()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(opts);
    }

    private static ClinicalDbContext BuildPgDb()
    {
        var opts = new DbContextOptionsBuilder<ClinicalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ClinicalDbContext(opts);
    }

    private static ClinicalDocument SeedDocument(
        ApplicationDbContext sqlDb,
        int id = 1,
        int patientId = 2,
        OcrStatus status = OcrStatus.Extracted,
        string? rawText = "BP: 120/80\nAspirin 100mg")
    {
        var doc = new ClinicalDocument
        {
            Id                = id,
            PatientId         = patientId,
            OcrStatus         = status,
            RawOcrText        = rawText,
            EncryptedBlobPath = "enc/a.bin",
            UploadedByStaffId = 1,
            OriginalFileName  = "scan.pdf"
        };
        sqlDb.ClinicalDocuments.Add(doc);
        sqlDb.SaveChanges();
        return doc;
    }

    private static ExtractClinicalFieldsJob BuildJob(
        ApplicationDbContext       sqlDb,
        ClinicalDbContext          pgDb,
        Mock<IBackgroundJobClient> jobs)
    {
        var extractor = new ClinicalFieldExtractor(NullLogger<ClinicalFieldExtractor>.Instance);
        return new ExtractClinicalFieldsJob(
            sqlDb, pgDb, extractor, jobs.Object,
            NullLogger<ExtractClinicalFieldsJob>.Instance);
    }

    // ── TC-001: OcrStatus=Extracted + text → fields inserted; dedup enqueued ─

    [Fact]
    public async Task ExtractJob_ExtractedStatus_InsertsFieldsAndEnqueuesDedup()
    {
        await using var sqlDb = BuildSqlDb();
        await using var pgDb  = BuildPgDb();
        SeedDocument(sqlDb, rawText: "BP: 120/80\nAspirin 100mg daily");
        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(sqlDb, pgDb, jobs);

        await job.ExecuteAsync(1, null!);

        var count = await pgDb.ExtractedClinicalFields.CountAsync();
        Assert.True(count >= 1);
        jobs.Verify(j => j.Create(
            It.Is<Job>(j2 => j2.Type == typeof(DeduplicateClinicalFieldsJob)),
            It.IsAny<IState>()), Times.Once);
    }

    // ── TC-002: OcrStatus=LowConfidence → fields extracted; ConfidenceScore=0.60 ──

    [Fact]
    public async Task ExtractJob_LowConfidenceStatus_SetsConfidenceScore060()
    {
        await using var sqlDb = BuildSqlDb();
        await using var pgDb  = BuildPgDb();
        SeedDocument(sqlDb, status: OcrStatus.LowConfidence, rawText: "Diagnosis: Hypertension");
        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(sqlDb, pgDb, jobs);

        await job.ExecuteAsync(1, null!);

        var field = await pgDb.ExtractedClinicalFields.FirstAsync();
        Assert.Equal(0.60, field.ConfidenceScore);
    }

    // ── TC-003: OcrStatus=Extracted → ConfidenceScore=0.90 (heuristic [SOURCE:INPUT]) ──

    [Fact]
    public async Task ExtractJob_ExtractedStatus_SetsConfidenceScore090()
    {
        await using var sqlDb = BuildSqlDb();
        await using var pgDb  = BuildPgDb();
        SeedDocument(sqlDb, rawText: "Allergy to penicillin");
        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(sqlDb, pgDb, jobs);

        await job.ExecuteAsync(1, null!);

        var field = await pgDb.ExtractedClinicalFields.FirstAsync();
        Assert.Equal(0.90, field.ConfidenceScore);
    }

    // ── TC-004: Extracted fields carry correct PatientId and DocumentId ───────

    [Fact]
    public async Task ExtractJob_ExtractedFields_HaveCorrectPatientAndDocumentId()
    {
        await using var sqlDb = BuildSqlDb();
        await using var pgDb  = BuildPgDb();
        SeedDocument(sqlDb, id: 5, patientId: 7, rawText: "Aspirin 100mg daily");
        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(sqlDb, pgDb, jobs);

        await job.ExecuteAsync(5, null!);

        var fields = await pgDb.ExtractedClinicalFields.ToListAsync();
        Assert.All(fields, f =>
        {
            Assert.Equal(7, f.PatientId);
            Assert.Equal(5, f.DocumentId);
        });
    }

    // ── TC-005: OcrStatus=NoData → no fields; dedup NOT enqueued ─────────────

    [Fact]
    public async Task ExtractJob_NoDataStatus_SkipsInsertionAndDoesNotEnqueueDedup()
    {
        await using var sqlDb = BuildSqlDb();
        await using var pgDb  = BuildPgDb();
        SeedDocument(sqlDb, status: OcrStatus.NoData, rawText: null);
        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(sqlDb, pgDb, jobs);

        await job.ExecuteAsync(1, null!);

        Assert.Equal(0, await pgDb.ExtractedClinicalFields.CountAsync());
        jobs.Verify(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    // ── TC-006: Whitespace RawOcrText → no fields; dedup NOT enqueued ─────────

    [Fact]
    public async Task ExtractJob_WhitespaceRawOcrText_SkipsInsertionAndDoesNotEnqueueDedup()
    {
        await using var sqlDb = BuildSqlDb();
        await using var pgDb  = BuildPgDb();
        SeedDocument(sqlDb, status: OcrStatus.Extracted, rawText: "   ");
        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(sqlDb, pgDb, jobs);

        await job.ExecuteAsync(1, null!);

        Assert.Equal(0, await pgDb.ExtractedClinicalFields.CountAsync());
        jobs.Verify(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    // ── TC-007: Document not found → early return; no exception ──────────────

    [Fact]
    public async Task ExtractJob_DocumentNotFound_ReturnsWithoutException()
    {
        await using var sqlDb = BuildSqlDb();
        await using var pgDb  = BuildPgDb();
        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(sqlDb, pgDb, jobs);

        await job.ExecuteAsync(9999, null!);

        Assert.Equal(0, await pgDb.ExtractedClinicalFields.CountAsync());
    }

    // ── TC-008: Idempotency — already extracted → skip re-insert; re-enqueue dedup ──

    [Fact]
    public async Task ExtractJob_AlreadyExtracted_SkipsInsertionAndReenqueuesDedup()
    {
        await using var sqlDb = BuildSqlDb();
        await using var pgDb  = BuildPgDb();
        SeedDocument(sqlDb);
        pgDb.ExtractedClinicalFields.Add(new ExtractedClinicalField
        {
            PatientId       = 2,
            DocumentId      = 1,
            FieldType       = ClinicalFieldType.VitalSign,
            FieldName       = "VitalSign",
            FieldValue      = "BP: 120/80",
            ExtractionJobId = "existing-job"
        });
        await pgDb.SaveChangesAsync();

        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(sqlDb, pgDb, jobs);

        await job.ExecuteAsync(1, null!);

        Assert.Equal(1, await pgDb.ExtractedClinicalFields.CountAsync());
        jobs.Verify(j => j.Create(
            It.Is<Job>(j2 => j2.Type == typeof(DeduplicateClinicalFieldsJob)),
            It.IsAny<IState>()), Times.Once);
    }

    // ── TC-009: ClinicalFieldExtractor extracts VitalSign field ──────────────

    [Fact]
    public void Extractor_VitalSignText_ReturnsVitalSignField()
    {
        var extractor = new ClinicalFieldExtractor(NullLogger<ClinicalFieldExtractor>.Instance);

        var fields = extractor.Extract("BP: 120/80\nHR: 72 bpm");

        Assert.Contains(fields, f => f.FieldType == ClinicalFieldType.VitalSign);
    }

    // ── TC-010: ClinicalFieldExtractor extracts Medication field ─────────────

    [Fact]
    public void Extractor_MedicationText_ReturnsMedicationField()
    {
        var extractor = new ClinicalFieldExtractor(NullLogger<ClinicalFieldExtractor>.Instance);

        var fields = extractor.Extract("Aspirin 100mg daily");

        Assert.Contains(fields, f => f.FieldType == ClinicalFieldType.Medication);
    }

    // ── TC-011: ClinicalFieldExtractor extracts Allergy field ────────────────

    [Fact]
    public void Extractor_AllergyText_ReturnsAllergyField()
    {
        var extractor = new ClinicalFieldExtractor(NullLogger<ClinicalFieldExtractor>.Instance);

        var fields = extractor.Extract("Allergy to penicillin");

        Assert.Contains(fields, f => f.FieldType == ClinicalFieldType.Allergy);
    }

    // ── TC-012: ClinicalFieldExtractor extracts Diagnosis field ──────────────

    [Fact]
    public void Extractor_DiagnosisText_ReturnsDiagnosisField()
    {
        var extractor = new ClinicalFieldExtractor(NullLogger<ClinicalFieldExtractor>.Instance);

        var fields = extractor.Extract("Diagnosis: Hypertension");

        Assert.Contains(fields, f => f.FieldType == ClinicalFieldType.Diagnosis);
    }

    // ── TC-013: ClinicalFieldExtractor returns empty list for unrecognised text ──

    [Fact]
    public void Extractor_UnrecognisedText_ReturnsEmptyList()
    {
        var extractor = new ClinicalFieldExtractor(NullLogger<ClinicalFieldExtractor>.Instance);

        var fields = extractor.Extract("Lorem ipsum dolor sit amet");

        Assert.Empty(fields);
    }

    // ── EC-001: ClinicalFieldExtractor handles null and whitespace input ──────

    [Fact]
    public void Extractor_NullOrWhitespaceInput_ReturnsEmptyListWithoutException()
    {
        var extractor = new ClinicalFieldExtractor(NullLogger<ClinicalFieldExtractor>.Instance);

        Assert.Empty(extractor.Extract(null!));
        Assert.Empty(extractor.Extract("  "));
    }

    // ── EC-002: Zero extracted fields → dedup still enqueued [SOURCE:INFERRED] ──

    [Fact]
    public async Task ExtractJob_ZeroExtractedFields_StillEnqueuesDedup()
    {
        await using var sqlDb = BuildSqlDb();
        await using var pgDb  = BuildPgDb();
        SeedDocument(sqlDb, rawText: "Lorem ipsum dolor sit amet");
        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(sqlDb, pgDb, jobs);

        await job.ExecuteAsync(1, null!);

        Assert.Equal(0, await pgDb.ExtractedClinicalFields.CountAsync());
        jobs.Verify(j => j.Create(
            It.Is<Job>(j2 => j2.Type == typeof(DeduplicateClinicalFieldsJob)),
            It.IsAny<IState>()), Times.Once);
    }
}
