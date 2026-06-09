namespace ClinicalHealthcare.Infrastructure.Sms;

/// <summary>
/// Abstraction over an SMS delivery provider (AC-004).
/// The production implementation is <see cref="TwilioSandboxSmsGateway"/>.
/// Implementations MUST be idempotent — Hangfire may call this multiple times on retry.
/// </summary>
public interface ISmsGateway
{
    /// <summary>
    /// Sends an SMS message to the given phone number.
    /// </summary>
    /// <param name="toE164">Destination phone number in E.164 format (e.g. "+14155552671").</param>
    /// <param name="body">Message body text (max 160 chars recommended).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendAsync(string toE164, string body, CancellationToken cancellationToken = default);
}
