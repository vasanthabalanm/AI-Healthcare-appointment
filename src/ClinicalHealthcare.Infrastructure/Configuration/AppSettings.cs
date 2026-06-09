namespace ClinicalHealthcare.Infrastructure.Configuration;

/// <summary>
/// Application-level configuration settings bound from the "AppSettings" section.
/// Values not present in configuration fall back to the declared defaults.
/// </summary>
public sealed class AppSettings
{
    public const string SectionName = "AppSettings";

    /// <summary>
    /// Number of hours a patient has to accept a swap offer before it expires
    /// and the slot is returned to general availability. Default: 2 hours.
    /// </summary>
    public int SwapOfferWindowHours { get; init; } = 2;

    /// <summary>
    /// No-show risk score at or above this value marks an appointment as high-risk.
    /// Default: 70. Range: 0–100.
    /// </summary>
    public int NoShowRiskThreshold { get; init; } = 70;

    /// <summary>
    /// Minimum hours before a slot time that a patient may cancel or reschedule.
    /// Default: 24 hours.
    /// </summary>
    public int CancellationCutoffHours { get; init; } = 24;

    /// <summary>
    /// Maximum number of walk-in patients that may be queued per day.
    /// Exceeding this limit returns HTTP 409 unless <c>Override=true</c> is supplied (AC-003 / TASK_033).
    /// Default: 20.
    /// </summary>
    public int QueueCapacity { get; init; } = 20;
}
