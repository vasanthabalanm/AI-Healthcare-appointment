namespace ClinicalHealthcare.Infrastructure.Entities;

/// <summary>
/// Resolution status for a <see cref="ConflictFlag"/>.
/// </summary>
public enum ConflictFlagStatus
{
    Unresolved = 0,
    Resolved   = 1,
    Dismissed  = 2
}

/// <summary>
/// Represents a detected data conflict between two extracted values for the same
/// clinical field. A staff member reviews and resolves or dismisses each conflict.
/// Stored in PostgreSQL via <see cref="ClinicalHealthcare.Infrastructure.Data.ClinicalDbContext"/>.
/// </summary>
public sealed class ConflictFlag
{
    public int Id { get; set; }

    public int PatientId { get; set; }

    /// <summary>Name of the clinical field that has conflicting values (e.g. "BloodPressure").</summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>First conflicting value.</summary>
    public string Value1 { get; set; } = string.Empty;

    /// <summary>Second conflicting value.</summary>
    public string Value2 { get; set; } = string.Empty;

    public ConflictFlagStatus Status { get; set; } = ConflictFlagStatus.Unresolved;

    /// <summary>
    /// Staff member who resolved or dismissed this conflict.
    /// Null while status is <see cref="ConflictFlagStatus.Unresolved"/>.
    /// </summary>
    public int? ResolvedByStaffId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
