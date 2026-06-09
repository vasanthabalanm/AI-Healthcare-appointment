using System.Text.RegularExpressions;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.Extensions.Logging;

namespace ClinicalHealthcare.Infrastructure.NLP;

/// <summary>
/// Rule-based NLP pipeline that extracts structured clinical fields from OCR text
/// (AC-001 — TASK_041).
///
/// Patterns cover the five mandated types:
///   • <see cref="ClinicalFieldType.VitalSign"/>      — BP, HR, temperature, weight, height
///   • <see cref="ClinicalFieldType.MedicalHistory"/>  — ICD-10 codes, chronic condition keywords
///   • <see cref="ClinicalFieldType.Medication"/>      — drug name + dosage
///   • <see cref="ClinicalFieldType.Allergy"/>         — allergy/reaction keywords
///   • <see cref="ClinicalFieldType.Diagnosis"/>       — Diagnosis: / Assessment: prefixes
///
/// Unrecognised lines are logged at DEBUG level; no Unknown-type rows are produced (AC-003).
/// </summary>
public sealed class ClinicalFieldExtractor
{
    // ── Compiled patterns ─────────────────────────────────────────────────────

    // VitalSign: BP / blood pressure, HR / heart rate / pulse, temperature, weight, height, O2Sat
    private static readonly Regex VitalSignPattern = new(
        @"(?:(?:bp|blood\s*pressure)\s*[:\-]?\s*\d{2,3}\s*/\s*\d{2,3}(?:\s*mmhg)?)" +
        @"|(?:(?:hr|heart\s*rate|pulse)\s*[:\-]?\s*\d{2,3}(?:\s*bpm)?)" +
        @"|(?:temp(?:erature)?\s*[:\-]?\s*\d{2,3}(?:\.\d)?\s*(?:°?[cf])?)" +
        @"|(?:weight\s*[:\-]?\s*\d+(?:\.\d)?\s*(?:kg|lbs?))" +
        @"|(?:height\s*[:\-]?\s*\d+(?:'\d+""|\s*cm|\s*m))" +
        @"|(?:(?:spo2|o2\s*sat(?:uration)?)\s*[:\-]?\s*\d{2,3}\s*%?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // MedicalHistory: ICD-10 codes (A00–Z99 formats) or chronic condition keywords
    private static readonly Regex MedicalHistoryPattern = new(
        @"(?:[A-Z]\d{2}(?:\.\d{1,4})?(?:\s*–[A-Z]\d{2}(?:\.\d{1,4})?)?)" +         // ICD-10
        @"|(?:(?:history\s+of|h/o|hx:?)\s+[\w\s,]+)" +                               // history of ...
        @"|(?:(?:type\s+[12]|type\s+ii?)\s+diabetes)" +
        @"|(?:hypertension|hypertensive|coronary\s+artery|heart\s+failure|copd|asthma" +
        @"|chronic\s+kidney|renal\s+failure|stroke|atrial\s+fibrillation|hypothyroidism)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Medication: drug name + dosage, with or without "Medications:" list header.
    // Must precede MedicalHistory in Rules to avoid medication lines being classified as history.
    private static readonly Regex MedicationPattern = new(
        @"(?:medications?\s*[:\-]\s*)?" +
        @"([\w][\w\s\-]*?\d+(?:\.\d+)?\s*(?:mg|mcg|g|iu|units?)" +
        @"(?:\s*(?:po|iv|im|sc|sl|td|topical|inhaled|oral|intravenous))?" +
        @"(?:\s*(?:od|bd|tds|qds|once|twice|daily|twice\s+daily|three\s+times|\d+\s*times?))?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Allergy: explicit allergy keyword followed by substance or reaction
    private static readonly Regex AllergyPattern = new(
        @"(?:allerg(?:y|ies|ic)\s+to\s+[\w\s,;]+)" +
        @"|(?:nkda|no\s+known\s+drug\s+allerg(?:y|ies))" +
        @"|(?:reaction\s+to\s+[\w\s]+)" +
        @"|(?:allerg(?:y|ies)\s*[:\-]\s*[\w\s,;]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Diagnosis: explicit label followed by text
    private static readonly Regex DiagnosisPattern = new(
        @"(?:diagnosis\s*[:\-]\s*[\w\s,;\.]+)" +
        @"|(?:assessment\s*[:\-]\s*[\w\s,;\.]+)" +
        @"|(?:impression\s*[:\-]\s*[\w\s,;\.]+)" +
        @"|(?:clinical\s+diagnosis\s*[:\-]\s*[\w\s,;\.]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── Ordered list of (pattern, fieldType, fieldName) tuples ───────────────
    // Evaluated in order; first match wins for each segment.
    private static readonly IReadOnlyList<(Regex Pattern, ClinicalFieldType Type, string Name)> Rules =
    [
        (VitalSignPattern,      ClinicalFieldType.VitalSign,      "VitalSign"),
        (AllergyPattern,        ClinicalFieldType.Allergy,        "Allergy"),
        (MedicationPattern,     ClinicalFieldType.Medication,     "Medication"),
        (DiagnosisPattern,      ClinicalFieldType.Diagnosis,      "Diagnosis"),
        (MedicalHistoryPattern, ClinicalFieldType.MedicalHistory, "MedicalHistory"),
    ];

    private readonly ILogger<ClinicalFieldExtractor> _logger;

    public ClinicalFieldExtractor(ILogger<ClinicalFieldExtractor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Extracts structured clinical fields from raw OCR text (AC-001, AC-002).
    /// Returns an empty list when <paramref name="rawOcrText"/> is null or whitespace.
    /// Unrecognised lines are logged at DEBUG level; no Unknown-type rows produced (AC-003).
    /// </summary>
    /// <param name="rawOcrText">Extracted OCR text from <see cref="Entities.ClinicalDocument.RawOcrText"/>.</param>
    /// <returns>List of extracted field DTOs ready for persistence.</returns>
    public IList<ExtractedFieldDto> Extract(string rawOcrText)
    {
        if (string.IsNullOrWhiteSpace(rawOcrText))
            return [];

        var results = new List<ExtractedFieldDto>();

        // Work line-by-line; a single line may yield multiple matches.
        foreach (var line in rawOcrText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed      = line.Trim();
            var lineMatched  = false;

            foreach (var (pattern, fieldType, fieldName) in Rules)
            {
                var match = pattern.Match(trimmed);
                if (!match.Success)
                    continue;

                var value = match.Value.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    results.Add(new ExtractedFieldDto(fieldType, fieldName, value));
                    lineMatched = true;
                    break; // first-rule-wins: one field type per input line
                }
            }

            if (!lineMatched && !string.IsNullOrWhiteSpace(trimmed))
            {
                // AC-003: log unrecognised text at DEBUG; never insert Unknown-type rows.
                _logger.LogDebug(
                    "ClinicalFieldExtractor: unrecognised text — '{Line}'", trimmed);
            }
        }

        return results;
    }
}
