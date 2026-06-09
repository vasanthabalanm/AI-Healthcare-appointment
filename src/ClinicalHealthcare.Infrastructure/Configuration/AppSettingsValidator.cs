using Microsoft.Extensions.Options;

namespace ClinicalHealthcare.Infrastructure.Configuration;

/// <summary>
/// Startup validator for <see cref="AppSettings"/>.
/// Fails fast if any setting value is outside its allowed range,
/// preventing silent misconfiguration in production (F3 fix, TASK_022).
/// </summary>
public sealed class AppSettingsValidator : IValidateOptions<AppSettings>
{
    public ValidateOptionsResult Validate(string? name, AppSettings options)
    {
        if (options.NoShowRiskThreshold is < 0 or > 100)
            return ValidateOptionsResult.Fail(
                $"AppSettings.NoShowRiskThreshold must be between 0 and 100 (configured: {options.NoShowRiskThreshold}).");

        if (options.CancellationCutoffHours < 0)
            return ValidateOptionsResult.Fail(
                $"AppSettings.CancellationCutoffHours must be ≥ 0 (configured: {options.CancellationCutoffHours}).");

        if (options.QueueCapacity < 1)
            return ValidateOptionsResult.Fail(
                $"AppSettings.QueueCapacity must be ≥ 1 (configured: {options.QueueCapacity}).");

        return ValidateOptionsResult.Success;
    }
}
