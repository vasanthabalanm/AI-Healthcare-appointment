using System.ComponentModel.DataAnnotations;

namespace ClinicalHealthcare.Api.Features.Intake;

/// <summary>
/// Request body for <c>POST /intake/manual</c> (AC-001 / AC-002).
///
/// All string fields are bounded by <see cref="MaxLengthAttribute"/> so they
/// align with the <c>IntakeRecord</c> column constraints.
/// <c>PatientId</c> is NOT accepted from the body — it is sourced exclusively
/// from the authenticated JWT <c>sub</c> claim (AC-004 / OWASP A01).
/// </summary>
public sealed record ManualIntakeRequest
{
    /// <summary>Primary presenting complaint (required).</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "chiefComplaint is required.")]
    [MaxLength(1000, ErrorMessage = "chiefComplaint must not exceed 1000 characters.")]
    public string ChiefComplaint { get; init; } = string.Empty;

    /// <summary>Current medications — optional free-text.</summary>
    [MaxLength(2000, ErrorMessage = "currentMeds must not exceed 2000 characters.")]
    public string? CurrentMeds { get; init; }

    /// <summary>Known drug/food allergies — optional free-text.</summary>
    [MaxLength(2000, ErrorMessage = "allergies must not exceed 2000 characters.")]
    public string? Allergies { get; init; }

    /// <summary>Relevant past medical history — optional free-text.</summary>
    [MaxLength(4000, ErrorMessage = "medicalHistory must not exceed 4000 characters.")]
    public string? MedicalHistory { get; init; }

    // ── Insurance fields (TASK_032) ───────────────────────────────────────────

    /// <summary>Payer/insurer identifier — optional. Empty triggers <c>InsuranceStatus=Skipped</c>.</summary>
    [MaxLength(100, ErrorMessage = "insurerId must not exceed 100 characters.")]
    public string? InsurerId { get; init; }

    /// <summary>Insurance plan code — optional. Empty triggers <c>InsuranceStatus=Skipped</c>.</summary>
    [MaxLength(100, ErrorMessage = "planCode must not exceed 100 characters.")]
    public string? PlanCode { get; init; }
}
