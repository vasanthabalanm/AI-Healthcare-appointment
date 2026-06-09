namespace ClinicalHealthcare.Infrastructure.Entities;

/// <summary>
/// Status values for a <see cref="WaitlistEntry"/>.
/// The filtered unique index on <c>(PatientId) WHERE Status = 0</c> relies on
/// <c>Active = 0</c> being the integer value stored in the database.
/// </summary>
public enum WaitlistStatus
{
    Active    = 0,
    Fulfilled = 1,
    Expired   = 2,

    /// <summary>
    /// A swap-offer has been sent to the patient.
    /// The patient has until <see cref="WaitlistEntry.OfferExpiresAt"/> to accept.
    /// Not covered by the <c>WHERE [Status] = 0</c> partial unique index (intentional).
    /// </summary>
    OfferSent = 3
}

/// <summary>
/// Represents a patient's position on the appointment waitlist.
/// A patient may have at most one <c>Active</c> entry at any time; this is
/// enforced by a filtered partial unique index on <c>(PatientId) WHERE [Status] = 0</c>.
/// </summary>
public sealed class WaitlistEntry
{
    public int Id { get; set; }

    public int PatientId { get; set; }

    /// <summary>
    /// Optional preferred slot the patient is waiting for.
    /// Null means "any available slot".
    /// </summary>
    public int? PreferredSlotId { get; set; }

    public WaitlistStatus Status { get; set; } = WaitlistStatus.Active;

    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When <see cref="WaitlistStatus.OfferSent"/>, the deadline by which the patient
    /// must accept the swap offer. Null for all other statuses.
    /// </summary>
    public DateTime? OfferExpiresAt { get; set; }

    /// <summary>
    /// When <see cref="WaitlistStatus.OfferSent"/>, the ID of the <see cref="Slot"/>
    /// being held for this patient. Null for all other statuses.
    /// </summary>
    public int? OfferedSlotId { get; set; }

    // ── PHI retention (AC-002 / TASK_011) ────────────────────────────────────

    /// <summary>Soft-delete flag. True means the entry is pending retention expiry.</summary>
    public bool IsDeleted { get; set; } = false;

    /// <summary>
    /// Date after which the record may be purged under the PHI 7-year retention policy.
    /// Null until the entry is soft-deleted.
    /// </summary>
    public DateTimeOffset? RetainUntil { get; set; }

    // Navigation properties
    public UserAccount? Patient { get; set; }
    public Slot? PreferredSlot { get; set; }
}
