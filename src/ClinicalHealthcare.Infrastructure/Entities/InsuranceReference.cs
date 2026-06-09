namespace ClinicalHealthcare.Infrastructure.Entities;

/// <summary>
/// Insurance pre-check outcome stored on <see cref="IntakeRecord"/> (AC-002 / TASK_032).
/// The pre-check is non-blocking — intake always returns 201 regardless of status.
/// </summary>
public enum InsuranceStatus
{
    /// <summary>Insurance found and active in <see cref="InsuranceReference"/> table.</summary>
    Validated  = 0,

    /// <summary>Insurance not found in <see cref="InsuranceReference"/> table, or lookup failed.</summary>
    NotVerified = 1,

    /// <summary>Insurance fields were empty or absent; check skipped per AC-004.</summary>
    Skipped    = 2,
}

/// <summary>
/// Read-only reference table used by <c>InsurancePreCheckService</c> for soft insurance validation (AC-003 / TASK_032).
///
/// Populated by operations/admin; never written by the patient-facing intake flow.
/// </summary>
public sealed class InsuranceReference
{
    public int Id { get; set; }

    /// <summary>Insurer identifier (e.g. payer ID) — used as lookup key.</summary>
    public string InsurerId { get; set; } = string.Empty;

    /// <summary>Human-readable insurer name for display purposes.</summary>
    public string InsurerName { get; set; } = string.Empty;

    /// <summary>Insurance plan code — combined with <see cref="InsurerId"/> for lookup.</summary>
    public string PlanCode { get; set; } = string.Empty;

    /// <summary>False records are ignored during pre-check (treated as NotVerified).</summary>
    public bool IsActive { get; set; } = true;
}
