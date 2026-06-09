using Microsoft.Extensions.Logging;

namespace ClinicalHealthcare.Infrastructure.Security;

/// <summary>
/// Development-only ClamAV stub that bypasses the real daemon and returns <see cref="ClamAvScanResult.Clean"/>.
/// Activated automatically when the <c>CLAMAV_HOST</c> environment variable is absent.
///
/// WARNING: This service performs NO virus scanning. It must never be used in production.
/// Production environments must set CLAMAV_HOST to enable real ClamAV scanning.
/// </summary>
public sealed class DevClamAvScanService : IClamAvScanService
{
    private readonly ILogger<DevClamAvScanService> _logger;

    public DevClamAvScanService(ILogger<DevClamAvScanService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<ClamAvScanResult> ScanAsync(Stream stream, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "[DevClamAv] CLAMAV_HOST is not set — virus scan BYPASSED. File accepted without scanning. " +
            "Set CLAMAV_HOST in production to enable real ClamAV scanning.");

        return Task.FromResult(ClamAvScanResult.Clean);
    }
}
