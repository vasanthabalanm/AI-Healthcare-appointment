namespace ClinicalHealthcare.Infrastructure.Security;

/// <summary>
/// Thrown when the ClamAV daemon is unreachable or returns a scan error.
/// The caller MUST treat this as a 503 — never silently bypass the scan (AC-002).
/// </summary>
public sealed class ClamAvUnavailableException : Exception
{
    public ClamAvUnavailableException(string message, Exception? inner = null)
        : base(message, inner) { }
}
