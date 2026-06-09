using ClinicalHealthcare.Infrastructure.AI;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicalHealthcare.Infrastructure.Jobs;

/// <summary>
/// Hangfire background job: generates ICD-10 code suggestions for a verified patient
/// using Ollama BioMistral 7B (AIR-003, AC-002).
///
/// Retry policy: 3 attempts at 30 s / 60 s / 120 s intervals (AC-004).
/// The [AutomaticRetry] attribute here overrides the global filter for this job type only.
///
/// Dead-letter (AC-005): when all retries are exhausted, Hangfire moves the job to
/// "Failed" state and no <see cref="MedicalCodeSuggestion"/> rows are committed,
/// because the transaction is only committed on success.
/// </summary>
[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 60, 120])]
public sealed class GenerateIcd10CodesJob
{
    private readonly IOllamaCodeGenerationService   _ollama;
    private readonly ClinicalDbContext              _pgDb;
    private readonly ApplicationDbContext           _appDb;
    private readonly ILogger<GenerateIcd10CodesJob> _logger;

    public GenerateIcd10CodesJob(
        IOllamaCodeGenerationService   ollama,
        ClinicalDbContext              pgDb,
        ApplicationDbContext           appDb,
        ILogger<GenerateIcd10CodesJob> logger)
    {
        _ollama = ollama;
        _pgDb   = pgDb;
        _appDb  = appDb;
        _logger = logger;
    }

    /// <summary>
    /// Entry point invoked by Hangfire.
    /// Assembles a brief patient summary, calls Ollama, and inserts validated
    /// <see cref="MedicalCodeSuggestion"/> rows atomically (AC-002, AC-005).
    /// </summary>
    public async Task ExecuteAsync(int patientId, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "GenerateIcd10CodesJob starting for patient {PatientId}.", patientId);

        // Build a compact clinical summary for the Ollama prompt.
        var summary = await BuildPatientSummaryAsync(patientId, ct);

        // Call Ollama — throws on timeout/unavailability so Hangfire retries (AC-004).
        var suggestions = await _ollama.GenerateIcd10Async(summary, ct);

        if (suggestions.Count == 0)
        {
            _logger.LogWarning(
                "Ollama returned 0 valid ICD-10 suggestions for patient {PatientId}.", patientId);
            return;
        }

        // AC-005: insert all rows in a single transaction — no partial commits.
        await using var tx = await _pgDb.Database.BeginTransactionAsync(ct);
        try
        {
            foreach (var s in suggestions)
            {
                _pgDb.MedicalCodeSuggestions.Add(new MedicalCodeSuggestion
                {
                    PatientId         = patientId,
                    CodeType          = CodeType.ICD10,
                    SuggestedCode     = s.SuggestedCode,
                    CodeDescription   = s.CodeDescription,
                    ConfidenceScore   = s.ConfidenceScore,
                    LowConfidenceFlag = s.ConfidenceScore < OllamaCodeGenerationService.LowConfidenceThreshold,   // AC-003
                    Status            = SuggestionStatus.Pending
                });
            }

            await _pgDb.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw; // Re-throw so Hangfire retries (AC-004) / dead-letters (AC-005).
        }

        _logger.LogInformation(
            "GenerateIcd10CodesJob inserted {Count} ICD-10 suggestions for patient {PatientId}.",
            suggestions.Count, patientId);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Assembles a brief plain-text summary from <see cref="ExtractedClinicalField"/> rows.
    /// Used as the Ollama prompt body.
    /// </summary>
    private async Task<string> BuildPatientSummaryAsync(int patientId, CancellationToken ct)
    {
        var fields = await _pgDb.ExtractedClinicalFields
            .Where(f => f.PatientId == patientId)
            .OrderByDescending(f => f.ExtractedAt)
            .Take(50)
            .Select(f => $"{f.FieldName}: {f.FieldValue}")
            .ToListAsync(ct);

        if (fields.Count > 0)
            return string.Join('\n', fields);

        // Fallback: use the patient's latest intake record when no clinical fields exist.
        var intake = await _appDb.IntakeRecords
            .Where(r => r.PatientId == patientId)
            .OrderByDescending(r => r.SubmittedAt)
            .FirstOrDefaultAsync(ct);

        if (intake is null)
            return $"Patient ID {patientId} — no clinical data available.";

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(intake.ChiefComplaint))  parts.Add($"Chief Complaint: {intake.ChiefComplaint}");
        if (!string.IsNullOrWhiteSpace(intake.CurrentMeds))     parts.Add($"Current Medications: {intake.CurrentMeds}");
        if (!string.IsNullOrWhiteSpace(intake.Allergies))       parts.Add($"Allergies: {intake.Allergies}");
        if (!string.IsNullOrWhiteSpace(intake.MedicalHistory))  parts.Add($"Medical History: {intake.MedicalHistory}");

        return parts.Count > 0
            ? string.Join('\n', parts)
            : $"Patient ID {patientId} — no clinical data available.";
    }
}
