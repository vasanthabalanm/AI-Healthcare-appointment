using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace ClinicalHealthcare.Infrastructure.Email;

/// <summary>
/// Development-only email service that logs email content to the console instead of sending.
/// The verification/reset URL is extracted from the HTML body and printed clearly so developers
/// can copy it directly from the terminal without needing an SMTP server or mail client.
/// Also stores last email per recipient for E2E test retrieval.
/// </summary>
public sealed class DevEmailService : IEmailService
{
    private static readonly Regex HrefPattern =
        new(@"href=""([^""]+)""", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>Stores the last email URL sent to each recipient (for E2E testing).</summary>
    private static readonly ConcurrentDictionary<string, DevEmailRecord> LastEmails = new();

    private readonly ILogger<DevEmailService> _logger;

    public DevEmailService(ILogger<DevEmailService> logger)
    {
        _logger = logger;
    }

    /// <summary>Gets the last email URL sent to the specified recipient.</summary>
    public static DevEmailRecord? GetLastEmail(string email)
        => LastEmails.TryGetValue(email.ToLowerInvariant(), out var record) ? record : null;

    /// <summary>Clears all stored emails (call between test runs).</summary>
    public static void ClearAll() => LastEmails.Clear();

    public Task SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        var match = HrefPattern.Match(htmlBody);
        var url   = match.Success ? match.Groups[1].Value : "(no URL found in body)";

        // Store for E2E test retrieval
        var normalizedEmail = toEmail.ToLowerInvariant();
        LastEmails[normalizedEmail] = new DevEmailRecord(toEmail, subject, url, DateTime.UtcNow);

        _logger.LogInformation(
            """

            ╔══════════════════════════════════════════════════════════════╗
            ║  [DevEmail] Email captured (not sent)                        ║
            ╠══════════════════════════════════════════════════════════════╣
            ║  To      : {To}
            ║  Subject : {Subject}
            ╠══════════════════════════════════════════════════════════════╣
            ║  ACTION URL (copy and open in browser):
            ║  {Url}
            ╚══════════════════════════════════════════════════════════════╝
            """,
            toEmail, subject, url);

        return Task.CompletedTask;
    }
}

/// <summary>Record of a dev email for E2E test retrieval.</summary>
public sealed record DevEmailRecord(string To, string Subject, string ActionUrl, DateTime SentAt);
