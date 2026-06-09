using ClinicalHealthcare.Infrastructure.Entities;

namespace ClinicalHealthcare.Infrastructure.NLP;

/// <summary>
/// Lightweight extraction result produced by <see cref="ClinicalFieldExtractor.Extract"/>.
/// Mapped to <see cref="ExtractedClinicalField"/> by the Hangfire job.
/// </summary>
public sealed record ExtractedFieldDto(
    ClinicalFieldType FieldType,
    string            FieldName,
    string            FieldValue);
