namespace ClinicalHealthcare.Infrastructure.Security;

/// <summary>
/// Abstraction for AES-256-CBC file encryption and decryption.
/// </summary>
public interface IAesEncryptionService
{
    /// <summary>
    /// Encrypts the entire content of <paramref name="input"/> using AES-256-CBC.
    /// </summary>
    /// <param name="input">Plaintext stream. Must be positioned at the start.</param>
    /// <returns>
    /// A tuple of <c>Ciphertext</c> (the encrypted bytes) and <c>Iv</c>
    /// (the randomly generated 16-byte initialisation vector).
    /// </returns>
    (byte[] Ciphertext, byte[] Iv) Encrypt(Stream input);

    /// <summary>
    /// Decrypts an encrypted blob file written by <see cref="Encrypt"/> and
    /// returns the plaintext as an in-memory stream (AC-002 — no temp file written).
    /// </summary>
    /// <param name="encryptedBlobPath">
    /// Path to the <c>.enc</c> file containing <c>Base64(IV):Base64(Ciphertext)</c>.
    /// </param>
    /// <returns>A <see cref="MemoryStream"/> positioned at 0 containing the decrypted plaintext.</returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// Thrown when the file is corrupt, truncated, or the wrong key is used (AC-004).
    /// </exception>
    Stream Decrypt(string encryptedBlobPath);
}
