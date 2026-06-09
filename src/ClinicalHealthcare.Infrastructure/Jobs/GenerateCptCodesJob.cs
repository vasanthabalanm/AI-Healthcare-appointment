using ClinicalHealthcare.Infrastructure.AI;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicalHealthcare.Infrastructure.Jobs;

/// <summary>
/// Hangfire background job: generates CPT procedure code suggestions for a verified patient
/// using Ollama BioMistral 7B (AIR-004, AC-001).
///
/// Retry policy: 3 attempts at 30 s / 60 s / 120 s intervals.
///
/// Dead-letter: when all retries are exhausted, Hangfire moves the job to "Failed" state and
/// no <see cref="MedicalCodeSuggestion"/> rows are committed (transactional rollback, AC-005 pattern).
/// </summary>
[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 60, 120])]
public sealed class GenerateCptCodesJob
{
    private readonly IOllamaCodeGenerationService   _ollama;
    private readonly ClinicalDbContext             _pgDb;
    private readonly ApplicationDbContext          _appDb;
    private readonly ILogger<GenerateCptCodesJob>  _logger;

    public GenerateCptCodesJob(
        IOllamaCodeGenerationService   ollama,
        ClinicalDbContext              pgDb,
        ApplicationDbContext           appDb,
        ILogger<GenerateCptCodesJob>   logger)
    {
        _ollama = ollama;
        _pgDb   = pgDb;
        _appDb  = appDb;
        _logger = logger;
    }

    /// <summary>
    /// Entry point invoked by Hangfire.
    /// Assembles a brief patient summary, calls Ollama for CPT codes, and inserts validated
    /// <see cref="MedicalCodeSuggestion"/> rows atomically (AC-002).
    /// </summary>
    public async Task ExecuteAsync(int patientId, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "GenerateCptCodesJob starting for patient {PatientId}.", patientId);

        var summary = await BuildPatientSummaryAsync(patientId, ct);

        // Throws on timeout/unavailability — Hangfire retries.
        var suggestions = await _ollama.GenerateCptAsync(summary, ct);

        // AC-003: "No procedures identified" path — zero rows, log info.
        if (suggestions.Count == 0)
        {
            _logger.LogInformation(
                "No CPT suggestions available for patient {PatientId}. Zero rows inserted.", patientId);
            return;
        }

        // Atomic insert — no partial commits on failure.
        await using var tx = await _pgDb.Database.BeginTransactionAsync(ct);
        try
        {
            foreach (var s in suggestions)
            {
                _pgDb.MedicalCodeSuggestions.Add(new MedicalCodeSuggestion
                {
                    PatientId         = patientId,
                    CodeType          = CodeType.CPT,
                    SuggestedCode     = s.SuggestedCode,
                    CodeDescription   = s.CodeDescription,
                    ConfidenceScore   = s.ConfidenceScore,
                    LowConfidenceFlag = s.ConfidenceScore < OllamaCodeGenerationService.LowConfidenceThreshold,
                    Status            = SuggestionStatus.Pending
                });
            }

            await _pgDb.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw; // Re-throw so Hangfire retries / dead-letters.
        }

        _logger.LogInformation(
            "GenerateCptCodesJob inserted {Count} CPT suggestions for patient {PatientId}.",
            suggestions.Count, patientId);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

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
