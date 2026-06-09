using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Jobs;
using ClinicalHealthcare.Infrastructure.NLP;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Jobs;

public sealed class ExtractClinicalFieldsJobTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateSqlDb()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(opts);
    }

    private static ClinicalDbContext CreatePgDb()
    {
        var opts = new DbContextOptionsBuilder<ClinicalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ClinicalDbContext(opts);
    }

    private static ClinicalFieldExtractor CreateExtractor() =>
        new(NullLogger<ClinicalFieldExtractor>.Instance);

    private static ExtractClinicalFieldsJob CreateJob(
        ApplicationDbContext sqlDb,
        ClinicalDbContext    pgDb,
        IBackgroundJobClient jobClient)
        => new(
            sqlDb,
            pgDb,
            CreateExtractor(),
            jobClient,
            NullLogger<ExtractClinicalFieldsJob>.Instance);

    private static Mock<IBackgroundJobClient> MockJobs() => new(MockBehavior.Loose);

    // ── DocumentNotFound ───────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_DocumentNotFound_ExitsWithoutInsert()
    {
        var sql  = CreateSqlDb();
        var pg   = CreatePgDb();
        var jobs = MockJobs();
        var sut  = CreateJob(sql, pg, jobs.Object);

        await sut.ExecuteAsync(999, null!);

        Assert.Empty(await pg.ExtractedClinicalFields.ToListAsync());
    }

    // ── NoData status ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_OcrStatusNoData_SkipsInsertion()
    {
        var sql = CreateSqlDb();
        var pg  = CreatePgDb();

        var doc = new ClinicalDocument
        {
            Id              = 1,
            PatientId       = 10,
            OcrStatus       = OcrStatus.NoData,
            RawOcrText      = "some text",
            OriginalFileName = "test.pdf",
            EncryptedBlobPath = "path/to/file"
        };
        sql.ClinicalDocuments.Add(doc);
        await sql.SaveChangesAsync();

        var jobs = MockJobs();
        var sut  = CreateJob(sql, pg, jobs.Object);

        await sut.ExecuteAsync(1, null!);

        Assert.Empty(await pg.ExtractedClinicalFields.ToListAsync());
    }

    // ── Null/empty OCR text ────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NullOcrText_SkipsInsertion()
    {
        var sql = CreateSqlDb();
        var pg  = CreatePgDb();

        var doc = new ClinicalDocument
        {
            Id               = 2,
            PatientId        = 10,
            OcrStatus        = OcrStatus.Extracted,
            RawOcrText       = null,
            OriginalFileName = "test.pdf",
            EncryptedBlobPath = "path/to/file"
        };
        sql.ClinicalDocuments.Add(doc);
        await sql.SaveChangesAsync();

        var jobs = MockJobs();
        var sut  = CreateJob(sql, pg, jobs.Object);

        await sut.ExecuteAsync(2, null!);

        Assert.Empty(await pg.ExtractedClinicalFields.ToListAsync());
    }

    // ── OCR Extracted — inserts rows ───────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_OcrStatusExtracted_InsertsFieldRows()
    {
        var sql = CreateSqlDb();
        var pg  = CreatePgDb();

        var doc = new ClinicalDocument
        {
            Id               = 3,
            PatientId        = 20,
            OcrStatus        = OcrStatus.Extracted,
            RawOcrText       = "BP: 120/80 mmHg\nDiagnosis: Hypertension",
            OriginalFileName = "test.pdf",
            EncryptedBlobPath = "path/to/file"
        };
        sql.ClinicalDocuments.Add(doc);
        await sql.SaveChangesAsync();

        var jobs = MockJobs();
        var sut  = CreateJob(sql, pg, jobs.Object);

        await sut.ExecuteAsync(3, null!);

        var saved = await pg.ExtractedClinicalFields.ToListAsync();
        Assert.Equal(2, saved.Count);
        Assert.All(saved, f => Assert.Equal(20, f.PatientId));
        Assert.All(saved, f => Assert.Equal(3, f.DocumentId));
    }

    // ── Confidence propagation ─────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_OcrStatusExtracted_PropagatesHighConfidence()
    {
        var sql = CreateSqlDb();
        var pg  = CreatePgDb();

        var doc = new ClinicalDocument
        {
            Id               = 4,
            PatientId        = 30,
            OcrStatus        = OcrStatus.Extracted,
            RawOcrText       = "BP: 130/85",
            OriginalFileName = "test.pdf",
            EncryptedBlobPath = "path/to/file"
        };
        sql.ClinicalDocuments.Add(doc);
        await sql.SaveChangesAsync();

        var jobs = MockJobs();
        var sut  = CreateJob(sql, pg, jobs.Object);

        await sut.ExecuteAsync(4, null!);

        var saved = await pg.ExtractedClinicalFields.FirstAsync();
        Assert.InRange(saved.ConfidenceScore, 0.80, 1.0); // Extracted → 0.90 (AC-002)
    }

    [Fact]
    public async Task ExecuteAsync_OcrStatusLowConfidence_PropagatesLowConfidence()
    {
        var sql = CreateSqlDb();
        var pg  = CreatePgDb();

        var doc = new ClinicalDocument
        {
            Id               = 5,
            PatientId        = 40,
            OcrStatus        = OcrStatus.LowConfidence,
            RawOcrText       = "HR: 88 bpm",
            OriginalFileName = "test.pdf",
            EncryptedBlobPath = "path/to/file"
        };
        sql.ClinicalDocuments.Add(doc);
        await sql.SaveChangesAsync();

        var jobs = MockJobs();
        var sut  = CreateJob(sql, pg, jobs.Object);

        await sut.ExecuteAsync(5, null!);

        var saved = await pg.ExtractedClinicalFields.FirstAsync();
        Assert.InRange(saved.ConfidenceScore, 0.30, 0.79); // LowConfidence → 0.60 (AC-002)
    }

    // ── Deduplication job enqueue ──────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Always_EnqueuesDeduplicateClinicalFieldsJob()
    {
        var sql = CreateSqlDb();
        var pg  = CreatePgDb();

        var doc = new ClinicalDocument
        {
            Id               = 6,
            PatientId        = 50,
            OcrStatus        = OcrStatus.Extracted,
            RawOcrText       = "BP: 110/70",
            OriginalFileName = "test.pdf",
            EncryptedBlobPath = "path/to/file"
        };
        sql.ClinicalDocuments.Add(doc);
        await sql.SaveChangesAsync();

        var jobs = MockJobs();
        var sut  = CreateJob(sql, pg, jobs.Object);

        await sut.ExecuteAsync(6, null!);

        jobs.Verify(j => j.Create(
            It.Is<Job>(job => job.Type == typeof(DeduplicateClinicalFieldsJob)),
            It.IsAny<IState>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NoFieldsExtracted_StillEnqueuesDeduplicateJob()
    {
        var sql = CreateSqlDb();
        var pg  = CreatePgDb();

        var doc = new ClinicalDocument
        {
            Id               = 7,
            PatientId        = 60,
            OcrStatus        = OcrStatus.Extracted,
            RawOcrText       = "No clinical content here at all.",
            OriginalFileName = "test.pdf",
            EncryptedBlobPath = "path/to/file"
        };
        sql.ClinicalDocuments.Add(doc);
        await sql.SaveChangesAsync();

        var jobs = MockJobs();
        var sut  = CreateJob(sql, pg, jobs.Object);

        await sut.ExecuteAsync(7, null!);

        jobs.Verify(j => j.Create(
            It.Is<Job>(job => job.Type == typeof(DeduplicateClinicalFieldsJob)),
            It.IsAny<IState>()),
            Times.Once);
    }

    // ── ExtractionJobId consistency ────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MultipleFields_SameExtractionJobId()
    {
        var sql = CreateSqlDb();
        var pg  = CreatePgDb();

        var doc = new ClinicalDocument
        {
            Id               = 8,
            PatientId        = 70,
            OcrStatus        = OcrStatus.Extracted,
            RawOcrText       = "BP: 120/80\nMetformin 500mg daily\nDiagnosis: Hypertension",
            OriginalFileName = "test.pdf",
            EncryptedBlobPath = "path/to/file"
        };
        sql.ClinicalDocuments.Add(doc);
        await sql.SaveChangesAsync();

        var jobs = MockJobs();
        var sut  = CreateJob(sql, pg, jobs.Object);

        await sut.ExecuteAsync(8, null!);

        var saved  = await pg.ExtractedClinicalFields.ToListAsync();
        var jobIds = saved.Select(f => f.ExtractionJobId).Distinct().ToList();

        Assert.Single(jobIds); // all rows share the same ExtractionJobId within one run
    }

    // ── Idempotency guard ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_AlreadyExtracted_SkipsReInsertionAndEnqueuesDedup()
    {
        var sql = CreateSqlDb();
        var pg  = CreatePgDb();

        var doc = new ClinicalDocument
        {
            Id               = 9,
            PatientId        = 80,
            OcrStatus        = OcrStatus.Extracted,
            RawOcrText       = "BP: 120/80",
            OriginalFileName = "test.pdf",
            EncryptedBlobPath = "path/to/file"
        };
        sql.ClinicalDocuments.Add(doc);
        await sql.SaveChangesAsync();

        // Simulate a prior successful run: rows already exist in ClinicalDbContext.
        pg.ExtractedClinicalFields.Add(new ExtractedClinicalField
        {
            PatientId       = 80,
            DocumentId      = 9,
            FieldType       = ClinicalFieldType.VitalSign,
            FieldName       = "VitalSign",
            FieldValue      = "BP: 120/80",
            ConfidenceScore = 0.90,
            ExtractionJobId = "prior-job-id",
        });
        await pg.SaveChangesAsync();

        var jobs = MockJobs();
        var sut  = CreateJob(sql, pg, jobs.Object);

        // Second execution (Hangfire retry scenario).
        await sut.ExecuteAsync(9, null!);

        // Row count must remain 1 — no duplicate inserted.
        var saved = await pg.ExtractedClinicalFields.ToListAsync();
        Assert.Single(saved);
        Assert.Equal("prior-job-id", saved[0].ExtractionJobId);

        // Dedup job must still be enqueued so downstream is unblocked.
        jobs.Verify(j => j.Create(
            It.Is<Job>(job => job.Type == typeof(DeduplicateClinicalFieldsJob)),
            It.IsAny<IState>()),
            Times.Once);
    }
}
