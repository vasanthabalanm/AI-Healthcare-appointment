namespace ClinicalHealthcare.Infrastructure.Entities;

/// <summary>
/// Virus scan result for an uploaded <see cref="ClinicalDocument"/>.
/// The database column defaults to <c>Pending (0)</c> via a column-level DEFAULT constraint.
/// </summary>
public enum VirusScanResult
{
    Pending  = 0,
    Clean    = 1,
    Infected = 2
}

/// <summary>
/// OCR extraction status for an uploaded <see cref="ClinicalDocument"/>.
/// </summary>
public enum OcrStatus
{
    Pending       = 0,
    Extracted     = 1,
    LowConfidence = 2,
    NoData        = 3
}

/// <summary>
/// Represents a patient-uploaded clinical document.
///
/// IMPORTANT: Binary document content is NEVER stored in the database.
/// Only the <see cref="EncryptedBlobPath"/> — the AES-encrypted on-disk path —
/// is persisted. This satisfies the HIPAA requirement that PHI at rest is
/// encrypted and that the database does not hold raw document bytes.
/// </summary>
public sealed class ClinicalDocument
{
    public int Id { get; set; }

    public int PatientId { get; set; }

    /// <summary>Staff member who uploaded this document on behalf of a patient. Nullable for patient self-uploads.</summary>
    public int? UploadedByStaffId { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;

    private string _encryptedBlobPath = string.Empty;

    /// <summary>
    /// AES-encrypted file path on disk. Stored as <c>nvarchar(500)</c>.
    /// Binary document content is never stored in the database.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the value is null or whitespace.</exception>
    public string EncryptedBlobPath
    {
        get => _encryptedBlobPath;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("EncryptedBlobPath must not be null or whitespace.", nameof(value));
            _encryptedBlobPath = value;
        }
    }

    /// <summary>
    /// Virus scan outcome. Defaults to <see cref="VirusScanResult.Pending"/> at
    /// both the C# property level and the database column DEFAULT constraint.
    /// </summary>
    public VirusScanResult VirusScanResult { get; set; } = VirusScanResult.Pending;

    /// <summary>OCR extraction status. Defaults to <see cref="OcrStatus.Pending"/>.</summary>
    public OcrStatus OcrStatus { get; set; } = OcrStatus.Pending;

    /// <summary>
    /// Raw text extracted by the Tesseract OCR pipeline (AC-004 — TASK_040).
    /// Null until the <c>OcrDocumentJob</c> completes successfully.
    /// </summary>
    public string? RawOcrText { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optimistic concurrency token. Prevents lost updates when background
    /// workers (virus scanner, OCR pipeline) update the same row concurrently.
    /// Mapped to SQL Server <c>rowversion</c> (auto-incremented by DB on every write).
    /// </summary>
    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    // ── PHI retention (AC-002 / TASK_011) ────────────────────────────────────

    /// <summary>Soft-delete flag. True means the document is pending retention expiry.</summary>
    public bool IsDeleted { get; set; } = false;

    /// <summary>
    /// Date after which the record may be purged under the PHI 7-year retention policy.
    /// Null until the document is soft-deleted.
    /// </summary>
    public DateTimeOffset? RetainUntil { get; set; }

    // Navigation properties
    public UserAccount? Patient { get; set; }
    public UserAccount? UploadedByStaff { get; set; }
}
