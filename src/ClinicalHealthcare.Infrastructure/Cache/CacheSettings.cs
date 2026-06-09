namespace ClinicalHealthcare.Infrastructure.Cache;

/// <summary>
/// TTL configuration for Redis cache keys (seconds).
/// Bound from "CacheSettings" configuration section.
/// </summary>
public sealed class CacheSettings
{
    public const string SectionName = "CacheSettings";

    /// <summary>Patient session slot cache TTL. Default 15 minutes.</summary>
    public int SessionTtlSeconds { get; init; } = 900;

    /// <summary>Individual appointment slot cache TTL. Default 60 seconds.</summary>
    public int SlotTtlSeconds { get; init; } = 60;

    /// <summary>360° patient view aggregate cache TTL. Default 5 minutes.</summary>
    public int View360TtlSeconds { get; init; } = 300;
}
