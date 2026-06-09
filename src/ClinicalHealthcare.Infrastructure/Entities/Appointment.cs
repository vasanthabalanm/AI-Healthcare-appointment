namespace ClinicalHealthcare.Infrastructure.Entities;

/// <summary>
/// Finite-state machine (FSM) states for an <see cref="Appointment"/>.
/// Valid transitions are enforced by <c>AppointmentFsmInterceptor</c>:
/// <list type="bullet">
///   <item>Scheduled → Arrived</item>
///   <item>Scheduled → Cancelled</item>
///   <item>Scheduled → NoShow</item>
///   <item>Arrived   → Completed</item>
/// </list>
/// All other transitions are invalid and will throw <see cref="InvalidOperationException"/>.
/// </summary>
public enum AppointmentStatus
{
    Scheduled  = 0,
    Arrived    = 1,
    Completed  = 2,
    Cancelled  = 3,
    NoShow     = 4
}

/// <summary>
/// Represents a patient appointment linked to a <see cref="Slot"/>.
/// Status transitions are enforced by <c>AppointmentFsmInterceptor</c> at save time.
/// </summary>
public sealed class Appointment
{
    public int Id { get; set; }

    public int PatientId { get; set; }

    public int SlotId { get; set; }

    public AppointmentStatus Status { get; set; } = AppointmentStatus.Scheduled;

    public DateTime BookedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Rule-based no-show risk score (0–100). Calculated once at booking; never updated.</summary>
    public int NoShowRiskScore { get; set; }

    /// <summary>True when <see cref="NoShowRiskScore"/> meets or exceeds <c>AppSettings.NoShowRiskThreshold</c>.</summary>
    public bool IsHighRisk { get; set; }

    /// <summary>
    /// Hangfire job ID of the T-48 h reminder job. Null when the appointment was booked
    /// less than 48 h before the slot time (no reminder scheduled) or before TASK_023.
    /// Used by cancel and reschedule to delete the stale job.
    /// Hangfire IDs are GUIDs (36 chars) or integers; 100 chars is a safe upper bound.
    /// </summary>
    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? ReminderJobId { get; set; }

    /// <summary>
    /// Hangfire job ID of the T-48 h SMS reminder job (TASK_027 / AC-005).
    /// Null when no SMS job was scheduled (patient had no phone at booking time).
    /// </summary>
    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? SmsReminderJobId48h { get; set; }

    /// <summary>
    /// Hangfire job ID of the T-2 h SMS reminder job (TASK_027 / AC-005).
    /// Null when no SMS job was scheduled (patient had no phone at booking time).
    /// </summary>
    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? SmsReminderJobId2h { get; set; }

    // ── Email reminder + cancellation link (TASK_028) ─────────────────────────

    /// <summary>
    /// Hangfire job ID of the T-48 h email reminder job (TASK_028 / AC-004).
    /// Null when no job was scheduled (appointment booked less than 48 h before slot).
    /// </summary>
    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? EmailReminderJobId { get; set; }

    /// <summary>
    /// SHA-256 hash of the single-use cancellation link token (TASK_028 / AC-002/AC-003).
    /// Null until <see cref="SendEmailReminderJob"/> generates the token.
    /// Never store the raw token — only the hash is persisted.
    /// </summary>
    [System.ComponentModel.DataAnnotations.MaxLength(64)]
    public string? CancellationLinkTokenHash { get; set; }

    /// <summary>UTC expiry of the cancellation link token (48 hours from generation).</summary>
    public DateTime? CancellationLinkExpiry { get; set; }

    /// <summary>
    /// True once the cancellation link has been consumed. Prevents replay attacks (AC-003).
    /// </summary>
    public bool CancellationLinkUsed { get; set; } = false;

    /// <summary>
    /// UTC timestamp set after the T-48h reminder email was successfully delivered.
    /// Used as an idempotence sentinel: <see cref="SendEmailReminderJob"/> skips execution
    /// if this value is non-null, preventing duplicate emails on Hangfire retry.
    /// </summary>
    public DateTime? EmailReminderSentAt { get; set; }

    /// <summary>
    /// Optimistic-concurrency token mapped to a SQL Server <c>rowversion</c> column.
    /// Used to detect concurrent check-in conflicts (AC-004 / TASK_035).
    /// </summary>
    [System.ComponentModel.DataAnnotations.Timestamp]
    public byte[] RowVersion { get; set; } = [];

    // Navigation properties
    public UserAccount? Patient { get; set; }
    public Slot? Slot { get; set; }
}
