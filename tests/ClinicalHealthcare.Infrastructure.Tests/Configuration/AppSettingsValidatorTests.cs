using ClinicalHealthcare.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Configuration;

/// <summary>
/// Unit tests for <see cref="AppSettingsValidator"/>.
/// Verifies fail-fast startup validation for all configurable settings.
/// </summary>
public sealed class AppSettingsValidatorTests
{
    private static AppSettingsValidator Validator() => new();

    // ── NoShowRiskThreshold ───────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void Validate_NoShowRiskThreshold_InRange_ReturnsSuccess(int threshold)
    {
        var result = Validator().Validate(null, new AppSettings { NoShowRiskThreshold = threshold });
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Validate_NoShowRiskThreshold_OutOfRange_ReturnsFail(int threshold)
    {
        var result = Validator().Validate(null, new AppSettings { NoShowRiskThreshold = threshold });
        Assert.True(result.Failed);
    }

    // ── CancellationCutoffHours ───────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(72)]
    public void Validate_CancellationCutoffHours_NonNegative_ReturnsSuccess(int hours)
    {
        var result = Validator().Validate(null, new AppSettings { CancellationCutoffHours = hours });
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_CancellationCutoffHours_Negative_ReturnsFail()
    {
        var result = Validator().Validate(null, new AppSettings { CancellationCutoffHours = -1 });
        Assert.True(result.Failed);
        Assert.Contains("CancellationCutoffHours", result.FailureMessage);
    }
}
