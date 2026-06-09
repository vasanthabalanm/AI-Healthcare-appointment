using System.Text;
using Microsoft.Extensions.Logging;
using Tesseract;
using UglyToad.PdfPig;
using PdfPage = UglyToad.PdfPig.Content.Page;

namespace ClinicalHealthcare.Infrastructure.OCR;

/// <summary>
/// Tesseract 5.x OCR service that processes a decrypted PDF stream.
///
/// Strategy (AC-001, AC-003):
///   1. Open the PDF with PdfPig and iterate pages.
///   2. Fast path — if PdfPig extracts words directly (digital PDF), use
///      the text with a high-confidence score (0.90).
///   3. Slow path — for pages with embedded images but no extractable text
///      (scanned PDFs), run Tesseract on each image via P/Invoke.
///   4. Average confidence across all pages (AC-003).
///
/// Key resolution: <c>TESSERACT_DATA_PATH</c> env var; falls back to
/// <c>tessdata</c> folder adjacent to the assembly output directory.
/// </summary>
public sealed class TesseractOcrService : ITesseractOcrService
{
    // Confidence assigned to pages whose text is directly extractable by PdfPig
    // (i.e. the PDF has a text layer rather than being an image scan).
    private const float DigitalPdfPageConfidence = 0.90f;

    private readonly string _tessDataPath;
    private readonly ILogger<TesseractOcrService> _logger;

    public TesseractOcrService(ILogger<TesseractOcrService> logger)
    {
        _tessDataPath = Environment.GetEnvironmentVariable("TESSERACT_DATA_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "tessdata");
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<(string RawText, float AverageConfidence)> OcrAsync(
        Stream pdfStream,
        CancellationToken ct = default)
    {
        // Reset position if the incoming stream is seekable and not at the start.
        // Critical: if the stream has already been read, CopyToAsync will copy zero bytes.
        if (pdfStream.CanSeek)
        {
            if (pdfStream.Position != 0)
            {
                _logger.LogWarning(
                    "TesseractOcrService: incoming pdfStream position was {Position}, resetting to 0",
                    pdfStream.Position);
                pdfStream.Position = 0;
            }
        }
        else
        {
            // Non-seekable streams (e.g. CryptoStream) must be positioned correctly by the caller.
            _logger.LogDebug("TesseractOcrService: pdfStream is not seekable, assuming position is correct");
        }

        // Copy to memory to avoid cross-thread issues with CryptoStream or caller disposal.
        // This ensures the PDF data is fully captured before Task.Run executes on another thread.
        var memoryStream = new MemoryStream();
        await pdfStream.CopyToAsync(memoryStream, ct);

        // Validate that we actually copied data. An empty stream means the incoming stream
        // was already consumed or is corrupt.
        if (memoryStream.Length == 0)
        {
            throw new InvalidOperationException(
                "The provided PDF stream is empty or has already been consumed. " +
                "Ensure the stream contains valid PDF data and is positioned at the start before calling OcrAsync.");
        }

        // Validate PDF magic bytes (%PDF-) to detect encrypted or corrupted streams early.
        // This prevents cryptic "Could not find the version header" errors from PdfPig.
        memoryStream.Position = 0;
        var header = new byte[5];
        var bytesRead = await memoryStream.ReadAsync(header, ct);
        if (bytesRead < 5 || header[0] != 0x25 || header[1] != 0x50 || 
            header[2] != 0x44 || header[3] != 0x46 || header[4] != 0x2D)
        {
            _logger.LogError(
                "TesseractOcrService: Stream does not start with PDF header. First bytes: {Bytes}",
                BitConverter.ToString(header));
            throw new InvalidOperationException(
                "The provided stream does not contain valid PDF data. " +
                "The stream may still be encrypted, corrupted, or contain a different file format. " +
                "Ensure the stream is fully decrypted before calling OcrAsync.");
        }

        memoryStream.Position = 0;

        // CPU-bound; offload to thread pool so caller's async context is not blocked.
        return await Task.Run(() =>
        {
            using (memoryStream)
            {
                return ProcessPdf(memoryStream, ct);
            }
        }, ct);
    }

    // ── Internal processing ───────────────────────────────────────────────────

    private (string RawText, float AverageConfidence) ProcessPdf(
        Stream pdfStream, CancellationToken ct)
    {
        // Defensive: reset stream position to 0 if the caller forgot to do so.
        // PdfPig requires the stream to be at the start to find the PDF header.
        if (pdfStream.CanSeek && pdfStream.Position != 0)
        {
            _logger.LogWarning(
                "TesseractOcrService: pdfStream position was {Position}, resetting to 0",
                pdfStream.Position);
            pdfStream.Position = 0;
        }

        using var pdf      = PdfDocument.Open(pdfStream);
        var pages          = pdf.GetPages().ToList();
        var allText        = new StringBuilder();
        var pageConfidences = new List<float>(pages.Count);

        // Create Tesseract engine lazily — only if any page requires OCR.
        TesseractEngine? engine = null;
        try
        {
            foreach (var page in pages)
            {
                ct.ThrowIfCancellationRequested();

                // Fast path: PdfPig word extraction (digital PDFs with a text layer).
                var words = page.GetWords().ToList();
                if (words.Count > 0)
                {
                    allText.AppendLine(string.Join(" ", words.Select(w => w.Text)));
                    pageConfidences.Add(DigitalPdfPageConfidence);
                    continue;
                }

                // Slow path: run Tesseract on embedded images.
                engine ??= CreateEngine();
                var (pageText, pageConf) = OcrPageImages(engine, page);

                if (pageText.Length > 0)
                    allText.AppendLine(pageText);

                pageConfidences.Add(pageConf);
            }
        }
        finally
        {
            engine?.Dispose();
        }

        var rawText           = allText.ToString().Trim();
        var averageConfidence = pageConfidences.Count == 0
            ? 0f
            : pageConfidences.Average();

        return (rawText, averageConfidence);
    }

    private TesseractEngine CreateEngine()
    {
        if (!Directory.Exists(_tessDataPath))
            _logger.LogWarning(
                "TesseractOcrService: tessdata directory not found at '{Path}'. " +
                "Set TESSERACT_DATA_PATH or place eng.traineddata in the output directory.",
                _tessDataPath);

        // TesseractEngine constructor throws TesseractException (wraps
        // DllNotFoundException) when the native lib is absent — surfaced as a
        // Hangfire job failure and retried up to 3× (AC-005).
        return new TesseractEngine(_tessDataPath, "eng", EngineMode.Default);
    }

    private (string Text, float Confidence) OcrPageImages(TesseractEngine engine, PdfPage page)
    {
        var images = page.GetImages().ToList();
        if (images.Count == 0)
            return (string.Empty, 0f);

        var texts       = new List<string>(images.Count);
        var confidences = new List<float>(images.Count);

        foreach (var image in images)
        {
            try
            {
                var rawBytes = image.RawBytes.ToArray();
                using var pix    = Pix.LoadFromMemory(rawBytes);
                using var result = engine.Process(pix);

                var text = result.GetText().Trim();
                if (text.Length > 0)
                    texts.Add(text);

                confidences.Add(result.GetMeanConfidence());
            }
            catch (Exception ex)
            {
                // Skip individual images that cannot be decoded (corrupt, unsupported format).
                _logger.LogDebug(
                    ex,
                    "TesseractOcrService: skipping undecodable image on page — {Message}",
                    ex.Message);
            }
        }

        if (confidences.Count == 0)
            return (string.Empty, 0f);

        return (string.Join("\n", texts), confidences.Average());
    }
}
