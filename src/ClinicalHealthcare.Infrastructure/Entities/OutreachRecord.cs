namespace ClinicalHealthcare.Infrastructure.Entities;

/// <summary>
/// Records a staff outreach attempt on a high-risk appointment (AC-002 / TASK_037).
///
/// One outreach record per attempt; multiple records may exist for the same appointment.
/// </summary>
public sealed class OutreachRecord
{
    public int Id { get; set; }

    /// <summary>FK → <see cref="Appointment.Id"/>.</summary>
    public int AppointmentId { get; set; }

    /// <summary>FK → <see cref="UserAccount.Id"/> of the staff member who made the attempt.</summary>
    public int StaffId { get; set; }

    /// <summary>Free-text notes from the outreach attempt.</summary>
    public string? Notes { get; set; }

    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public Appointment? Appointment { get; set; }
    public UserAccount? Staff       { get; set; }
}
