using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.NLP;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicalHealthcare.Infrastructure.Jobs;

/// <summary>
/// Hangfire background job that extracts structured clinical fields from the OCR text of a
/// <see cref="ClinicalDocument"/> (AC-001, AC-004 — TASK_041).
///
/// Pipeline:
///   1. Load <see cref="ClinicalDocument"/> from <c>ApplicationDbContext</c> (SQL Server).
///   2. If <see cref="OcrStatus.NoData"/> (or document missing) → return without inserting rows.
///   3. Call <see cref="ClinicalFieldExtractor.Extract"/> → <see cref="IList{ExtractedFieldDto}"/>.
///   4. Insert <see cref="ExtractedClinicalField"/> rows in <c>ClinicalDbContext</c> (PostgreSQL).
///   5. Enqueue <c>DeduplicateClinicalFieldsJob</c> (us_042) for the patient.
/// </summary>
public sealed class ExtractClinicalFieldsJob
{
    private readonly ApplicationDbContext         _sqlDb;
    private readonly ClinicalDbContext            _pgDb;
    private readonly ClinicalFieldExtractor       _extractor;
    private readonly IBackgroundJobClient         _jobs;
    private readonly ILogger<ExtractClinicalFieldsJob> _logger;

    public ExtractClinicalFieldsJob(
        ApplicationDbContext              sqlDb,
        ClinicalDbContext                 pgDb,
        ClinicalFieldExtractor            extractor,
        IBackgroundJobClient              jobs,
        ILogger<ExtractClinicalFieldsJob> logger)
    {
        _sqlDb     = sqlDb;
        _pgDb      = pgDb;
        _extractor = extractor;
        _jobs      = jobs;
        _logger    = logger;
    }

    /// <summary>
    /// Entry point invoked by Hangfire.
    /// </summary>
    /// <param name="documentId">PK of the <see cref="ClinicalDocument"/> to process.</param>
    /// <param name="cancellationToken">Hangfire-supplied shutdown token.</param>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 60, 120])]
    public async Task ExecuteAsync(int documentId, IJobCancellationToken cancellationToken)
    {
        var doc = await _sqlDb.ClinicalDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (doc is null)
        {
            _logger.LogWarning(
                "ExtractClinicalFieldsJob: document {DocumentId} not found — skipping.", documentId);
            return;
        }

        // Skip extraction when OCR produced no usable text.
        if (doc.OcrStatus == OcrStatus.NoData || string.IsNullOrWhiteSpace(doc.RawOcrText))
        {
            _logger.LogInformation(
                "ExtractClinicalFieldsJob: document {DocumentId} has OcrStatus={Status} — skipping extraction.",
                documentId, doc.OcrStatus);
            return;
        }

        // Idempotency guard: if rows already exist for this document (e.g. Hangfire retry after
        // SaveChangesAsync succeeded but Enqueue threw), skip re-insertion and just re-enqueue
        // the deduplication job so downstream processing can still complete.
        var alreadyExtracted = await _pgDb.ExtractedClinicalFields
            .AnyAsync(f => f.DocumentId == documentId);

        if (alreadyExtracted)
        {
            _logger.LogInformation(
                "ExtractClinicalFieldsJob: document {DocumentId} already extracted — skipping re-insertion.",
                documentId);
            _jobs.Enqueue<DeduplicateClinicalFieldsJob>(j => j.ExecuteAsync(doc.PatientId, null!));
            return;
        }

        var confidence = doc.OcrStatus == OcrStatus.Extracted ? 0.90 : 0.60; // AC-002
        // Use precise OCR confidence when available; fall back to heuristic per OcrStatus tier.
        // (exact float stored in TASK_040 OCR job; approximated here from OcrStatus tier)

        var fields = _extractor.Extract(doc.RawOcrText);

        if (fields.Count == 0)
        {
            _logger.LogInformation(
                "ExtractClinicalFieldsJob: no fields extracted from document {DocumentId}.", documentId);
        }
        else
        {
            var jobId   = Guid.NewGuid().ToString("N");
            var entities = fields.Select(f => new ExtractedClinicalField
            {
                PatientId       = doc.PatientId,
                DocumentId      = doc.Id,
                FieldType       = f.FieldType,
                FieldName       = f.FieldName,
                FieldValue      = f.FieldValue,
                ConfidenceScore = confidence,   // AC-002: propagate confidence to each row
                ExtractionJobId = jobId,
            }).ToList();

            _pgDb.ExtractedClinicalFields.AddRange(entities);
            await _pgDb.SaveChangesAsync();

            _logger.LogInformation(
                "ExtractClinicalFieldsJob: inserted {Count} field(s) for document {DocumentId}.",
                entities.Count, documentId);
        }

        // AC-004 / us_042: enqueue deduplication regardless of field count (idempotent).
        _jobs.Enqueue<DeduplicateClinicalFieldsJob>(j => j.ExecuteAsync(doc.PatientId, null!));
    }
}
