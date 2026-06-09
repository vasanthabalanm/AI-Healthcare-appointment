namespace ClinicalHealthcare.Infrastructure.AI;

/// <summary>
/// Redis-serialisable AI intake session state (AC-002).
///
/// Stored under key <c>ai-intake:{sessionId}</c> with TTL=900s.
/// The TTL is reset on every <c>POST /intake/ai/message</c> call.
/// </summary>
public sealed class AiIntakeSession
{
    /// <summary>Redis / Rasa sender ID.</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Authenticated patient's user-account ID.</summary>
    public int PatientId { get; set; }

    /// <summary>
    /// Fields confirmed at ≥0.70 confidence.
    /// Keys match <see cref="ClinicalHealthcare.Infrastructure.Entities.IntakeRecord"/> property names
    /// (camelCase: chiefComplaint, currentMeds, allergies, medicalHistory).
    /// </summary>
    public Dictionary<string, string> ConfirmedFields { get; set; } = new();

    /// <summary>
    /// Index into the fixed intake question sequence (0–3).
    /// Auto-incremented by <c>SendAiMessageEndpoint</c> when a field is committed.
    /// Sequence: chiefComplaint → currentMeds → allergies → medicalHistory.
    /// </summary>
    public int CurrentStep { get; set; } = 0;

    /// <summary>UTC timestamp of session creation.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Set after a successful <c>POST /intake/ai/complete</c> to prevent a duplicate
    /// <see cref="ClinicalHealthcare.Infrastructure.Entities.IntakeRecord"/> on retry.
    /// </summary>
    public int? CompletedIntakeRecordId { get; set; }
}
