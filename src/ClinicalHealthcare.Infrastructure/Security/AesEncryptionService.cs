using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ClinicalHealthcare.Infrastructure.Security;

/// <summary>
/// AES-256-CBC encryption/decryption service.
///
/// Key resolution (AC-001):
///   1. <c>CLINICAL_AES_KEY</c> env var (Base64-encoded 32-byte key) — primary.
///   2. Windows DPAPI (<see cref="ProtectedData.Unprotect"/>) from <c>clinical_aes_key.dpapi</c>
///      adjacent to the assembly — fallback when env var is absent on Windows.
///
/// A fresh random IV is generated per <see cref="Encrypt"/> call.
/// Decryption reads the stored <c>Base64(IV):Base64(ciphertext)</c> blob in-memory (AC-002).
/// </summary>
public sealed class AesEncryptionService : IAesEncryptionService
{
    /// <summary>File name for the DPAPI-protected key blob (AC-001 fallback).</summary>
    internal const string DpapiKeyFileName = "clinical_aes_key.dpapi";

    private readonly byte[] _key;

    /// <summary>
    /// Production constructor — reads <c>CLINICAL_AES_KEY</c> from the environment,
    /// falling back to Windows DPAPI when the variable is absent (AC-001).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when neither the env var nor the DPAPI key file is available.
    /// </exception>
    public AesEncryptionService()
    {
        _key = ResolveKey();
    }

    /// <summary>
    /// Test constructor — accepts a pre-validated 32-byte key directly.
    /// </summary>
    public AesEncryptionService(byte[] key)
    {
        if (key is null || key.Length != 32)
            throw new ArgumentException("Key must be exactly 32 bytes.", nameof(key));
        _key = key;
    }

    // ── Key resolution ────────────────────────────────────────────────────────

    private static byte[] ResolveKey()
    {
        // Primary: env var.
        var keyBase64 = Environment.GetEnvironmentVariable("CLINICAL_AES_KEY");
        if (!string.IsNullOrWhiteSpace(keyBase64))
        {
            var key = Convert.FromBase64String(keyBase64);
            if (key.Length != 32)
                throw new InvalidOperationException(
                    $"CLINICAL_AES_KEY must decode to exactly 32 bytes. Got {key.Length}.");
            return key;
        }

        // Fallback: Windows DPAPI (AC-001).
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new InvalidOperationException(
                "CLINICAL_AES_KEY env var is required on non-Windows platforms (DPAPI unavailable).");

        var dpapiPath = Path.Combine(
            AppContext.BaseDirectory, DpapiKeyFileName);

        if (!File.Exists(dpapiPath))
            throw new InvalidOperationException(
                $"CLINICAL_AES_KEY env var not set and DPAPI key file not found at: {dpapiPath}");

        var encryptedKeyBlob = File.ReadAllBytes(dpapiPath);
        var decryptedKey     = ProtectedData.Unprotect(
            encryptedKeyBlob, null, DataProtectionScope.LocalMachine);

        if (decryptedKey.Length != 32)
            throw new InvalidOperationException(
                $"DPAPI key file decoded to {decryptedKey.Length} bytes; expected 32.");

        return decryptedKey;
    }

    // ── Encrypt ───────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public (byte[] Ciphertext, byte[] Iv) Encrypt(Stream input)
    {
        using var aes = Aes.Create();
        aes.KeySize   = 256;
        aes.Mode      = CipherMode.CBC;
        aes.Padding   = PaddingMode.PKCS7;
        aes.Key       = _key;
        aes.GenerateIV(); // Random IV per file (AC-003 / TASK_038)

        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        {
            input.CopyTo(cs);
        } // CryptoStream.Dispose() calls FlushFinalBlock() before reading ms

        return (ms.ToArray(), aes.IV);
    }

    // ── Decrypt ───────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Reads the <c>Base64(IV):Base64(Ciphertext)</c> blob, decrypts with AES-256-CBC
    /// via <see cref="CryptoStream"/>, and returns a <see cref="MemoryStream"/> at position 0.
    /// No temporary file is ever written (AC-002).
    /// Caller must dispose the returned stream.
    /// </remarks>
    public Stream Decrypt(string encryptedBlobPath)
    {
        if (string.IsNullOrWhiteSpace(encryptedBlobPath))
            throw new ArgumentException("Encrypted blob path must not be null or whitespace.", nameof(encryptedBlobPath));

        // Read the stored text: Base64(iv):Base64(ciphertext)
        var blobText = File.ReadAllText(encryptedBlobPath);
        var colonIdx = blobText.IndexOf(':', StringComparison.Ordinal);
        if (colonIdx < 0)
            throw new CryptographicException("Encrypted blob format is invalid — missing IV separator.");

        byte[] iv;
        byte[] ciphertext;
        try
        {
            iv         = Convert.FromBase64String(blobText[..colonIdx]);
            ciphertext = Convert.FromBase64String(blobText[(colonIdx + 1)..]);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Encrypted blob contains invalid Base64 data.", ex);
        }

        if (iv.Length != 16)
            throw new CryptographicException($"IV must be 16 bytes; got {iv.Length}.");

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode    = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key     = _key;
        aes.IV      = iv;

        // Decrypt in-memory via CryptoStream — no temp file written (AC-002).
        var plaintext = new MemoryStream();
        using (var cipherStream = new MemoryStream(ciphertext))
        using (var cs = new CryptoStream(cipherStream, aes.CreateDecryptor(), CryptoStreamMode.Read))
        {
            cs.CopyTo(plaintext); // CryptographicException surfaces here on bad key / corrupt data
        }

        plaintext.Position = 0;
        return plaintext;
    }
}

