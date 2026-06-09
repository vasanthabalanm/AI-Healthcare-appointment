using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Jobs;
using ClinicalHealthcare.Infrastructure.OCR;
using ClinicalHealthcare.Infrastructure.Security;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Jobs;

/// <summary>
/// Unit tests for TASK_040 — OcrDocumentJob full pipeline.
/// Covers AC-001 to AC-005, OcrStatus logic, and error paths.
/// All Tesseract calls are mocked via <see cref="ITesseractOcrService"/>.
/// </summary>
public sealed class OcrDocumentJobTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ApplicationDbContext BuildDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static async Task<ClinicalDocument> SeedDocumentAsync(ApplicationDbContext db)
    {
        var patient = new UserAccount
        {
            Email = $"p-{Guid.NewGuid()}@t.com", PasswordHash = "",
            Role = "patient", FirstName = "P", LastName = "L"
        };
        db.UserAccounts.Add(patient);
        await db.SaveChangesAsync();

        var doc = new ClinicalDocument
        {
            PatientId         = patient.Id,
            OriginalFileName  = "report.pdf",
            EncryptedBlobPath = "/tmp/dummy.enc",
            VirusScanResult   = VirusScanResult.Clean,
        };
        db.ClinicalDocuments.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    private static OcrDocumentJob BuildJob(
        ApplicationDbContext db,
        IAesEncryptionService aes,
        ITesseractOcrService ocr)
        => new(db, aes, ocr, new Mock<IBackgroundJobClient>(MockBehavior.Loose).Object, NullLogger<OcrDocumentJob>.Instance);

    private static Mock<IAesEncryptionService> AesMock()
    {
        var m = new Mock<IAesEncryptionService>();
        m.Setup(a => a.Decrypt(It.IsAny<string>()))
         .Returns(new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 }));
        return m;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-002 — OcrStatus mapping from confidence
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_HighConfidence_SetsExtracted()
    {
        await using var db = BuildDb();
        var doc  = await SeedDocumentAsync(db);
        var aes  = AesMock();
        var ocr  = new Mock<ITesseractOcrService>();
        ocr.Setup(o => o.OcrAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(("Patient: John Doe", 0.85f));

        await BuildJob(db, aes.Object, ocr.Object)
            .ExecuteAsync(doc.Id, JobCancellationToken.Null);

        var saved = await db.ClinicalDocuments.FindAsync(doc.Id);
        Assert.Equal(OcrStatus.Extracted, saved!.OcrStatus);
    }

    [Fact]
    public async Task Execute_HighConfidenceAtBoundary_SetsExtracted()
    {
        await using var db = BuildDb();
        var doc  = await SeedDocumentAsync(db);
        var aes  = AesMock();
        var ocr  = new Mock<ITesseractOcrService>();
        ocr.Setup(o => o.OcrAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(("some text", 0.75f)); // Exactly at threshold → Extracted

        await BuildJob(db, aes.Object, ocr.Object)
            .ExecuteAsync(doc.Id, JobCancellationToken.Null);

        var saved = await db.ClinicalDocuments.FindAsync(doc.Id);
        Assert.Equal(OcrStatus.Extracted, saved!.OcrStatus);
    }

    [Fact]
    public async Task Execute_LowConfidence_SetsLowConfidence()
    {
        await using var db = BuildDb();
        var doc  = await SeedDocumentAsync(db);
        var aes  = AesMock();
        var ocr  = new Mock<ITesseractOcrService>();
        ocr.Setup(o => o.OcrAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(("partial text", 0.60f));

        await BuildJob(db, aes.Object, ocr.Object)
            .ExecuteAsync(doc.Id, JobCancellationToken.Null);

        var saved = await db.ClinicalDocuments.FindAsync(doc.Id);
        Assert.Equal(OcrStatus.LowConfidence, saved!.OcrStatus);
    }

    [Fact]
    public async Task Execute_EmptyText_SetsNoData()
    {
        await using var db = BuildDb();
        var doc  = await SeedDocumentAsync(db);
        var aes  = AesMock();
        var ocr  = new Mock<ITesseractOcrService>();
        ocr.Setup(o => o.OcrAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((string.Empty, 0.0f));

        await BuildJob(db, aes.Object, ocr.Object)
            .ExecuteAsync(doc.Id, JobCancellationToken.Null);

        var saved = await db.ClinicalDocuments.FindAsync(doc.Id);
        Assert.Equal(OcrStatus.NoData, saved!.OcrStatus);
    }

    [Fact]
    public async Task Execute_WhitespaceOnlyText_SetsNoData()
    {
        await using var db = BuildDb();
        var doc  = await SeedDocumentAsync(db);
        var aes  = AesMock();
        var ocr  = new Mock<ITesseractOcrService>();
        ocr.Setup(o => o.OcrAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(("   \n  ", 0.0f));

        await BuildJob(db, aes.Object, ocr.Object)
            .ExecuteAsync(doc.Id, JobCancellationToken.Null);

        var saved = await db.ClinicalDocuments.FindAsync(doc.Id);
        Assert.Equal(OcrStatus.NoData, saved!.OcrStatus);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-004 — RawOcrText stored
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_HighConfidence_StoresRawOcrText()
    {
        await using var db = BuildDb();
        var doc  = await SeedDocumentAsync(db);
        var aes  = AesMock();
        var ocr  = new Mock<ITesseractOcrService>();
        var expected = "Patient: Jane Smith\nDOB: 1980-04-12";
        ocr.Setup(o => o.OcrAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((expected, 0.82f));

        await BuildJob(db, aes.Object, ocr.Object)
            .ExecuteAsync(doc.Id, JobCancellationToken.Null);

        var saved = await db.ClinicalDocuments.FindAsync(doc.Id);
        Assert.Equal(expected, saved!.RawOcrText);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-003 — Multi-page: confidence determined by average (via mock)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_MultiPageAverageBelow075_SetsLowConfidence()
    {
        // Average of 0.80 + 0.60 = 0.70 → below threshold → LowConfidence
        await using var db = BuildDb();
        var doc  = await SeedDocumentAsync(db);
        var aes  = AesMock();
        var ocr  = new Mock<ITesseractOcrService>();
        ocr.Setup(o => o.OcrAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(("page1 text\npage2 text", 0.70f));

        await BuildJob(db, aes.Object, ocr.Object)
            .ExecuteAsync(doc.Id, JobCancellationToken.Null);

        var saved = await db.ClinicalDocuments.FindAsync(doc.Id);
        Assert.Equal(OcrStatus.LowConfidence, saved!.OcrStatus);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-005 — Error handling: exception → NoData + rethrow
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_OcrThrows_SetsNoDataAndRethrows()
    {
        await using var db = BuildDb();
        var doc  = await SeedDocumentAsync(db);
        var aes  = AesMock();
        var ocr  = new Mock<ITesseractOcrService>();
        ocr.Setup(o => o.OcrAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new InvalidOperationException("Tesseract P/Invoke failed"));

        var job = BuildJob(db, aes.Object, ocr.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => job.ExecuteAsync(doc.Id, JobCancellationToken.Null));

        // OcrStatus must be persisted as NoData before the rethrow.
        var saved = await db.ClinicalDocuments.FindAsync(doc.Id);
        Assert.Equal(OcrStatus.NoData, saved!.OcrStatus);
    }

    [Fact]
    public async Task Execute_AesDecryptThrows_SetsNoDataAndRethrows()
    {
        await using var db = BuildDb();
        var doc  = await SeedDocumentAsync(db);
        var aes  = new Mock<IAesEncryptionService>();
        aes.Setup(a => a.Decrypt(It.IsAny<string>()))
           .Throws(new System.Security.Cryptography.CryptographicException("bad key"));
        var ocr  = new Mock<ITesseractOcrService>();

        var job = BuildJob(db, aes.Object, ocr.Object);

        await Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(
            () => job.ExecuteAsync(doc.Id, JobCancellationToken.Null));

        var saved = await db.ClinicalDocuments.FindAsync(doc.Id);
        Assert.Equal(OcrStatus.NoData, saved!.OcrStatus);
        Assert.Null(saved.RawOcrText);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Edge case — document not found
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_DocumentNotFound_ExitsWithoutOcrCall()
    {
        await using var db = BuildDb();
        var aes = AesMock();
        var ocr = new Mock<ITesseractOcrService>();

        // No document seeded — ID 9999 does not exist.
        await BuildJob(db, aes.Object, ocr.Object)
            .ExecuteAsync(9999, JobCancellationToken.Null);

        // OCR should never be called when the document is not found.
        ocr.Verify(
            o => o.OcrAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-001 — Decrypt is called with the correct blob path
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Execute_CallsAesDecryptWithDocumentBlobPath()
    {
        await using var db = BuildDb();
        var doc  = await SeedDocumentAsync(db);
        var aes  = AesMock();
        var ocr  = new Mock<ITesseractOcrService>();
        ocr.Setup(o => o.OcrAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync(("text", 0.9f));

        await BuildJob(db, aes.Object, ocr.Object)
            .ExecuteAsync(doc.Id, JobCancellationToken.Null);

        aes.Verify(a => a.Decrypt(doc.EncryptedBlobPath), Times.Once);
    }
}
