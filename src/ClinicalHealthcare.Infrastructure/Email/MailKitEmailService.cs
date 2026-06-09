using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ClinicalHealthcare.Infrastructure.Email;

/// <summary>
/// MailKit SMTPS implementation of <see cref="IEmailService"/>.
///
/// SMTP credentials are read exclusively from environment variables:
///   SMTP_HOST, SMTP_PORT, SMTP_USER, SMTP_PASS, SMTP_FROM_ADDRESS, SMTP_FROM_NAME
///
/// Connection uses <see cref="SecureSocketOptions.SslOnConnect"/> (port 465).
/// For STARTTLS (port 587), change to <see cref="SecureSocketOptions.StartTls"/>.
/// </summary>
public sealed class MailKitEmailService : IEmailService
{
    private readonly string _host;
    private readonly int    _port;
    private readonly string _user;
    private readonly string _pass;
    private readonly string _fromAddress;
    private readonly string _fromName;

    public MailKitEmailService()
    {
        _host        = RequireEnv("SMTP_HOST");
        _port        = int.Parse(RequireEnv("SMTP_PORT"));
        _user        = RequireEnv("SMTP_USER");
        _pass        = RequireEnv("SMTP_PASS");
        _fromAddress = RequireEnv("SMTP_FROM_ADDRESS");
        _fromName    = Environment.GetEnvironmentVariable("SMTP_FROM_NAME") ?? "ClinicalHub";
    }

    /// <inheritdoc />
    public async Task SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_fromName, _fromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;

        var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
        message.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        // Port 465 = implicit SSL; port 587 = STARTTLS; anything else = let MailKit auto-negotiate.
        var socketOptions = _port switch
        {
            465 => SecureSocketOptions.SslOnConnect,
            587 => SecureSocketOptions.StartTls,
            _   => SecureSocketOptions.Auto
        };
        await client.ConnectAsync(_host, _port, socketOptions, cancellationToken);
        await client.AuthenticateAsync(_user, _pass, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }

    private static string RequireEnv(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException(
            $"Required environment variable '{name}' is not set.");
}
