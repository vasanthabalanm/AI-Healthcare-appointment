namespace ClinicalHealthcare.Infrastructure.OCR;

/// <summary>
/// Abstraction for Tesseract 5.x OCR over a PDF stream.
/// </summary>
public interface ITesseractOcrService
{
    /// <summary>
    /// Performs OCR on the supplied <paramref name="pdfStream"/> and returns
    /// the extracted raw text and the average confidence across all pages (AC-001, AC-003).
    /// </summary>
    /// <param name="pdfStream">Decrypted in-memory PDF stream positioned at 0.</param>
    /// <param name="ct">Cancellation token forwarded from the Hangfire job.</param>
    /// <returns>
    /// A tuple of <c>RawText</c> (concatenated page text) and
    /// <c>AverageConfidence</c> in the range [0.0, 1.0].
    /// </returns>
    Task<(string RawText, float AverageConfidence)> OcrAsync(
        Stream pdfStream,
        CancellationToken ct = default);
}
