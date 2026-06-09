using Microsoft.Extensions.Logging;

namespace ClinicalHealthcare.Infrastructure.AI;

/// <summary>
/// Tries Ollama first; falls back to <see cref="RuleCodeGenerationService"/> when
/// Ollama is unreachable or returns an error.
///
/// This lets the system work in dev environments without a local Ollama installation,
/// while automatically upgrading to AI-generated codes when Ollama becomes available.
/// </summary>
public sealed class FallbackCodeGenerationService : IOllamaCodeGenerationService
{
    private readonly OllamaCodeGenerationService    _ollama;
    private readonly RuleCodeGenerationService      _rules;
    private readonly ILogger<FallbackCodeGenerationService> _logger;

    public FallbackCodeGenerationService(
        OllamaCodeGenerationService            ollama,
        RuleCodeGenerationService              rules,
        ILogger<FallbackCodeGenerationService> logger)
    {
        _ollama = ollama;
        _rules  = rules;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CodeSuggestionDto>> GenerateIcd10Async(
        string patientSummary, CancellationToken ct = default)
    {
        try
        {
            var result = await _ollama.GenerateIcd10Async(patientSummary, ct);
            if (result.Count > 0) return result;

            _logger.LogWarning("Ollama returned 0 ICD-10 suggestions — falling back to rule-based service.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama unavailable for ICD-10 generation — falling back to rule-based service.");
        }

        return await _rules.GenerateIcd10Async(patientSummary, ct);
    }

    public async Task<IReadOnlyList<CodeSuggestionDto>> GenerateCptAsync(
        string patientSummary, CancellationToken ct = default)
    {
        try
        {
            var result = await _ollama.GenerateCptAsync(patientSummary, ct);
            if (result.Count > 0) return result;

            _logger.LogWarning("Ollama returned 0 CPT suggestions — falling back to rule-based service.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama unavailable for CPT generation — falling back to rule-based service.");
        }

        return await _rules.GenerateCptAsync(patientSummary, ct);
    }
}
