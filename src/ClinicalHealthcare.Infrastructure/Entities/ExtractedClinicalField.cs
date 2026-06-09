namespace ClinicalHealthcare.Infrastructure.Entities;

/// <summary>
/// Represents the type of clinical field extracted by the OCR / AI pipeline.
/// </summary>
public enum ClinicalFieldType
{
    VitalSign      = 0,
    MedicalHistory = 1,
    Medication     = 2,
    Allergy        = 3,
    Diagnosis      = 4
}

/// <summary>
/// Represents a single structured field extracted from a <see cref="ClinicalDocument"/>
/// by the OCR / AI pipeline. Soft-deleted via <see cref="IsDeleted"/>; never hard-deleted.
/// Stored in PostgreSQL via <see cref="ClinicalHealthcare.Infrastructure.Data.ClinicalDbContext"/>.
/// </summary>
public sealed class ExtractedClinicalField
{
    public int Id { get; set; }

    public int PatientId { get; set; }

    /// <summary>Source document from which this field was extracted.</summary>
    public int DocumentId { get; set; }

    public ClinicalFieldType FieldType { get; set; }

    public string FieldName { get; set; } = string.Empty;

    public string FieldValue { get; set; } = string.Empty;

    /// <summary>
    /// AI confidence score for the extracted value.
    /// Constrained to [0.0, 1.0] via a PostgreSQL CHECK constraint.
    /// </summary>
    public double ConfidenceScore { get; set; }

    /// <summary>Identifier of the batch extraction job that produced this field.</summary>
    public string ExtractionJobId { get; set; } = string.Empty;

    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Soft-delete flag. Rows are never physically removed.</summary>
    public bool IsDeleted { get; set; } = false;
}
