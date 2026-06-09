using System.ComponentModel.DataAnnotations;

namespace ClinicalHealthcare.Infrastructure.Entities;

/// <summary>
/// Stores AES-256-CBC encrypted OAuth2 tokens for calendar sync.
/// AC-004: tokens are never stored in plaintext.
/// </summary>
public sealed class CalendarToken
{
    public int Id { get; set; }

    public int PatientId { get; set; }

    public int AppointmentId { get; set; }

    /// <summary>OAuth2 provider name, e.g. "Google".</summary>
    [MaxLength(32)]
    public string Provider { get; set; } = "Google";

    /// <summary>AES-256-CBC encrypted access token (IV prepended, base64 encoded).</summary>
    [MaxLength(4000)]
    public string EncryptedAccessToken { get; set; } = string.Empty;

    /// <summary>AES-256-CBC encrypted refresh token. Null when provider does not supply one.</summary>
    [MaxLength(4000)]
    public string? EncryptedRefreshToken { get; set; }

    /// <summary>
    /// Google Calendar event ID set after successful event creation.
    /// Null until the calendar event has been created.
    /// Used for idempotency: if non-null, skip event creation on re-sync.
    /// </summary>
    [MaxLength(200)]
    public string? CalendarEventId { get; set; }

    /// <summary>UTC expiry time of the access token.</summary>
    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public UserAccount? Patient { get; set; }
    public Appointment? Appointment { get; set; }
}
