namespace ClinicalHealthcare.Infrastructure.AI;

/// <summary>
/// Abstracts Ollama BioMistral code generation to allow testability and future provider swap.
/// </summary>
public interface IOllamaCodeGenerationService
{
    Task<IReadOnlyList<CodeSuggestionDto>> GenerateIcd10Async(string patientSummary, CancellationToken ct = default);
    Task<IReadOnlyList<CodeSuggestionDto>> GenerateCptAsync(string patientSummary, CancellationToken ct = default);
}
