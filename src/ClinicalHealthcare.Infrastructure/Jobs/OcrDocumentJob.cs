using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.OCR;
using ClinicalHealthcare.Infrastructure.Security;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
namespace ClinicalHealthcare.Infrastructure.Jobs;

/// <summary>
/// Hangfire background job that decrypts a clinical document in-memory and
/// performs Tesseract 5.x OCR (AC-001 — TASK_040).
///
/// Pipeline:
///   1. Load <see cref="ClinicalDocument"/> from the database.
///   2. Decrypt the encrypted blob via <see cref="IAesEncryptionService.Decrypt"/> —
///      no temp file written (AC-002 / TASK_039).
///   3. Run <see cref="ITesseractOcrService.OcrAsync"/> on the in-memory stream.
///   4. Map average confidence to <see cref="OcrStatus"/> (AC-002):
///      ≥0.75 → Extracted | &lt;0.75 → LowConfidence | empty text → NoData.
///   5. Persist <see cref="ClinicalDocument.RawOcrText"/> + <see cref="ClinicalDocument.OcrStatus"/>.
///
/// Retries: <see cref="AutomaticRetryAttribute"/> 3× with exponential back-off (AC-005).
/// On any failure the <see cref="OcrStatus"/> is set to <see cref="OcrStatus.NoData"/>
/// before re-throwing so the database reflects the terminal state after dead-lettering.
/// </summary>
public sealed class OcrDocumentJob
{
    private readonly ApplicationDbContext    _db;
    private readonly IAesEncryptionService   _aes;
    private readonly ITesseractOcrService    _ocr;
    private readonly IBackgroundJobClient    _jobs;
    private readonly ILogger<OcrDocumentJob> _logger;

    public OcrDocumentJob(
        ApplicationDbContext    db,
        IAesEncryptionService   aes,
        ITesseractOcrService    ocr,
        IBackgroundJobClient    jobs,
        ILogger<OcrDocumentJob> logger)
    {
        _db     = db;
        _aes    = aes;
        _ocr    = ocr;
        _jobs   = jobs;
        _logger = logger;
    }

    /// <summary>
    /// Entry point invoked by Hangfire.
    /// </summary>
    /// <param name="documentId">PK of the <see cref="ClinicalDocument"/> to process.</param>
    /// <param name="cancellationToken">Hangfire-supplied shutdown token.</param>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 60, 120])]
    public async Task ExecuteAsync(int documentId, IJobCancellationToken cancellationToken)
    {
        var doc = await _db.ClinicalDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId);

        if (doc is null)
        {
            _logger.LogWarning(
                "OcrDocumentJob: document {DocumentId} not found — nothing to do.", documentId);
            return;
        }

        try
        {
            // AC-001: decrypt in-memory (no temp file) then OCR.
            using var plaintextStream = _aes.Decrypt(doc.EncryptedBlobPath);
            var ct = cancellationToken?.ShutdownToken ?? CancellationToken.None;

            var (rawText, confidence) = await _ocr.OcrAsync(plaintextStream, ct);

            // AC-002: map confidence to OcrStatus.
            if (string.IsNullOrWhiteSpace(rawText))
                doc.OcrStatus = OcrStatus.NoData;
            else if (confidence >= 0.75f)
                doc.OcrStatus = OcrStatus.Extracted;
            else
                doc.OcrStatus = OcrStatus.LowConfidence;

            // AC-004: store raw OCR text.
            doc.RawOcrText = rawText;

            _logger.LogInformation(
                "OcrDocumentJob: document {DocumentId} processed — status={Status}, confidence={Confidence:F2}.",
                documentId, doc.OcrStatus, confidence);
        }
        catch (Exception ex)
        {
            // Persist NoData so the record is not stuck in Pending after dead-lettering.
            _logger.LogError(
                ex,
                "OcrDocumentJob: pipeline failed for document {DocumentId}. Setting OcrStatus=NoData.",
                documentId);
            doc.OcrStatus  = OcrStatus.NoData;
            doc.RawOcrText = null;
            await _db.SaveChangesAsync();
            throw; // Re-throw → Hangfire retries / dead-letters (AC-005).
        }

        await _db.SaveChangesAsync();

        // Chain extraction job only when OCR produced usable text (AC-001 — TASK_041).
        if (doc.OcrStatus != OcrStatus.NoData)
            _jobs.Enqueue<ExtractClinicalFieldsJob>(j => j.ExecuteAsync(documentId, null!));
    }
}

