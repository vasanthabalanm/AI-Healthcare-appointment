namespace ClinicalHealthcare.Infrastructure.Security;

/// <summary>Result of a ClamAV virus scan.</summary>
public enum ClamAvScanResult
{
    Clean    = 0,
    Infected = 1
}

/// <summary>
/// Abstraction for ClamAV virus scanning.
/// Implementations use the nClam TCP client.
/// </summary>
public interface IClamAvScanService
{
    /// <summary>
    /// Scans the provided stream for viruses.
    /// </summary>
    /// <param name="stream">File content to scan. The stream position is NOT reset before scanning.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see cref="ClamAvScanResult.Clean"/> or <see cref="ClamAvScanResult.Infected"/>.</returns>
    /// <exception cref="ClamAvUnavailableException">Thrown when the ClamAV daemon is unreachable or returns an error.</exception>
    Task<ClamAvScanResult> ScanAsync(Stream stream, CancellationToken ct = default);
}
