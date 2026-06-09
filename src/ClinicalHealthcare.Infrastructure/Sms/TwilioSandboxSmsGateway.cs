using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ClinicalHealthcare.Infrastructure.Sms;

/// <summary>
/// Twilio sandbox implementation of <see cref="ISmsGateway"/> (AC-004).
///
/// Uses the Twilio REST API directly via <see cref="HttpClient"/> — no Twilio SDK dependency.
/// Credentials are read exclusively from environment variables:
///   TWILIO_ACCOUNT_SID   — Twilio Account SID (starts with "AC")
///   TWILIO_AUTH_TOKEN    — Twilio Auth Token
///   TWILIO_FROM_NUMBER   — Sender number in E.164 format
///
/// For production, swap this registration for a production-grade Twilio implementation
/// that uses the Twilio.AspNet.Core helper or equivalent.
/// </summary>
public sealed class TwilioSandboxSmsGateway : ISmsGateway
{
    private readonly IHttpClientFactory                    _httpFactory;
    private readonly ILogger<TwilioSandboxSmsGateway>     _logger;

    public TwilioSandboxSmsGateway(
        IHttpClientFactory                 httpFactory,
        ILogger<TwilioSandboxSmsGateway>   logger)
    {
        _httpFactory = httpFactory;
        _logger      = logger;
    }

    /// <inheritdoc />
    public async Task SendAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        var accountSid = RequireEnv("TWILIO_ACCOUNT_SID");
        var authToken  = RequireEnv("TWILIO_AUTH_TOKEN");
        var fromNumber = RequireEnv("TWILIO_FROM_NUMBER");

        var url = $"https://api.twilio.com/2010-04-01/Accounts/{accountSid}/Messages.json";

        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("To",   toE164),
            new KeyValuePair<string, string>("From", fromNumber),
            new KeyValuePair<string, string>("Body", body),
        });

        // Use per-request HttpRequestMessage so the Authorization header is never mutated
        // on the shared pooled HttpClient — thread-safe for concurrent Hangfire workers.
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}"));
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = formContent,
        };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        var httpClient = _httpFactory.CreateClient("TwilioSms");
        var response = await httpClient.SendAsync(requestMessage, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "TwilioSandboxSmsGateway: SMS to {To} failed — HTTP {StatusCode}: {Body}",
                toE164, (int)response.StatusCode, error);

            response.EnsureSuccessStatusCode(); // Throw so Hangfire retries the job.
        }

        _logger.LogInformation(
            "TwilioSandboxSmsGateway: SMS sent to {To}.", toE164);
    }

    private static string RequireEnv(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException(
            $"Required environment variable '{name}' is not set.");
}
