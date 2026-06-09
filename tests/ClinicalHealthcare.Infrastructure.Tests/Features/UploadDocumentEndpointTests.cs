using ClinicalHealthcare.Api.Features.ClinicalDocs;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Jobs;
using ClinicalHealthcare.Infrastructure.Security;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for TASK_038 — Clinical document upload + ClamAV virus scan.
/// Covers AC-001 to AC-005, edge cases, and error paths.
/// </summary>
public sealed class UploadDocumentEndpointTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ApplicationDbContext BuildDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static int StatusCode(IResult result)
    {
        if (result is IStatusCodeHttpResult sc && sc.StatusCode is not null)
            return sc.StatusCode.Value;
        return (int)(result.GetType().GetProperty("StatusCode")?.GetValue(result) ?? 0);
    }

    private static HttpContext BuildStaffContext(int staffId = 1)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, staffId.ToString())],
            "TestAuth"));
        return ctx;
    }

    /// <summary>Creates a minimal valid PDF IFormFile (magic bytes %PDF-).</summary>
    private static IFormFile BuildPdfFormFile(long sizeBytes = 1024, string contentType = "application/pdf",
        bool validMagic = true)
    {
        var bytes = new byte[sizeBytes];
        if (validMagic)
        {
            bytes[0] = 0x25; bytes[1] = 0x50; bytes[2] = 0x44;
            bytes[3] = 0x46; bytes[4] = 0x2D; // %PDF-
        }
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", "test.pdf")
        {
            Headers     = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static byte[] TestAesKey() => new byte[32]; // 256-bit zero key — only for tests

    private static (Mock<IClamAvScanService> clamAv, Mock<IAesEncryptionService> aes, Mock<IBackgroundJobClient> jobs)
        CreateMocks(ClamAvScanResult scanResult = ClamAvScanResult.Clean)
    {
        var clamAv = new Mock<IClamAvScanService>();
        clamAv.Setup(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(scanResult);

        var aes = new Mock<IAesEncryptionService>();
        aes.Setup(a => a.Encrypt(It.IsAny<Stream>()))
           .Returns((new byte[16], new byte[16])); // fake ciphertext + IV

        var jobs = new Mock<IBackgroundJobClient>();
        jobs.Setup(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()))
            .Returns(Guid.NewGuid().ToString());

        return (clamAv, aes, jobs);
    }

    private static async Task<UserAccount> SeedPatientAsync(ApplicationDbContext db)
    {
        var patient = new UserAccount
        {
            Email = $"p-{Guid.NewGuid()}@t.com", PasswordHash = "",
            Role = "patient", FirstName = "P", LastName = "L"
        };
        db.UserAccounts.Add(patient);
        await db.SaveChangesAsync();
        return patient;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-001 — PDF + ≤10MB validation
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Upload_NonPdfMimeType_Returns400()
    {
        await using var db = BuildDb();
        var patient = await SeedPatientAsync(db);
        var (clamAv, aes, jobs) = CreateMocks();
        var file = BuildPdfFormFile(contentType: "image/jpeg");
        var ctx  = BuildStaffContext(1);

        var result = await UploadDocumentEndpoint.HandleUpload(
            patient.Id, file, ctx, db, clamAv.Object, aes.Object, jobs.Object, Mock.Of<ClinicalHealthcare.Infrastructure.Cache.ICacheService>(), CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    [Fact]
    public async Task Upload_FileTooLarge_Returns400()
    {
        await using var db = BuildDb();
        var patient = await SeedPatientAsync(db);
        var (clamAv, aes, jobs) = CreateMocks();
        // Simulate a file > 10MB by faking the FormFile Length property
        var bigFile = new Mock<IFormFile>();
        bigFile.Setup(f => f.ContentType).Returns("application/pdf");
        bigFile.Setup(f => f.Length).Returns(11L * 1024 * 1024); // 11 MB
        var ctx = BuildStaffContext(1);

        var result = await UploadDocumentEndpoint.HandleUpload(
            patient.Id, bigFile.Object, ctx, db, clamAv.Object, aes.Object, jobs.Object, Mock.Of<ClinicalHealthcare.Infrastructure.Cache.ICacheService>(), CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    [Fact]
    public async Task Upload_InvalidMagicBytes_Returns400()
    {
        await using var db = BuildDb();
        var patient = await SeedPatientAsync(db);
        var (clamAv, aes, jobs) = CreateMocks();
        var file = BuildPdfFormFile(validMagic: false); // no %PDF- header
        var ctx  = BuildStaffContext(1);

        var result = await UploadDocumentEndpoint.HandleUpload(
            patient.Id, file, ctx, db, clamAv.Object, aes.Object, jobs.Object, Mock.Of<ClinicalHealthcare.Infrastructure.Cache.ICacheService>(), CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-002 — ClamAV scan: infected → 422; unavailable → 503
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Upload_InfectedFile_Returns422()
    {
        await using var db = BuildDb();
        var patient = await SeedPatientAsync(db);
        var (clamAv, aes, jobs) = CreateMocks(ClamAvScanResult.Infected);
        var file = BuildPdfFormFile();
        var ctx  = BuildStaffContext(1);

        var result = await UploadDocumentEndpoint.HandleUpload(
            patient.Id, file, ctx, db, clamAv.Object, aes.Object, jobs.Object, Mock.Of<ClinicalHealthcare.Infrastructure.Cache.ICacheService>(), CancellationToken.None);

        Assert.Equal(422, StatusCode(result));
    }

    [Fact]
    public async Task Upload_ClamAvUnavailable_Returns503()
    {
        await using var db = BuildDb();
        var patient = await SeedPatientAsync(db);

        var clamAv = new Mock<IClamAvScanService>();
        clamAv.Setup(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new ClamAvUnavailableException("daemon down"));
        var aes  = new Mock<IAesEncryptionService>();
        var jobs = new Mock<IBackgroundJobClient>();
        var file = BuildPdfFormFile();
        var ctx  = BuildStaffContext(1);

        var result = await UploadDocumentEndpoint.HandleUpload(
            patient.Id, file, ctx, db, clamAv.Object, aes.Object, jobs.Object, Mock.Of<ClinicalHealthcare.Infrastructure.Cache.ICacheService>(), CancellationToken.None);

        Assert.Equal(503, StatusCode(result));
    }

    [Fact]
    public async Task Upload_ClamAvUnavailable_NoDocumentRowCreated()
    {
        await using var db = BuildDb();
        var patient = await SeedPatientAsync(db);

        var clamAv = new Mock<IClamAvScanService>();
        clamAv.Setup(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new ClamAvUnavailableException("daemon down"));
        var aes  = new Mock<IAesEncryptionService>();
        var jobs = new Mock<IBackgroundJobClient>();
        var file = BuildPdfFormFile();
        var ctx  = BuildStaffContext(1);

        await UploadDocumentEndpoint.HandleUpload(
            patient.Id, file, ctx, db, clamAv.Object, aes.Object, jobs.Object, Mock.Of<ClinicalHealthcare.Infrastructure.Cache.ICacheService>(), CancellationToken.None);

        // AC-002: no DB row created when ClamAV is unavailable
        Assert.Equal(0, await db.ClinicalDocuments.CountAsync());
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-003 + AC-004 — Encrypt + ClinicalDocument row inserted
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Upload_ValidPdf_Returns201AndPersistsDocumentRow()
    {
        await using var db = BuildDb();
        var patient = await SeedPatientAsync(db);
        var (clamAv, aes, jobs) = CreateMocks();
        var file = BuildPdfFormFile();
        var ctx  = BuildStaffContext(42);

        var result = await UploadDocumentEndpoint.HandleUpload(
            patient.Id, file, ctx, db, clamAv.Object, aes.Object, jobs.Object, Mock.Of<ClinicalHealthcare.Infrastructure.Cache.ICacheService>(), CancellationToken.None);

        Assert.Equal(201, StatusCode(result));

        var doc = await db.ClinicalDocuments.FirstOrDefaultAsync(d => d.PatientId == patient.Id);
        Assert.NotNull(doc);
        Assert.Equal(VirusScanResult.Clean, doc.VirusScanResult);
        Assert.Equal(42, doc.UploadedByStaffId);
        Assert.Equal("test.pdf", doc.OriginalFileName);
        Assert.False(string.IsNullOrWhiteSpace(doc.EncryptedBlobPath));
    }

    [Fact]
    public async Task Upload_ValidPdf_CallsAesEncrypt()
    {
        await using var db = BuildDb();
        var patient = await SeedPatientAsync(db);
        var (clamAv, aes, jobs) = CreateMocks();
        var file = BuildPdfFormFile();
        var ctx  = BuildStaffContext(1);

        await UploadDocumentEndpoint.HandleUpload(
            patient.Id, file, ctx, db, clamAv.Object, aes.Object, jobs.Object, Mock.Of<ClinicalHealthcare.Infrastructure.Cache.ICacheService>(), CancellationToken.None);

        // AC-003: encryption service must be called exactly once
        aes.Verify(a => a.Encrypt(It.IsAny<Stream>()), Times.Once);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-005 — OcrDocumentJob enqueued after row insert
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Upload_ValidPdf_EnqueuesOcrDocumentJob()
    {
        await using var db = BuildDb();
        var patient = await SeedPatientAsync(db);
        var (clamAv, aes, jobs) = CreateMocks();
        var file = BuildPdfFormFile();
        var ctx  = BuildStaffContext(1);

        await UploadDocumentEndpoint.HandleUpload(
            patient.Id, file, ctx, db, clamAv.Object, aes.Object, jobs.Object, Mock.Of<ClinicalHealthcare.Infrastructure.Cache.ICacheService>(), CancellationToken.None);

        // AC-005: OcrDocumentJob must be enqueued once
        jobs.Verify(j => j.Create(
            It.Is<Job>(job => job.Type == typeof(OcrDocumentJob)),
            It.IsAny<IState>()),
            Times.Once);
    }

    [Fact]
    public async Task Upload_ValidPdf_InvalidatesView360Cache()
    {
        await using var db = BuildDb();
        var patient = await SeedPatientAsync(db);
        var (clamAv, aes, jobs) = CreateMocks();
        var file  = BuildPdfFormFile();
        var ctx   = BuildStaffContext(1);
        var cache = new Mock<ClinicalHealthcare.Infrastructure.Cache.ICacheService>(MockBehavior.Loose);

        await UploadDocumentEndpoint.HandleUpload(
            patient.Id, file, ctx, db, clamAv.Object, aes.Object, jobs.Object, cache.Object, CancellationToken.None);

        // AC-005 (TASK_044): 360° view cache must be invalidated after successful upload.
        cache.Verify(c => c.DeleteAsync(
            $"view360:{patient.Id}",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AesEncryptionService unit tests
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AesEncryptionService_Encrypt_ProducesNonEmptyCiphertext()
    {
        var svc = new AesEncryptionService(TestAesKey());
        using var input = new MemoryStream([0x25, 0x50, 0x44, 0x46, 0x2D]);

        var (ciphertext, iv) = svc.Encrypt(input);

        Assert.NotEmpty(ciphertext);
        Assert.Equal(16, iv.Length); // AES block size
    }

    [Fact]
    public void AesEncryptionService_Encrypt_DifferentIvEachCall()
    {
        var svc = new AesEncryptionService(TestAesKey());
        using var input1 = new MemoryStream([1, 2, 3, 4, 5]);
        using var input2 = new MemoryStream([1, 2, 3, 4, 5]);

        var (_, iv1) = svc.Encrypt(input1);
        var (_, iv2) = svc.Encrypt(input2);

        Assert.False(iv1.SequenceEqual(iv2), "IV must be randomised per call.");
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Error paths
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Upload_UnknownPatient_Returns404()
    {
        await using var db = BuildDb();
        var (clamAv, aes, jobs) = CreateMocks();
        var file = BuildPdfFormFile();
        var ctx  = BuildStaffContext(1);

        var result = await UploadDocumentEndpoint.HandleUpload(
            9999, file, ctx, db, clamAv.Object, aes.Object, jobs.Object, Mock.Of<ClinicalHealthcare.Infrastructure.Cache.ICacheService>(), CancellationToken.None);

        Assert.Equal(404, StatusCode(result));
    }

    [Fact]
    public async Task Upload_MissingSubClaim_Returns401()
    {
        await using var db = BuildDb();
        var patient = await SeedPatientAsync(db);
        var (clamAv, aes, jobs) = CreateMocks();
        var file = BuildPdfFormFile();
        var ctx  = new DefaultHttpContext(); // no sub claim

        var result = await UploadDocumentEndpoint.HandleUpload(
            patient.Id, file, ctx, db, clamAv.Object, aes.Object, jobs.Object, Mock.Of<ClinicalHealthcare.Infrastructure.Cache.ICacheService>(), CancellationToken.None);

        Assert.Equal(401, StatusCode(result));
    }
}

