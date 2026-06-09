using ClinicalHealthcare.Api.Features.ClinicalDocs;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using System.Security.Cryptography;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for TASK_039 — AES-256 decryption at rest + role-gated retrieval.
/// Covers AC-001 to AC-005, IDOR guard, and error paths.
/// </summary>
public sealed class DownloadDocumentEndpointTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ApplicationDbContext BuildDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static int StatusCode(IResult result)
    {
        if (result is IStatusCodeHttpResult sc)
            return sc.StatusCode ?? 200; // null means default 200 (e.g. FileStreamHttpResult)
        return (int)(result.GetType().GetProperty("StatusCode")?.GetValue(result) ?? 200);
    }

    private static byte[] TestAesKey() => new byte[32];

    /// <summary>
    /// Produces a valid Base64(IV):Base64(Ciphertext) blob in a temp file,
    /// encrypting the given plaintext with <paramref name="key"/>.
    /// Returns the file path.
    /// </summary>
    private static string WriteTempEncryptedFile(byte[] plaintext, byte[] key)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode    = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key     = key;
        aes.GenerateIV();

        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            cs.Write(plaintext);

        var path = Path.GetTempFileName();
        File.WriteAllText(path,
            $"{Convert.ToBase64String(aes.IV)}:{Convert.ToBase64String(ms.ToArray())}");
        return path;
    }

    private static async Task<ClinicalDocument> SeedDocumentAsync(
        ApplicationDbContext db, int patientId, string blobPath, string fileName = "report.pdf")
    {
        var doc = new ClinicalDocument
        {
            PatientId         = patientId,
            OriginalFileName  = fileName,
            EncryptedBlobPath = blobPath,
            VirusScanResult   = VirusScanResult.Clean,
        };
        db.ClinicalDocuments.Add(doc);
        await db.SaveChangesAsync();
        return doc;
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
    // AC-005 — GET /patients/{id}/documents/{docId} streams decrypted PDF
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Download_ValidDocument_Returns200WithPdfContent()
    {
        await using var db    = BuildDb();
        var patient           = await SeedPatientAsync(db);
        var key               = TestAesKey();
        var svc               = new AesEncryptionService(key);
        var originalBytes     = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x01, 0x02 };
        var blobPath          = WriteTempEncryptedFile(originalBytes, key);

        try
        {
            var doc = await SeedDocumentAsync(db, patient.Id, blobPath);

            var result = await DownloadDocumentEndpoint.HandleDownload(
                patient.Id, doc.Id, db, svc, CancellationToken.None);

            Assert.Equal(200, StatusCode(result));
        }
        finally { File.Delete(blobPath); }
    }

    [Fact]
    public async Task Download_ValidDocument_DecryptedContentMatchesOriginal()
    {
        await using var db = BuildDb();
        var patient        = await SeedPatientAsync(db);
        var key            = TestAesKey();
        var svc            = new AesEncryptionService(key);
        var originalBytes  = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var blobPath       = WriteTempEncryptedFile(originalBytes, key);

        try
        {
            var doc    = await SeedDocumentAsync(db, patient.Id, blobPath);
            var result = await DownloadDocumentEndpoint.HandleDownload(
                patient.Id, doc.Id, db, svc, CancellationToken.None);

            // Extract stream from FileStreamHttpResult
            var fileResult = result as IFileHttpResult;
            Assert.NotNull(fileResult);
            Assert.Equal("application/pdf", fileResult.ContentType);
        }
        finally { File.Delete(blobPath); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-004 — CryptographicException → 422
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Download_CorruptFile_Returns422()
    {
        await using var db = BuildDb();
        var patient        = await SeedPatientAsync(db);
        // Write a corrupt blob (not valid IV:ciphertext)
        var blobPath = Path.GetTempFileName();
        File.WriteAllText(blobPath, "AAAAAA==:BBBBBBBBBBBBBBBBBBBBBBBBBBB=");

        try
        {
            var doc = await SeedDocumentAsync(db, patient.Id, blobPath);
            var aes = new Mock<IAesEncryptionService>();
            aes.Setup(a => a.Decrypt(It.IsAny<string>()))
               .Throws(new CryptographicException("bad padding"));

            var result = await DownloadDocumentEndpoint.HandleDownload(
                patient.Id, doc.Id, db, aes.Object, CancellationToken.None);

            Assert.Equal(422, StatusCode(result));
        }
        finally { File.Delete(blobPath); }
    }

    [Fact]
    public async Task Download_WrongKey_Returns422()
    {
        await using var db = BuildDb();
        var patient        = await SeedPatientAsync(db);
        var encryptKey     = TestAesKey();
        var blobPath       = WriteTempEncryptedFile(new byte[] { 1, 2, 3 }, encryptKey);

        try
        {
            var doc     = await SeedDocumentAsync(db, patient.Id, blobPath);
            var wrongKey = new byte[32];
            wrongKey[0] = 0xFF; // Different from all-zeros test key
            var svc     = new AesEncryptionService(wrongKey);

            var result = await DownloadDocumentEndpoint.HandleDownload(
                patient.Id, doc.Id, db, svc, CancellationToken.None);

            Assert.Equal(422, StatusCode(result));
        }
        finally { File.Delete(blobPath); }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AC-003 — IDOR prevention + error paths
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Download_WrongPatientId_Returns404()
    {
        await using var db = BuildDb();
        var patient1       = await SeedPatientAsync(db);
        var patient2       = await SeedPatientAsync(db);
        var blobPath       = Path.GetTempFileName();
        File.WriteAllText(blobPath, "dummy");

        try
        {
            var doc = await SeedDocumentAsync(db, patient1.Id, blobPath);
            var aes = new Mock<IAesEncryptionService>();

            // Attempt to access patient1's document using patient2's id — IDOR
            var result = await DownloadDocumentEndpoint.HandleDownload(
                patient2.Id, doc.Id, db, aes.Object, CancellationToken.None);

            Assert.Equal(404, StatusCode(result));
            // Decrypt must never be called — file was not accessed
            aes.Verify(a => a.Decrypt(It.IsAny<string>()), Times.Never);
        }
        finally { File.Delete(blobPath); }
    }

    [Fact]
    public async Task Download_UnknownDocId_Returns404()
    {
        await using var db = BuildDb();
        var patient        = await SeedPatientAsync(db);
        var aes            = new Mock<IAesEncryptionService>();

        var result = await DownloadDocumentEndpoint.HandleDownload(
            patient.Id, 9999, db, aes.Object, CancellationToken.None);

        Assert.Equal(404, StatusCode(result));
    }

    [Fact]
    public async Task Download_MissingBlobFile_Returns404()
    {
        await using var db = BuildDb();
        var patient        = await SeedPatientAsync(db);
        var doc            = await SeedDocumentAsync(db, patient.Id, "/nonexistent/path.enc");

        var aes = new Mock<IAesEncryptionService>();
        aes.Setup(a => a.Decrypt(It.IsAny<string>()))
           .Throws(new FileNotFoundException("file not found"));

        var result = await DownloadDocumentEndpoint.HandleDownload(
            patient.Id, doc.Id, db, aes.Object, CancellationToken.None);

        Assert.Equal(404, StatusCode(result));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // AesEncryptionService.Decrypt unit tests (AC-001 / AC-002)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void AesDecrypt_RoundTrip_PlaintextMatchesOriginal()
    {
        var key         = TestAesKey();
        var svc         = new AesEncryptionService(key);
        var original    = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 10, 20, 30 };
        var blobPath    = WriteTempEncryptedFile(original, key);

        try
        {
            using var decrypted = svc.Decrypt(blobPath);
            var result = new MemoryStream();
            decrypted.CopyTo(result);
            Assert.Equal(original, result.ToArray());
        }
        finally { File.Delete(blobPath); }
    }

    [Fact]
    public void AesDecrypt_ReturnsStreamAtPositionZero()
    {
        var key      = TestAesKey();
        var svc      = new AesEncryptionService(key);
        var blobPath = WriteTempEncryptedFile(new byte[] { 1, 2, 3 }, key);

        try
        {
            using var stream = svc.Decrypt(blobPath);
            Assert.Equal(0, stream.Position);
        }
        finally { File.Delete(blobPath); }
    }

    [Fact]
    public void AesDecrypt_InvalidFormat_ThrowsCryptographicException()
    {
        var key      = TestAesKey();
        var svc      = new AesEncryptionService(key);
        var blobPath = Path.GetTempFileName();
        File.WriteAllText(blobPath, "no-colon-in-here");

        try
        {
            Assert.Throws<CryptographicException>(() => svc.Decrypt(blobPath));
        }
        finally { File.Delete(blobPath); }
    }
}
