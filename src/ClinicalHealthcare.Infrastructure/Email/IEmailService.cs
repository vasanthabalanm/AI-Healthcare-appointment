namespace ClinicalHealthcare.Infrastructure.Email;

/// <summary>
/// Abstraction for transactional email delivery.
/// Decouples feature code from the MailKit implementation so tests can substitute a no-op fake.
/// </summary>
public interface IEmailService
{
    /// <summary>Sends a plain-text + HTML email to the specified recipient.</summary>
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default);
}
