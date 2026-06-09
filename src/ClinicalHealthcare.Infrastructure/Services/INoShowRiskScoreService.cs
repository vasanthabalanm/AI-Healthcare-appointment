namespace ClinicalHealthcare.Infrastructure.Services;

/// <summary>
/// Calculates a rule-based no-show risk score (0–100) for a patient booking.
///
/// Score components (AC-002, TASK_022):
/// <list type="bullet">
///   <item>Prior no-shows: each no-show adds +20 points, capped at 60.</item>
///   <item>Lead time: &lt;24 h = +30; 24–72 h = +15; &gt;72 h = +0.</item>
///   <item>Intake completion: no <see cref="ClinicalHealthcare.Infrastructure.Entities.IntakeRecord"/> for patient = +10; any record = +0.</item>
/// </list>
/// Total is capped at 100.
/// </summary>
public interface INoShowRiskScoreService
{
    /// <summary>
    /// Calculates the risk score for a patient booking at <paramref name="slotTime"/>.
    /// </summary>
    Task<int> CalculateAsync(int patientId, DateTime slotTime, CancellationToken cancellationToken = default);
}
