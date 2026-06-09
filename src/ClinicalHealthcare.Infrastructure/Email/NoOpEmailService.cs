namespace ClinicalHealthcare.Infrastructure.Email;

/// <summary>
/// No-op email stub registered until MailKit is wired in US_026.
/// Satisfies the <see cref="IEmailService"/> DI contract without requiring SMTP credentials.
/// </summary>
public sealed class NoOpEmailService : IEmailService
{
    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
