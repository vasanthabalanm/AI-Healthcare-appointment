using System.Security.Cryptography;
using System.Text;

namespace ClinicalHealthcare.Infrastructure.Calendar;

/// <summary>
/// AES-256-CBC encryption helper.
/// The 16-byte IV is randomly generated per-encryption and prepended to the ciphertext.
/// Output format: Base64(IV || CipherText).
/// AC-004: tokens are never stored in plaintext.
/// </summary>
public static class AesCbc
{
    /// <summary>
    /// Derives a 32-byte AES key from arbitrary key material using SHA-256.
    /// Deterministic — same input always produces the same key.
    /// </summary>
    public static byte[] DeriveKey(string keyMaterial) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial));

    /// <summary>Encrypts <paramref name="plaintext"/> and returns Base64(IV || CipherText).</summary>
    public static string Encrypt(string plaintext, byte[] key)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode    = CipherMode.CBC;
        aes.Key     = key;
        aes.GenerateIV();

        using var ms         = new MemoryStream();
        ms.Write(aes.IV, 0, aes.IV.Length); // 16-byte IV prepended

        using var encryptor  = aes.CreateEncryptor();
        using var cs         = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
        var plaintextBytes   = Encoding.UTF8.GetBytes(plaintext);
        cs.Write(plaintextBytes, 0, plaintextBytes.Length);
        cs.FlushFinalBlock();

        return Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>Decrypts a Base64(IV || CipherText) string produced by <see cref="Encrypt"/>.</summary>
    public static string Decrypt(string ciphertext, byte[] key)
    {
        var data = Convert.FromBase64String(ciphertext);
        var iv   = data[..16];
        var body = data[16..];

        using var aes       = Aes.Create();
        aes.KeySize = 256;
        aes.Mode    = CipherMode.CBC;
        aes.Key     = key;
        aes.IV      = iv;

        using var ms        = new MemoryStream(body);
        using var decryptor = aes.CreateDecryptor();
        using var cs        = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var reader    = new StreamReader(cs, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
