using Microsoft.Extensions.Logging;

namespace ClinicalHealthcare.Infrastructure.AI;

/// <summary>
/// Keyword-driven fallback implementation of <see cref="IOllamaCodeGenerationService"/>.
///
/// Used when Ollama (BioMistral) is not available (e.g. local dev without GPU).
/// Maps common clinical keywords in the patient summary to validated ICD-10 and CPT codes
/// with confidence scores derived from match strength.
///
/// No external dependencies — works entirely in-process.
/// </summary>
public sealed class RuleCodeGenerationService : IOllamaCodeGenerationService
{
    private readonly ILogger<RuleCodeGenerationService> _logger;

    public RuleCodeGenerationService(ILogger<RuleCodeGenerationService> logger)
        => _logger = logger;

    // ── ICD-10 keyword map ────────────────────────────────────────────────────
    // Each entry: (keywords[], icd10_code, description, base_confidence)
    private static readonly (string[] Keywords, string Code, string Description, double Confidence)[] IcdMap =
    [
        (["headache", "head pain", "cephalgia"],          "R51.9",  "Headache, unspecified",                                0.82),
        (["migraine"],                                     "G43.909","Migraine, unspecified, not intractable",               0.85),
        (["sore throat", "throat pain", "pharyngitis"],   "J02.9",  "Acute pharyngitis, unspecified",                       0.88),
        (["pain in throat", "throat"],                    "R07.0",  "Pain in throat",                                       0.80),
        (["upper respiratory", "cold", "uri", "runny nose", "congestion"],
                                                           "J06.9",  "Acute upper respiratory infection, unspecified",       0.75),
        (["cough"],                                        "R05.9",  "Cough, unspecified",                                   0.80),
        (["fever", "high temperature", "pyrexia"],        "R50.9",  "Fever, unspecified",                                   0.82),
        (["chest pain", "chest tightness"],               "R07.9",  "Chest pain, unspecified",                              0.78),
        (["shortness of breath", "dyspnea", "breathless"],"R06.00", "Dyspnea, unspecified",                                 0.80),
        (["abdominal pain", "stomach pain", "belly pain"],"R10.9",  "Unspecified abdominal pain",                           0.78),
        (["nausea", "vomiting"],                          "R11.2",  "Nausea with vomiting, unspecified",                    0.80),
        (["diarrhea", "loose stools"],                    "R19.7",  "Diarrhea, unspecified",                                0.78),
        (["back pain", "lower back", "dorsalgia"],        "M54.50", "Low back pain, unspecified",                           0.80),
        (["neck pain", "cervical pain"],                  "M54.2",  "Cervicalgia",                                          0.78),
        (["dizziness", "vertigo", "lightheaded"],         "R42",    "Dizziness and giddiness",                              0.80),
        (["fatigue", "tiredness", "weakness"],            "R53.83", "Other fatigue",                                        0.75),
        (["hypertension", "high blood pressure"],         "I10",    "Essential (primary) hypertension",                     0.88),
        (["diabetes", "diabetic", "blood sugar"],         "E11.9",  "Type 2 diabetes mellitus without complications",       0.85),
        (["asthma"],                                       "J45.901","Unspecified asthma with (acute) exacerbation",         0.85),
        (["anxiety", "anxious", "panic"],                 "F41.9",  "Anxiety disorder, unspecified",                        0.78),
        (["depression", "depressed", "low mood"],         "F32.9",  "Major depressive disorder, single episode, unspecified",0.78),
        (["insomnia", "sleep problem", "cannot sleep"],   "G47.00", "Insomnia, unspecified",                                0.78),
        (["allergy", "allergic", "allergies"],            "T78.40XA","Allergy, unspecified",                                0.72),
        (["rash", "skin rash", "dermatitis"],             "L30.9",  "Dermatitis, unspecified",                              0.75),
        (["urinary", "uti", "dysuria", "frequent urination"],
                                                           "N39.0",  "Urinary tract infection, site not specified",          0.80),
        (["ear pain", "earache", "otitis"],               "H92.09", "Otalgia, unspecified ear",                             0.80),
        (["eye pain", "blurred vision", "eye redness"],  "H57.10", "Ocular pain, unspecified eye",                         0.75),
        (["general", "symptoms", "malaise", "unwell"],   "R68.89", "Other specified general symptoms and signs",            0.58),
    ];

