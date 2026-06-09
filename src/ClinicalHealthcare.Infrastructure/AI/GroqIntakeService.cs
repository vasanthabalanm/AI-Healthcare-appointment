using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ClinicalHealthcare.Infrastructure.Cache;
using Microsoft.Extensions.Logging;

namespace ClinicalHealthcare.Infrastructure.AI;

/// <summary>
/// Single-turn in a Groq conversation — stored alongside the intake session so
/// subsequent turns have context.
/// </summary>
public sealed record GroqChatTurn(string User, string Assistant);

/// <summary>
/// OpenAI-compatible <see cref="IRasaIntakeService"/> implementation that routes
/// conversational intake through the Groq API (llama-3.3-70b-versatile by default).
///
/// Environment variables consumed:
///   OPENAI_BASE_URL   — defaults to https://api.groq.com/openai
///   OPENAI_API_KEY    — required
///   OPENAI_CHAT_MODEL — defaults to llama-3.3-70b-versatile
/// </summary>
public sealed class GroqIntakeService : IRasaIntakeService
{
    private readonly ICacheService                _cache;
    private readonly IHttpClientFactory           _http;
    private readonly ILogger<GroqIntakeService>   _logger;

    private const string HistoryKeyPrefix = "groq-history:";
    private static readonly TimeSpan HistoryTtl = TimeSpan.FromSeconds(900);

    private const string SystemPrompt =
        """
        You are a compassionate, professional healthcare intake assistant for ClinicalHub clinic.
        Your role is to collect pre-appointment health information from patients through friendly conversation.

        Collect information in this order:
        1. Chief complaint — reason for visit, main symptoms, how long, severity (1-10)
        2. Current medications — name, dose, frequency (say "none" if none)
        3. Known allergies — medications, foods, environmental (say "none" if none)
        4. Relevant medical history — previous conditions, surgeries

        Rules:
        - Ask about ONE topic at a time, then wait for the patient's response
        - Be empathetic and conversational (not clinical or robotic)
        - If the patient's answer is unclear, ask one concise clarifying follow-up question
        - Once you have collected all 4 topics, briefly summarise what you've gathered, then
          end your response with exactly the token: INTAKE_COMPLETE
        - Keep every response to 2–3 sentences maximum
        - Do NOT ask the patient's name or date of birth — you already have those on file
        """;

    public GroqIntakeService(
        ICacheService                cache,
        IHttpClientFactory           http,
        ILogger<GroqIntakeService>   logger)
    {
        _cache  = cache;
        _http   = http;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<RasaMessage> SendMessageAsync(
        string            sessionId,
        string            message,
        CancellationToken ct = default)
    {
        var historyKey = $"{HistoryKeyPrefix}{sessionId}";
        var history    = await _cache.GetAsync<List<GroqChatTurn>>(historyKey, ct)
                         ?? new List<GroqChatTurn>();

        // Build OpenAI-format messages list.
        var messages = new List<object>
        {
            new { role = "system", content = SystemPrompt }
        };

        foreach (var turn in history)
        {
            messages.Add(new { role = "user",      content = turn.User      });
            messages.Add(new { role = "assistant", content = turn.Assistant });
        }

        // Translate Rasa-style trigger commands to natural language.
        var userMessage = message.StartsWith('/')
            ? "Hello, I'd like to start my intake form."
            : message;

        messages.Add(new { role = "user", content = userMessage });

        string responseText;
        try
        {
            responseText = await CallGroqAsync(messages, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Groq API unreachable for intake session {SessionId}.", sessionId);
            throw new RasaUnavailableException("AI intake service unavailable.", ex);
        }

        // Persist updated history (best-effort — NullCache silently drops it).
        history.Add(new GroqChatTurn(userMessage, responseText));
        await _cache.SetAsync(historyKey, history, HistoryTtl, ct);

        // INTAKE_COMPLETE signals the conversation is finished — return max confidence.
        var isComplete = responseText.Contains("INTAKE_COMPLETE", StringComparison.OrdinalIgnoreCase);
        var cleanText  = responseText
            .Replace("INTAKE_COMPLETE", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return new RasaMessage(cleanText, isComplete ? 1.0 : 0.85);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private async Task<string> CallGroqAsync(List<object> messages, CancellationToken ct)
    {
        var baseUrl = (Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "https://api.groq.com/openai")
                      .TrimEnd('/');
        var apiKey  = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
        var model   = Environment.GetEnvironmentVariable("OPENAI_CHAT_MODEL") ?? "llama-3.3-70b-versatile";

        var client = _http.CreateClient("Groq");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new
        {
            model,
            messages,
            max_tokens  = 300,
            temperature = 0.7,
        };

        var response = await client.PostAsJsonAsync($"{baseUrl}/v1/chat/completions", payload, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GroqApiResponse>(cancellationToken: ct);
        return result?.Choices?[0]?.Message?.Content
               ?? "I'm here to help you complete your intake form. Could you start by telling me the reason for your visit today?";
    }

    // ── JSON response shapes (file-scoped for minimal surface) ────────────────

    private sealed record GroqApiResponse(
        [property: JsonPropertyName("choices")] List<GroqApiChoice>? Choices);

    private sealed record GroqApiChoice(
        [property: JsonPropertyName("message")] GroqApiMessage? Message);

    private sealed record GroqApiMessage(
        [property: JsonPropertyName("content")] string? Content);
}
