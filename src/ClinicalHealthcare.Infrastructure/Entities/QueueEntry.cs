using System.ComponentModel.DataAnnotations;

namespace ClinicalHealthcare.Infrastructure.Entities;

/// <summary>
/// Status of a patient's position in the daily walk-in queue.
/// </summary>
public enum QueueStatus
{
    Waiting   = 0,
    CheckedIn = 1,
    Removed   = 2,
}

/// <summary>
/// Represents one patient's position in the walk-in queue for a given calendar date (AC-005 / TASK_033).
///
/// One entry per patient per day; <c>Position</c> is assigned as (MAX(Position) + 1) for
/// the target date at the time of insertion and is never re-numbered.
/// </summary>
public sealed class QueueEntry
{
    public int Id { get; set; }

    /// <summary>FK → <see cref="UserAccount.Id"/> of the queued patient.</summary>
    public int PatientId { get; set; }

    /// <summary>Calendar date (UTC date only) this entry belongs to.</summary>
    public DateOnly QueueDate { get; set; }

    /// <summary>1-based sequential position within the day's queue.</summary>
    public int Position { get; set; }

    public QueueStatus Status { get; set; } = QueueStatus.Waiting;

    /// <summary>True when the entry was created via the walk-in registration flow.</summary>
    public bool IsWalkIn { get; set; } = false;

    /// <summary>FK → <see cref="UserAccount.Id"/> of the staff member who created the entry.</summary>
    public int AddedByStaffId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optimistic-concurrency token mapped to a SQL Server <c>rowversion</c> column.
    /// Used to detect concurrent reorder conflicts (AC-003 / TASK_034).
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    // Navigation properties
    public UserAccount? Patient    { get; set; }
    public UserAccount? AddedByStaff { get; set; }
}
