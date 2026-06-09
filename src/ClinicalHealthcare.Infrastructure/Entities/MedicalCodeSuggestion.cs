namespace ClinicalHealthcare.Infrastructure.Entities;

/// <summary>
/// Medical coding standard used for a <see cref="MedicalCodeSuggestion"/>.
/// </summary>
public enum CodeType
{
    ICD10 = 0,
    CPT   = 1
}

/// <summary>
/// Review status of a <see cref="MedicalCodeSuggestion"/>.
/// The Trust-First CHECK constraint requires that <c>verified_by</c> is non-null
/// whenever status is <c>Accepted</c>.
/// </summary>
public enum SuggestionStatus
{
    Pending  = 0,
    Accepted = 1,
    Modified = 2,
    Rejected = 3
}

/// <summary>
/// Represents an AI-generated medical code suggestion for a patient encounter.
/// Enforces two PostgreSQL CHECK constraints:
/// <list type="bullet">
///   <item><c>confidence_score BETWEEN 0.0 AND 1.0</c> (AC-005)</item>
///   <item><c>status != 'Accepted' OR verified_by IS NOT NULL</c> Trust-First rule (AC-004)</item>
/// </list>
/// Stored in PostgreSQL via <see cref="ClinicalHealthcare.Infrastructure.Data.ClinicalDbContext"/>.
/// </summary>
public sealed class MedicalCodeSuggestion
{
    public int Id { get; set; }

    public int PatientId { get; set; }

    public CodeType CodeType { get; set; }

    /// <summary>AI-suggested medical code (e.g. "Z00.00").</summary>
    public string SuggestedCode { get; set; } = string.Empty;

    /// <summary>
    /// Final committed code after staff review. Null until reviewed.
    /// May differ from <see cref="SuggestedCode"/> when status is <c>Modified</c>.
    /// </summary>
    public string? CommittedCode { get; set; }

    public string CodeDescription { get; set; } = string.Empty;

    /// <summary>
    /// AI confidence score in [0.0, 1.0].
    /// Enforced by a PostgreSQL CHECK constraint.
    /// </summary>
    public double ConfidenceScore { get; set; }

    /// <summary>True when <see cref="ConfidenceScore"/> falls below the system threshold.</summary>
    public bool LowConfidenceFlag { get; set; }

    public SuggestionStatus Status { get; set; } = SuggestionStatus.Pending;

    /// <summary>
    /// Staff member who verified this suggestion.
    /// MUST be non-null when <see cref="Status"/> is <c>Accepted</c> (Trust-First rule).
    /// Enforced at the PostgreSQL level via a CHECK constraint.
    /// </summary>
    public int? VerifiedById { get; set; }

    /// <summary>UTC timestamp of staff verification. Null until reviewed.</summary>
    public DateTime? VerifiedAt { get; set; }
}
