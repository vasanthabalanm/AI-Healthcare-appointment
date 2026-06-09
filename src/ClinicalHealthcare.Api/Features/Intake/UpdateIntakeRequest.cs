using System.ComponentModel.DataAnnotations;

namespace ClinicalHealthcare.Api.Features.Intake;

/// <summary>
/// Request body for <c>PATCH /intake/{intakeGroupId}</c> (AC-001 / AC-002).
///
/// All fields are optional — supply only the fields you want to change.
/// Null means "leave unchanged"; the prior version's value is carried forward.
/// <c>PatientId</c> is NOT accepted from the body — sourced from JWT <c>sub</c> (OWASP A01).
/// </summary>
public sealed record UpdateIntakeRequest
{
    /// <summary>Updated primary presenting complaint. Null = no change.</summary>
    [MaxLength(1000, ErrorMessage = "chiefComplaint must not exceed 1000 characters.")]
    public string? ChiefComplaint { get; init; }

    /// <summary>Updated current medications. Null = no change.</summary>
    [MaxLength(2000, ErrorMessage = "currentMeds must not exceed 2000 characters.")]
    public string? CurrentMeds { get; init; }

    /// <summary>Updated known drug/food allergies. Null = no change.</summary>
    [MaxLength(2000, ErrorMessage = "allergies must not exceed 2000 characters.")]
    public string? Allergies { get; init; }

    /// <summary>Updated relevant past medical history. Null = no change.</summary>
    [MaxLength(4000, ErrorMessage = "medicalHistory must not exceed 4000 characters.")]
    public string? MedicalHistory { get; init; }
}
