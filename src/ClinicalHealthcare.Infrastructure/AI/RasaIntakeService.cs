using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace ClinicalHealthcare.Infrastructure.AI;

/// <summary>
/// Thrown when the Rasa service is unreachable or returns an HTTP 5xx response (AC-005).
/// Caught at the endpoint layer and returned as HTTP 503.
/// </summary>
public sealed class RasaUnavailableException : Exception
{
    public RasaUnavailableException(string message, Exception? inner = null)
        : base(message, inner) { }
}

/// <summary>
/// Response record from a single Rasa turn.
/// <see cref="Text"/> comes from the REST webhook; <see cref="Confidence"/> from /model/parse.
/// </summary>
public sealed record RasaMessage(string? Text, double Confidence);

/// <summary>
/// Abstraction over the Rasa HTTP proxy — enables test mocking.
/// </summary>
public interface IRasaIntakeService
{
    Task<RasaMessage> SendMessageAsync(string sessionId, string message, CancellationToken ct = default);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="confidence"/> meets or exceeds the
    /// configured threshold (env var <c>AI_EXTRACTION_CONFIDENCE_THRESHOLD</c>, default 0.70).
    /// </summary>
    static bool IsSufficientConfidence(double confidence)
    {
        var raw = Environment.GetEnvironmentVariable("AI_EXTRACTION_CONFIDENCE_THRESHOLD");
        var threshold = double.TryParse(raw,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed) ? parsed : 0.70;
        return confidence >= threshold;
    }
}

/// <summary>
/// HTTP proxy to the Rasa Open Source REST webhook
/// (<c>POST /webhooks/rest/webhook</c>).
///
/// RASA_URL env var (default: http://localhost:5005).
///
/// Security: no auth token required for localhost; add bearer auth env var when
/// deploying behind an API gateway.
/// </summary>
public sealed class RasaIntakeService : IRasaIntakeService
{
    private readonly IHttpClientFactory          _httpFactory;
    private readonly ILogger<RasaIntakeService>  _logger;

    public RasaIntakeService(
        IHttpClientFactory         httpFactory,
        ILogger<RasaIntakeService> logger)
    {
        _httpFactory = httpFactory;
        _logger      = logger;
    }

    /// <summary>
    /// Sends a single message to Rasa and returns the bot reply with NLU confidence.
    ///
    /// Two Rasa calls are made per turn:
    ///   1. <c>POST /model/parse</c> — returns intent confidence (skipped for trigger commands).
    ///   2. <c>POST /webhooks/rest/webhook</c> — returns the bot reply text.
    /// </summary>
    public async Task<RasaMessage> SendMessageAsync(
        string            sessionId,
        string            message,
        CancellationToken ct = default)
    {
        var baseUrl = Environment.GetEnvironmentVariable("RASA_URL")?.TrimEnd('/')
                   ?? "http://localhost:5005";

        try
        {
            var client = _httpFactory.CreateClient("Rasa");

            // Step 1: NLU confidence via /model/parse.
            // Trigger commands (e.g. /greet) bypass NLU — treat as full confidence.
            var confidence = message.StartsWith('/')
                ? 1.0
                : await GetNluConfidenceAsync(client, baseUrl, message, sessionId, ct);

            // Step 2: Bot reply from REST webhook.
            var replyText = await SendToWebhookAsync(client, baseUrl, sessionId, message, ct);

            return new RasaMessage(replyText, confidence);
        }
        catch (RasaUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogError(ex, "Rasa unreachable for session {SessionId}.", sessionId);
            throw new RasaUnavailableException("AI intake service unavailable.", ex);
        }
    }

    private async Task<double> GetNluConfidenceAsync(
        HttpClient        client,
        string            baseUrl,
        string            message,
        string            sessionId,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/model/parse")
        {
            Content = JsonContent.Create(new { text = message })
        };

        using var response = await client.SendAsync(req, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Rasa /model/parse returned HTTP {StatusCode} for session {SessionId}.",
                (int)response.StatusCode, sessionId);
            throw new RasaUnavailableException(
                $"Rasa /model/parse returned HTTP {(int)response.StatusCode}.");
        }

        var parsed = await response.Content
            .ReadFromJsonAsync<ParseResponse>(cancellationToken: ct);
        return parsed?.Intent?.Confidence ?? 0.0;
    }

    private async Task<string?> SendToWebhookAsync(
        HttpClient        client,
        string            baseUrl,
        string            sessionId,
        string            message,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"{baseUrl}/webhooks/rest/webhook")
        {
            Content = JsonContent.Create(new { sender = sessionId, message })
        };

        using var response = await client.SendAsync(req, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Rasa webhook returned HTTP {StatusCode} for session {SessionId}.",
                (int)response.StatusCode, sessionId);
            throw new RasaUnavailableException(
                $"Rasa webhook returned HTTP {(int)response.StatusCode}.");
        }

        var messages = await response.Content
            .ReadFromJsonAsync<WebhookResponse[]>(cancellationToken: ct);

        if (messages is null || messages.Length == 0)
        {
            _logger.LogWarning(
                "Rasa returned empty webhook response for session {SessionId}.", sessionId);
            return "I didn't understand that. Could you rephrase?";
        }

        return messages[0].Text;
    }

    // ── Private DTOs for Rasa API response deserialization ───────────────────

    private sealed record ParseResponse(
        [property: JsonPropertyName("intent")] IntentInfo? Intent);

    private sealed record IntentInfo(
        [property: JsonPropertyName("confidence")] double Confidence);

    private sealed record WebhookResponse(
        [property: JsonPropertyName("text")] string? Text);
}
