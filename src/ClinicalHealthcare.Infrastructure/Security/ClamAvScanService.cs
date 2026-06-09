using nClam;

namespace ClinicalHealthcare.Infrastructure.Security;

/// <summary>
/// ClamAV virus scan service that connects to the ClamAV daemon over TCP using nClam.
///
/// Configuration (environment variables):
///   CLAMAV_HOST — ClamAV daemon hostname (default: localhost)
///   CLAMAV_PORT — ClamAV daemon port     (default: 3310)
///
/// AC-002: ClamAV unavailable → throws <see cref="ClamAvUnavailableException"/>; never bypasses the scan.
/// </summary>
public sealed class ClamAvScanService : IClamAvScanService
{
    private readonly string _host;
    private readonly int    _port;

    public ClamAvScanService()
    {
        _host = Environment.GetEnvironmentVariable("CLAMAV_HOST") ?? "localhost";
        _port = int.TryParse(Environment.GetEnvironmentVariable("CLAMAV_PORT"), out var p) ? p : 3310;
    }

    /// <inheritdoc/>
    public async Task<ClamAvScanResult> ScanAsync(Stream stream, CancellationToken ct = default)
    {
        try
        {
            var client = new ClamClient(_host, _port);
            var result = await client.SendAndScanFileAsync(stream);

            return result.Result switch
            {
                ClamScanResults.VirusDetected => ClamAvScanResult.Infected,
                ClamScanResults.Clean         => ClamAvScanResult.Clean,
                // Error or Unknown — treat as unavailable so the upload is never silently accepted.
                _ => throw new ClamAvUnavailableException($"ClamAV returned an unexpected result: {result.Result}.")
            };
        }
        catch (ClamAvUnavailableException)
        {
            // Re-throw as-is; do not wrap in another ClamAvUnavailableException.
            throw;
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException
                                       or System.IO.IOException
                                       or TimeoutException)
        {
            throw new ClamAvUnavailableException("ClamAV daemon is unreachable.", ex);
        }
    }
}
