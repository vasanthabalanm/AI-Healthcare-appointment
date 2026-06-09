namespace ClinicalHealthcare.Infrastructure.Entities;

/// <summary>
/// Clinical record verification status for a patient (Trust-First pattern — AIR-006, TASK_044).
/// </summary>
public enum VerificationStatus
{
    Unverified = 0,
    Verified   = 1
}

/// <summary>
/// Medical coding completion status for a patient (Trust-First pattern — TASK_047).
/// </summary>
public enum CodingStatus
{
    Incomplete = 0,
    Complete   = 1
}

/// <summary>
/// Represents a registered user account (patient, staff, or admin).
/// Email uniqueness is enforced at the database level via a unique index.
/// </summary>
public sealed class UserAccount
{
    public int Id { get; set; }

    /// <summary>Unique email address — unique index enforced in OnModelCreating.</summary>
    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Role values: "patient" | "staff" | "admin"</summary>
    public string Role { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ── Profile fields (TASK_012) ─────────────────────────────────────────────
    public string FirstName { get; set; } = string.Empty;
    public string LastName  { get; set; } = string.Empty;

    // ── Walk-in registration fields (TASK_033) ───────────────────────────────

    /// <summary>Date of birth — used for patient search de-duplication (AC-001 / TASK_033).</summary>
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>True when the account was created via the staff walk-in registration flow (AC-002).</summary>
    public bool WalkIn { get; set; } = false;

    // ── Contact info (TASK_027) ──────────────────────────────────────────────

    /// <summary>
    /// Optional phone number (any format). Normalized to E.164 by <c>PhoneNormalizer</c> before SMS delivery.
    /// Null or empty means no phone number provided — SMS reminders are silently skipped.
    /// </summary>
    [System.ComponentModel.DataAnnotations.MaxLength(20)]
    public string? PhoneNumber { get; set; }

    // ── PHI retention (AC-002 / TASK_011) ────────────────────────────────────

    /// <summary>Soft-delete flag. True means the account is pending retention expiry.</summary>
    public bool IsDeleted { get; set; } = false;

    /// <summary>
    /// Date after which the record may be purged under the PHI 7-year retention policy.
    /// Null until the account is soft-deleted.
    /// </summary>
    public DateTimeOffset? RetainUntil { get; set; }

    // ── Login lockout (AC-005 / TASK_015) ─────────────────────────────────────

    /// <summary>Running count of consecutive failed login attempts since last success.</summary>
    public int FailedLoginAttempts { get; set; } = 0;

    /// <summary>UTC instant until which this account is locked out. Null means not locked.</summary>
    public DateTimeOffset? LockoutEnd { get; set; }

    // ── Email verification (AC-002 / TASK_012) ───────────────────────────────

    /// <summary>
    /// SHA-256 hash of the one-time email verification token.
    /// Null after the token has been consumed or was never generated.
    /// </summary>
    public string? VerificationTokenHash { get; set; }

    /// <summary>UTC expiry of the verification token (24 hours from generation).</summary>
    public DateTime? VerificationTokenExpiry { get; set; }

    // ── Password reset (AC-002 / TASK_017) ───────────────────────────────────

    /// <summary>PBKDF2 SHA-256 hash of the one-time password-reset token. Null when no reset is pending.</summary>
    public string? PasswordResetTokenHash { get; set; }

    /// <summary>UTC instant at which the reset token expires (60 minutes from generation). Null when none pending.</summary>
    public DateTime? PasswordResetTokenExpiry { get; set; }

    /// <summary>True once the token has been consumed; prevents replay attacks (AC-005).</summary>
    public bool PasswordResetTokenUsed { get; set; } = false;

    /// <summary>UTC instant the last reset token was issued. Used to enforce the 5-minute flood cooldown (F3).</summary>
    public DateTime? PasswordResetTokenIssuedAt { get; set; }

    // ── Clinical verification (TASK_044) ─────────────────────────────────────

    /// <summary>Clinical record verification status (Trust-First pattern — AIR-006).</summary>
    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.Unverified;

    /// <summary>Staff member who last verified this patient's clinical record. Null until first verification.</summary>
    public int? VerifiedById { get; set; }

    /// <summary>UTC timestamp of the last clinical verification. Null until first verification.</summary>
    public DateTime? VerifiedAt { get; set; }

    // ── Medical coding status (TASK_047) ──────────────────────────────────────

    /// <summary>Medical coding completion status (Trust-First pattern — TASK_047).</summary>
    public CodingStatus CodingStatus { get; set; } = CodingStatus.Incomplete;
}
