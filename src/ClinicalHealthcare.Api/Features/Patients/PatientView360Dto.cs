using System.Text.Json.Serialization;
using ClinicalHealthcare.Infrastructure.Entities;

namespace ClinicalHealthcare.Api.Features.Patients;

/// <summary>
/// Assembled 360° patient view returned by GET /patients/{id}/view360.
/// Contains demographics, appointment summary, and grouped extracted clinical fields.
/// Serialized to / deserialized from Redis with TTL=300s (AC-001, AC-002).
/// </summary>
public sealed class PatientView360Dto
{
    public int PatientId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public VerificationStatus VerificationStatus { get; init; }

    /// <summary>Extracted clinical fields grouped by <see cref="ClinicalFieldType"/>.</summary>
    public Dictionary<string, List<ClinicalFieldSummary>> ClinicalFields { get; init; } = [];

    /// <summary>Number of Unresolved ConflictFlags for this patient.</summary>
    public int UnresolvedConflicts { get; init; }

    /// <summary>
    /// Informational hint when the patient has no clinical documents.
    /// Null when at least one document exists (AC-005).
    /// </summary>
    public string? Hint { get; init; }
}

/// <summary>Single extracted field entry within a 360° view section.</summary>
public sealed class ClinicalFieldSummary
{
    public string FieldName { get; init; } = string.Empty;
    public string FieldValue { get; init; } = string.Empty;
    public double ConfidenceScore { get; init; }
    public DateTime ExtractedAt { get; init; }
}
