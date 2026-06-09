using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Jobs;
using ClinicalHealthcare.Infrastructure.OCR;
using ClinicalHealthcare.Infrastructure.Security;
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
/// Unit tests for <see cref="OcrDocumentJob.ExecuteAsync"/> (TASK_040 AC-001–AC-005).
/// </summary>
public sealed class OcrDocumentJobTests
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

    private static ClinicalDocument SeedDocument(ApplicationDbContext db, int id = 1, int patientId = 2)
    {
        var doc = new ClinicalDocument
        {
            Id                = id,
            PatientId         = patientId,
            EncryptedBlobPath = "enc/a.bin",
            UploadedByStaffId = 1,
            OriginalFileName  = "scan.pdf",
            OcrStatus         = OcrStatus.Pending
        };
        db.ClinicalDocuments.Add(doc);
        db.SaveChanges();
        return doc;
    }

    private static OcrDocumentJob BuildJob(
        ApplicationDbContext      db,
        Mock<ITesseractOcrService> ocr,
        Mock<IAesEncryptionService> aes,
        Mock<IBackgroundJobClient> jobs)
        => new(db, aes.Object, ocr.Object, jobs.Object, NullLogger<OcrDocumentJob>.Instance);

    // ── TC-001: High-confidence → Extracted; RawOcrText stored; job enqueued ──

    [Fact]
    public async Task ExecuteAsync_HighConfidence_SetsExtractedAndEnqueuesExtractionJob()
    {
        await using var db = BuildSqlDb();
        SeedDocument(db);

        var aes = new Mock<IAesEncryptionService>();
        aes.Setup(a => a.Decrypt(It.IsAny<string>())).Returns(new MemoryStream(new byte[64]));

        var ocr = new Mock<ITesseractOcrService>();
        ocr.Setup(o => o.OcrAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(("BP: 120/80\nAllergy to penicillin", 0.90f));

        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(db, ocr, aes, jobs);

        await job.ExecuteAsync(1, null!);

        var doc = await db.ClinicalDocuments.FirstAsync(d => d.Id == 1);
        Assert.Equal(OcrStatus.Extracted, doc.OcrStatus);
        Assert.Equal("BP: 120/80\nAllergy to penicillin", doc.RawOcrText);
        jobs.Verify(j => j.Create(
            It.Is<Job>(j2 => j2.Type == typeof(ExtractClinicalFieldsJob)),
            It.IsAny<IState>()), Times.Once);
    }

    // ── TC-002: Low-confidence → LowConfidence; RawOcrText stored; job enqueued (source behaviour) ──

    [Fact]
    public async Task ExecuteAsync_LowConfidence_SetsLowConfidenceAndEnqueuesExtractionJob()
    {
        await using var db = BuildSqlDb();
        SeedDocument(db);

        var aes = new Mock<IAesEncryptionService>();
        aes.Setup(a => a.Decrypt(It.IsAny<string>())).Returns(new MemoryStream(new byte[64]));

        var ocr = new Mock<ITesseractOcrService>();
        ocr.Setup(o => o.OcrAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(("partial text", 0.50f));

        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(db, ocr, aes, jobs);

        await job.ExecuteAsync(1, null!);

        var doc = await db.ClinicalDocuments.FirstAsync(d => d.Id == 1);
        Assert.Equal(OcrStatus.LowConfidence, doc.OcrStatus);
        Assert.Equal("partial text", doc.RawOcrText);
        jobs.Verify(j => j.Create(
            It.Is<Job>(j2 => j2.Type == typeof(ExtractClinicalFieldsJob)),
            It.IsAny<IState>()), Times.Once);
    }

    // ── TC-003: Empty OCR text → NoData; no extraction job enqueued ──────────

    [Fact]
    public async Task ExecuteAsync_EmptyOcrText_SetsNoDataAndDoesNotEnqueueExtractionJob()
    {
        await using var db = BuildSqlDb();
        SeedDocument(db);

        var aes = new Mock<IAesEncryptionService>();
        aes.Setup(a => a.Decrypt(It.IsAny<string>())).Returns(new MemoryStream(new byte[64]));

        var ocr = new Mock<ITesseractOcrService>();
        ocr.Setup(o => o.OcrAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(("", 0.0f));

        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(db, ocr, aes, jobs);

        await job.ExecuteAsync(1, null!);

        var doc = await db.ClinicalDocuments.FirstAsync(d => d.Id == 1);
        Assert.Equal(OcrStatus.NoData, doc.OcrStatus);
        jobs.Verify(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
    }

    // ── TC-004: Document not found → early return; no exception; DB unchanged ─

    [Fact]
    public async Task ExecuteAsync_DocumentNotFound_ReturnsWithoutException()
    {
        await using var db   = BuildSqlDb();
        var aes  = new Mock<IAesEncryptionService>();
        var ocr  = new Mock<ITesseractOcrService>();
        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(db, ocr, aes, jobs);

        await job.ExecuteAsync(9999, null!);

        Assert.Equal(0, await db.ClinicalDocuments.CountAsync());
    }

    // ── TC-005: OcrAsync throws → NoData saved; exception rethrown ───────────

    [Fact]
    public async Task ExecuteAsync_OcrAsyncThrows_SavesNoDataAndRethrows()
    {
        await using var db = BuildSqlDb();
        SeedDocument(db);

        var aes = new Mock<IAesEncryptionService>();
        aes.Setup(a => a.Decrypt(It.IsAny<string>())).Returns(new MemoryStream(new byte[64]));

        var ocr = new Mock<ITesseractOcrService>();
        ocr.Setup(o => o.OcrAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new InvalidOperationException("OCR failure"));

        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(db, ocr, aes, jobs);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ExecuteAsync(1, null!));

        var doc = await db.ClinicalDocuments.FirstAsync(d => d.Id == 1);
        Assert.Equal(OcrStatus.NoData, doc.OcrStatus);
    }

    // ── TC-006: Decrypt throws → NoData saved; exception rethrown ────────────

    [Fact]
    public async Task ExecuteAsync_DecryptThrows_SavesNoDataAndRethrows()
    {
        await using var db = BuildSqlDb();
        SeedDocument(db);

        var aes = new Mock<IAesEncryptionService>();
        aes.Setup(a => a.Decrypt(It.IsAny<string>())).Throws(new InvalidOperationException("decrypt fail"));

        var ocr  = new Mock<ITesseractOcrService>();
        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(db, ocr, aes, jobs);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ExecuteAsync(1, null!));

        var doc = await db.ClinicalDocuments.FirstAsync(d => d.Id == 1);
        Assert.Equal(OcrStatus.NoData, doc.OcrStatus);
    }

    // ── EC-001: Confidence exactly 0.75 → Extracted (inclusive lower bound) ──

    [Fact]
    public async Task ExecuteAsync_ConfidenceExactly075_SetsExtracted()
    {
        await using var db = BuildSqlDb();
        SeedDocument(db);

        var aes = new Mock<IAesEncryptionService>();
        aes.Setup(a => a.Decrypt(It.IsAny<string>())).Returns(new MemoryStream(new byte[64]));

        var ocr = new Mock<ITesseractOcrService>();
        ocr.Setup(o => o.OcrAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(("text", 0.75f));

        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(db, ocr, aes, jobs);

        await job.ExecuteAsync(1, null!);

        var doc = await db.ClinicalDocuments.FirstAsync(d => d.Id == 1);
        Assert.Equal(OcrStatus.Extracted, doc.OcrStatus);
    }

    // ── EC-002: Confidence 0.749 → LowConfidence ─────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ConfidenceJustBelow075_SetsLowConfidence()
    {
        await using var db = BuildSqlDb();
        SeedDocument(db);

        var aes = new Mock<IAesEncryptionService>();
        aes.Setup(a => a.Decrypt(It.IsAny<string>())).Returns(new MemoryStream(new byte[64]));

        var ocr = new Mock<ITesseractOcrService>();
        ocr.Setup(o => o.OcrAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(("text", 0.749f));

        var jobs = new Mock<IBackgroundJobClient>();
        var job  = BuildJob(db, ocr, aes, jobs);

        await job.ExecuteAsync(1, null!);

        var doc = await db.ClinicalDocuments.FirstAsync(d => d.Id == 1);
        Assert.Equal(OcrStatus.LowConfidence, doc.OcrStatus);
    }
}
