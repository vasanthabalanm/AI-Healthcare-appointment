namespace ClinicalHealthcare.Infrastructure.Entities;

/// <summary>
/// Identifies whether the intake record was captured via AI-assisted
/// conversational intake or manually filled by the patient.
/// </summary>
public enum IntakeSource
{
    Manual = 0,
    AI     = 1
}

/// <summary>
/// Represents one version of a patient intake record.
///
/// Versioning strategy (AC-003):
/// Each time an intake is PATCHed, the application sets <c>IsLatest = false</c>
/// on all existing rows sharing the same <c>IntakeGroupId</c>, then inserts a
/// new row with an incremented <c>Version</c> and <c>IsLatest = true</c>.
/// This produces an immutable audit trail of every intake revision.
///
/// Default query filter (AC-004):
/// <see cref="ApplicationDbContext"/> registers <c>.HasQueryFilter(r =&gt; r.IsLatest)</c>
/// so normal queries return only the current version. Use
/// <c>dbSet.IgnoreQueryFilters()</c> to access the full version history
/// (e.g., for admin or audit views).
/// </summary>
public sealed class IntakeRecord
{
    public int Id { get; set; }

    /// <summary>
    /// Groups all versions of the same logical intake together.
    /// All rows with the same <c>IntakeGroupId</c> belong to one intake form submission.
    /// </summary>
    public Guid IntakeGroupId { get; set; } = Guid.NewGuid();

    public int PatientId { get; set; }

    /// <summary>Monotonically increasing version number within an <c>IntakeGroupId</c>.</summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// True for the current (latest) version only.
    /// Managed by the application before every <c>SaveChanges</c>.
    /// The default query filter relies on this flag.
    /// </summary>
    public bool IsLatest { get; set; } = true;

    public IntakeSource Source { get; set; } = IntakeSource.Manual;

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    // ── Clinical fields ────────────────────────────────────────────────────
    public string? ChiefComplaint  { get; set; }
    public string? CurrentMeds     { get; set; }
    public string? Allergies       { get; set; }
    public string? MedicalHistory  { get; set; }

    // ── Insurance pre-check (AC-002 / TASK_032) ───────────────────────────
    /// <summary>Outcome of the soft insurance pre-check. Non-blocking — record always created.</summary>
    public InsuranceStatus InsuranceStatus { get; set; } = InsuranceStatus.Skipped;

    // ── PHI retention (AC-002 / TASK_011) ────────────────────────────────────

    /// <summary>Soft-delete flag. True means the record is pending retention expiry.</summary>
    public bool IsDeleted { get; set; } = false;

    /// <summary>
    /// Date after which the record may be purged under the PHI 7-year retention policy.
    /// Null until the record is soft-deleted.
    /// </summary>
    public DateTimeOffset? RetainUntil { get; set; }

    // Navigation property
    public UserAccount? Patient { get; set; }
}