    // ── CPT keyword map ───────────────────────────────────────────────────────
    private static readonly (string[] Keywords, string Code, string Description, double Confidence)[] CptMap =
    [
        (["office", "visit", "consult", "appointment", "clinic",
          "headache", "pain", "fever", "cough", "cold", "follow"],
                                                           "99213",  "Office or outpatient visit, established patient, low-moderate complexity",  0.90),
        (["throat", "pharyngitis", "strep", "tonsil"],    "87880",  "Rapid antigen test, Streptococcus Group A",           0.78),
        (["blood", "lab", "cbc", "complete blood"],       "85027",  "CBC, automated",                                      0.75),
        (["urine", "urinalysis", "uti", "urinary"],       "81003",  "Urinalysis, automated, without microscopy",           0.75),
        (["chest x-ray", "x ray", "xray", "radiograph"], "71046",  "Chest radiograph, 2 views",                           0.78),
        (["ekg", "ecg", "electrocardiogram", "heart"],   "93000",  "Electrocardiogram, routine, with interpretation",     0.75),
        (["blood pressure", "hypertension", "bp check"], "99211",  "Office visit, established patient, minimal complexity",0.72),
        (["glucose", "blood sugar", "diabetes", "hba1c"],"82947",  "Glucose, quantitative, blood (except reagent strip)",  0.75),
        (["culture", "specimen", "swab"],                 "99000",  "Handling/conveyance of specimen for transfer",         0.65),
        (["allergy", "allergic"],                         "95004",  "Percutaneous tests, immediate type reaction",          0.68),
        (["vaccine", "vaccination", "immunization"],      "90471",  "Immunization administration",                         0.72),
        (["vision", "eye exam", "ocular"],                "92002",  "Ophthalmological services, new patient",               0.70),
        (["depression", "anxiety", "mental health", "counseling"],
                                                           "90832",  "Psychotherapy, 30 minutes",                           0.70),
    ];

    /// <inheritdoc/>
    public Task<IReadOnlyList<CodeSuggestionDto>> GenerateIcd10Async(
        string patientSummary, CancellationToken ct = default)
    {
        var results = Match(patientSummary, IcdMap, topN: 6);
        _logger.LogInformation(
            "RuleCodeGenerationService: matched {Count} ICD-10 codes from keyword analysis.", results.Count);
        return Task.FromResult<IReadOnlyList<CodeSuggestionDto>>(results);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<CodeSuggestionDto>> GenerateCptAsync(
        string patientSummary, CancellationToken ct = default)
    {
        var results = Match(patientSummary, CptMap, topN: 4);
        _logger.LogInformation(
            "RuleCodeGenerationService: matched {Count} CPT codes from keyword analysis.", results.Count);
        return Task.FromResult<IReadOnlyList<CodeSuggestionDto>>(results);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<CodeSuggestionDto> Match(
        string summary,
        (string[] Keywords, string Code, string Description, double Confidence)[] map,
        int topN)
    {
        var lower = summary.ToLowerInvariant();

        // Score each rule: sum matched keyword count / total keywords for that rule.
        var scored = new List<(double Score, CodeSuggestionDto Dto)>();
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (keywords, code, description, baseConf) in map)
        {
            int hits = keywords.Count(k => lower.Contains(k, StringComparison.OrdinalIgnoreCase));
            if (hits == 0) continue;
            if (!seenCodes.Add(code)) continue;  // deduplicate

            // Blend base confidence with hit ratio: more matching keywords → higher score.
            double hitRatio = (double)hits / keywords.Length;
            double confidence = Math.Min(0.99, baseConf * (0.85 + 0.15 * hitRatio));

            scored.Add((confidence, new CodeSuggestionDto
            {
                SuggestedCode   = code,
                CodeDescription = description,
                ConfidenceScore = Math.Round(confidence, 2),
            }));
        }

        // Return top-N by confidence, descending.
        return scored
            .OrderByDescending(s => s.Score)
            .Take(topN)
            .Select(s => s.Dto)
            .ToList();
    }
}
