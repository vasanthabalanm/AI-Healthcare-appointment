using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace ClinicalHealthcare.Infrastructure.AI;

/// <summary>
/// DTO produced by <see cref="OllamaCodeGenerationService"/> for each valid suggestion.
/// </summary>
public sealed class CodeSuggestionDto
{
    public string SuggestedCode    { get; init; } = string.Empty;
    public string CodeDescription  { get; init; } = string.Empty;
    public double ConfidenceScore  { get; init; }
}

/// <summary>
/// Calls Ollama BioMistral 7B Q4_K_M to generate ICD-10 and CPT code suggestions
/// for an assembled 360° patient view (AIR-003, AIR-004).
///
/// Rules:
///   - Timeout: 30 s per request.
///   - ICD-10: invalid codes (fails <c>[A-Z][0-9]{2}(\.[0-9]{1,4})?</c>) → rejected + WARNING logged.
///   - CPT: invalid codes (fails <c>^\d{5}$</c>) → rejected + WARNING logged.
///   - Max 20 suggestions; extras silently dropped + DEBUG logged.
///   - Throws <see cref="HttpRequestException"/> or <see cref="TaskCanceledException"/> on
///     transport errors so Hangfire can retry (AC-004). Does not swallow transport failures.
/// </summary>
public sealed class OllamaCodeGenerationService : IOllamaCodeGenerationService
{
    // AIR-003: ICD-10 code format — one uppercase letter followed by two digits, optional decimal subdivision.
    private static readonly Regex IcdCodeRegex =
        new(@"^[A-Z][0-9]{2}(\.[0-9]{1,4})?$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    // AIR-004: CPT code format — exactly five digits.
    private static readonly Regex CptCodeRegex =
        new(@"^\d{5}$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    private const int    MaxSuggestions          = 20;
    /// <summary>Confidence threshold below which <see cref="MedicalCodeSuggestion.LowConfidenceFlag"/> is set (AC-003).</summary>
    internal const double LowConfidenceThreshold = 0.60;
    private const string OllamaEndpoint          = "api/chat";   // Relative — BaseAddress set in DI (Program.cs).
    private const string OllamaModel            = "biomistral";

    private const string CptSystemPrompt =
        "You are a clinical procedure coding assistant. Return ONLY a JSON array of objects with fields: " +
        "code (5-digit CPT string), description (string), confidence (float 0-1). " +
        "If no procedures are identified, return an empty JSON array []. No explanation text.";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<OllamaCodeGenerationService> _logger;

    public OllamaCodeGenerationService(
        IHttpClientFactory httpFactory,
        ILogger<OllamaCodeGenerationService> logger)
    {
        _httpFactory = httpFactory;
        _logger      = logger;
    }

    /// <summary>
    /// Calls Ollama and returns up to 20 validated ICD-10 code suggestions.
    /// Returns an empty list if Ollama is unavailable or returns no valid codes.
    /// </summary>
    public async Task<IReadOnlyList<CodeSuggestionDto>> GenerateIcd10Async(
        string patientSummary,
        CancellationToken ct = default)
    {
        using var client = _httpFactory.CreateClient("Ollama");

        var requestBody = new
        {
            model    = OllamaModel,
            messages = new[]
            {
                new
                {
                    role    = "system",
                    content = "You are a clinical coding assistant. Return ONLY a JSON array of objects with fields: code (ICD-10 string), description (string), confidence (float 0-1). No explanation text."
                },
                new
                {
                    role    = "user",
                    content = $"Generate ICD-10 codes for this patient summary:\n\n{patientSummary}"
                }
            },
            stream = false
        };

        string responseContent;
        try
        {
            using var cts     = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            using var response = await client.PostAsJsonAsync(OllamaEndpoint, requestBody, cts.Token);
            response.EnsureSuccessStatusCode();
            responseContent = await response.Content.ReadAsStringAsync(cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Ollama unavailable or timed out generating ICD-10 codes.");
            throw; // Let the Hangfire retry policy handle it.
        }

        return ParseAndValidate(responseContent);
    }

    /// <summary>
    /// Calls Ollama and returns up to 20 validated CPT code suggestions (AIR-004).
    /// Returns an empty list if Ollama returns no valid codes or the patient has no procedures.
    /// Throws on transport errors so Hangfire can retry (AC-004).
    /// </summary>
    public async Task<IReadOnlyList<CodeSuggestionDto>> GenerateCptAsync(
        string patientSummary,
        CancellationToken ct = default)
    {
        using var client = _httpFactory.CreateClient("Ollama");

        var requestBody = new
        {
            model    = OllamaModel,
            messages = new[]
            {
                new { role = "system", content = CptSystemPrompt },
                new { role = "user",   content = $"Generate CPT procedure codes for this patient summary:\n\n{patientSummary}" }
            },
            stream = false
        };

        string responseContent;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            using var response = await client.PostAsJsonAsync(OllamaEndpoint, requestBody, cts.Token);
            response.EnsureSuccessStatusCode();
            responseContent = await response.Content.ReadAsStringAsync(cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Ollama unavailable or timed out generating CPT codes.");
            throw;
        }

        return ParseAndValidate(responseContent, CptCodeRegex, "CPT");
    }

    // ── Parsing ───────────────────────────────────────────────────────────────

    private IReadOnlyList<CodeSuggestionDto> ParseAndValidate(string responseContent) =>
        ParseAndValidate(responseContent, IcdCodeRegex, "ICD-10");

    private IReadOnlyList<CodeSuggestionDto> ParseAndValidate(string responseContent, Regex codeRegex, string codeTypeName)
    {
        // Extract the assistant message content from the Ollama chat response.
        string? messageContent = null;
        try
        {
            using var doc      = JsonDocument.Parse(responseContent);
            var root           = doc.RootElement;
            messageContent     = root.GetProperty("message").GetProperty("content").GetString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not parse Ollama chat response envelope.");
            return [];
        }

        if (string.IsNullOrWhiteSpace(messageContent))
            return [];

        // The assistant returns a JSON array; find the first '[' to skip any preamble.
        var jsonStart = messageContent.IndexOf('[');
        var jsonEnd   = messageContent.LastIndexOf(']');
        if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart)
        {
            _logger.LogWarning("Ollama response did not contain a JSON array.");
            return [];
        }

        var jsonArray = messageContent[jsonStart..(jsonEnd + 1)];

        OllamaCodeItem[]? items;
        try
        {
            items = JsonSerializer.Deserialize<OllamaCodeItem[]>(
                jsonArray,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize Ollama code array.");
            return [];
        }

        if (items is null || items.Length == 0)
            return [];

        var results = new List<CodeSuggestionDto>(Math.Min(items.Length, MaxSuggestions));

        foreach (var item in items)
        {
            if (results.Count >= MaxSuggestions)
            {
                _logger.LogDebug(
                    "Ollama returned more than {Max} suggestions. Dropping remainder.", MaxSuggestions);
                break;
            }

            var code = item.Code?.Trim() ?? string.Empty;
            if (!codeRegex.IsMatch(code))
            {
                _logger.LogWarning("Rejecting malformed {CodeType} code from Ollama: '{Code}'.", codeTypeName, code);
                continue;
            }

            var confidence = Math.Clamp(item.Confidence, 0.0, 1.0);

            results.Add(new CodeSuggestionDto
            {
                SuggestedCode   = code,
                CodeDescription = item.Description ?? string.Empty,
                ConfidenceScore = confidence
            });
        }

        return results;
    }

    // ── Internal deserialization target ──────────────────────────────────────

    private sealed class OllamaCodeItem
    {
        [JsonPropertyName("code")]
        public string? Code { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("confidence")]
        public double Confidence { get; init; }
    }
}
