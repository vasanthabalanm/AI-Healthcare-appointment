using System.Security.Cryptography;
using ClinicalHealthcare.Infrastructure.Security;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Security;

/// <summary>
/// Unit tests for TASK_018 (AES-256-CBC encryption layer):
///   TC-001 — Encrypt + Decrypt roundtrip returns identical plaintext (AC-004)
///   TC-002 — Two Encrypt calls produce different IVs (AC-004 fresh-IV requirement)
///   EC-001 — Test constructor rejects key length != 32 bytes (AC-004 key validation)
///
/// Static-scan tests (Program.cs HTTPS/HSTS, appsettings.json secret hygiene,
/// .gitignore exclusions) are covered by DeploymentConfigTests.cs (already exists).
/// JWT_SECRET short-secret validation is covered by JwtSessionTests.cs (already exists).
/// </summary>
public sealed class AesEncryptionServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Returns a random 32-byte key for use with the test constructor.</summary>
    private static byte[] RandomKey()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    /// <summary>
    /// Writes a <c>Base64(iv):Base64(ciphertext)</c> blob to a temp file and returns the path.
    /// Caller is responsible for deleting the file after the test completes.
    /// </summary>
    private static string WriteBlobFile(byte[] iv, byte[] ciphertext)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, Convert.ToBase64String(iv) + ":" + Convert.ToBase64String(ciphertext));
        return path;
    }

    // ── TC-001: Encrypt → Decrypt roundtrip ───────────────────────────────────

    [Fact]
    public void Encrypt_ThenDecrypt_ReturnsOriginalPlaintext()
    {
        // Arrange
        var svc       = new AesEncryptionService(RandomKey());
        var plaintext = "PHI:DOB=1990-01-01"u8.ToArray();

        // Act — Encrypt
        var (ciphertext, iv) = svc.Encrypt(new MemoryStream(plaintext));

        // Act — persist blob then Decrypt
        var blobPath = WriteBlobFile(iv, ciphertext);
        try
        {
            using var decryptedStream = svc.Decrypt(blobPath);
            using var resultMs        = new MemoryStream();
            decryptedStream.CopyTo(resultMs);

            // Assert
            Assert.True(
                resultMs.ToArray().SequenceEqual(plaintext),
                "Decrypted bytes must equal the original plaintext.");
        }
        finally
        {
            File.Delete(blobPath);
        }
    }

    // ── TC-002: Fresh IV per Encrypt call ─────────────────────────────────────

    [Fact]
    public void Encrypt_TwoCallsOnSameInput_ProduceDifferentIvs()
    {
        // Arrange
        var svc       = new AesEncryptionService(RandomKey());
        var plaintext = "PHI:DOB=1990-01-01"u8.ToArray();

        // Act
        var (_, iv1) = svc.Encrypt(new MemoryStream(plaintext));
        var (_, iv2) = svc.Encrypt(new MemoryStream(plaintext));

        // Assert — IVs must differ (fresh GenerateIV() per call)
        Assert.False(
            iv1.SequenceEqual(iv2),
            "Each Encrypt call must generate a unique IV.");
    }

    // ── EC-001: Key length validation in test constructor ─────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    public void Constructor_WithKeyNotExactly32Bytes_ThrowsArgumentException(int keyLength)
    {
        // Arrange
        var invalidKey = new byte[keyLength];

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _ = new AesEncryptionService(invalidKey));
    }

    [Fact]
    public void Constructor_WithNullKey_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => _ = new AesEncryptionService(null!));
    }
}
