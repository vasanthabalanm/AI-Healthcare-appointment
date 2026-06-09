using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Api.Features.Patients;
using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Jobs;
using ClinicalHealthcare.Infrastructure.Security;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;

namespace ClinicalHealthcare.Api.Features.ClinicalDocs;

/// <summary>
/// Patient self-service document endpoints (resolved from JWT sub — no patient ID in URL).
///
///   GET    /patients/me/documents               — list the caller's uploaded documents
///   POST   /patients/me/documents               — upload PDF, JPG, PNG, or DOCX (≤ 20 MB)
///   GET    /patients/me/documents/{docId}/file  — stream decrypted file for preview
///   DELETE /patients/me/documents/{docId}       — soft-delete a document
///
/// Authorization: PatientOnly (role = "patient").
/// The patient's account ID is always sourced from the JWT sub claim to prevent IDOR (OWASP A01).
/// Upload pipeline: MIME + magic bytes validation → ClamAV scan → AES-256-CBC encrypt → disk → DB → Hangfire OCR job.
/// </summary>
public sealed class PatientDocumentsEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthorization(options =>
        {
            if (options.GetPolicy("PatientOnly") is null)
                options.AddPolicy("PatientOnly", p => p.RequireRole("patient"));
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/patients/me/documents", HandleList)
           .RequireAuthorization("PatientOnly")
           .WithName("ListMyDocuments")
           .WithTags("ClinicalDocs")
           .Produces<IEnumerable<PatientDocumentDto>>(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status401Unauthorized);

        app.MapPost("/patients/me/documents", HandleUpload)
           .RequireAuthorization("PatientOnly")
           .WithName("UploadMyDocument")
           .WithTags("ClinicalDocs")
           .Produces(StatusCodes.Status201Created)
           .Produces(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status401Unauthorized)
           .Produces(StatusCodes.Status422UnprocessableEntity)
           .Produces(StatusCodes.Status503ServiceUnavailable)
           .DisableAntiforgery();

        app.MapGet("/patients/me/documents/{docId:int}/file", HandlePreview)
           .RequireAuthorization("PatientOnly")
           .WithName("PreviewMyDocument")
           .WithTags("ClinicalDocs")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status401Unauthorized)
           .Produces(StatusCodes.Status404NotFound)
           .Produces(StatusCodes.Status422UnprocessableEntity);

        app.MapDelete("/patients/me/documents/{docId:int}", HandleDelete)
           .RequireAuthorization("PatientOnly")
           .WithName("DeleteMyDocument")
           .WithTags("ClinicalDocs")
           .Produces(StatusCodes.Status204NoContent)
           .Produces(StatusCodes.Status401Unauthorized)
           .Produces(StatusCodes.Status404NotFound);
    }

    // ── Response DTO ──────────────────────────────────────────────────────────

    public sealed record PatientDocumentDto(
        int     Id,
        string  FileName,
        string  FileType,
        string  UploadedAt,
        string  OcrStatus,
        double? OcrConfidence,
        long    FileSizeBytes);

    // ── GET /patients/me/documents ────────────────────────────────────────────

    public static async Task<IResult> HandleList(
        HttpContext          httpContext,
        ApplicationDbContext db,
        CancellationToken    ct)
    {
        if (!TryGetPatientId(httpContext, out var patientId))
            return Results.Unauthorized();

        var rows = await db.ClinicalDocuments
            .Where(d => d.PatientId == patientId && !d.IsDeleted)
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => new { d.Id, d.OriginalFileName, d.UploadedAt, d.OcrStatus })
            .ToListAsync(ct);

        var docs = rows.Select(d => new PatientDocumentDto(
            d.Id,
            d.OriginalFileName,
            MimeFromFileName(d.OriginalFileName),
            d.UploadedAt.ToString("O"),
            MapOcrStatus(d.OcrStatus),
            null,
            0));

        return Results.Ok(docs);
    }

    // ── POST /patients/me/documents ───────────────────────────────────────────

    public static async Task<IResult> HandleUpload(
        IFormFile            file,
        HttpContext          httpContext,
        ApplicationDbContext db,
        IClamAvScanService   clamAv,
        IAesEncryptionService aes,
        IBackgroundJobClient jobs,
        ICacheService        cache,
        CancellationToken    ct)
    {
        if (!TryGetPatientId(httpContext, out var patientId))
            return Results.Unauthorized();

        // Allowed MIME types.
        var allowedMimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "application/pdf",
            "image/jpeg",
            "image/png",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/msword"
        };
        if (!allowedMimes.Contains(file.ContentType))
            return Results.BadRequest(new { error = "Only PDF, JPG, PNG, and DOCX files are accepted." });

        // Max 20 MB.
        const long MaxFileSizeBytes = 20L * 1024 * 1024;
        if (file.Length > MaxFileSizeBytes)
            return Results.BadRequest(new { error = "File exceeds the 20 MB limit." });

        // Read into a seekable MemoryStream.
        using var ms = new MemoryStream((int)file.Length);
        await file.CopyToAsync(ms, ct);
        ms.Position = 0;

        // Magic bytes validation per file type (prevents content-type spoofing).
        var magicErr = ValidateMagicBytes(ms, file.ContentType);
        if (magicErr is not null)
            return Results.BadRequest(new { error = magicErr });
        ms.Position = 0;

        // ClamAV scan — unavailable → 503; infected → 422.
        ClamAvScanResult scanResult;
        try
        {
            scanResult = await clamAv.ScanAsync(ms, ct);
        }
        catch (ClamAvUnavailableException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                detail:     "Virus scanner is currently unavailable. The upload has been rejected.");
        }

        if (scanResult == ClamAvScanResult.Infected)
            return Results.UnprocessableEntity(new { error = "File failed the virus scan and was rejected." });

        ms.Position = 0;

        // Encrypt with AES-256-CBC; IV randomly generated per file.
        var (ciphertext, iv) = aes.Encrypt(ms);

        var storagePath = Environment.GetEnvironmentVariable("DOCUMENT_STORAGE_PATH")
                       ?? Path.GetTempPath();
        var fileName    = $"{Guid.NewGuid()}.enc";
        var filePath    = Path.Combine(storagePath, fileName);
        await File.WriteAllTextAsync(filePath,
            $"{Convert.ToBase64String(iv)}:{Convert.ToBase64String(ciphertext)}", ct);

        var document = new ClinicalDocument
        {
            PatientId          = patientId,
            UploadedByStaffId  = null,   // patient self-upload
            OriginalFileName   = file.FileName,
            EncryptedBlobPath  = filePath,
            VirusScanResult    = VirusScanResult.Clean,
        };

        db.ClinicalDocuments.Add(document);
        await db.SaveChangesAsync(ct);

        jobs.Enqueue<OcrDocumentJob>(j => j.ExecuteAsync(document.Id, null!));

        // Invalidate 360° view cache for this patient.
        await cache.DeleteAsync($"{Get360ViewEndpoint.CacheKeyPrefix}{patientId}", ct);

        return Results.Created($"/patients/me/documents/{document.Id}", new { documentId = document.Id });
    }

    // ── GET /patients/me/documents/{docId}/file ──────────────────────────────

    public static async Task<IResult> HandlePreview(
        int                   docId,
        HttpContext           httpContext,
        ApplicationDbContext  db,
        IAesEncryptionService aes,
        CancellationToken     ct)
    {
        if (!TryGetPatientId(httpContext, out var patientId))
            return Results.Unauthorized();

        var doc = await db.ClinicalDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == docId && d.PatientId == patientId && !d.IsDeleted, ct);

        if (doc is null)
            return Results.NotFound(new { error = $"Document {docId} not found." });

        Stream plaintext;
        try
        {
            plaintext = aes.Decrypt(doc.EncryptedBlobPath);
        }
        catch (CryptographicException)
        {
            return Results.UnprocessableEntity(new { error = "Document integrity check failed." });
        }
        catch (FileNotFoundException)
        {
            return Results.NotFound(new { error = "Encrypted document file not found on disk." });
        }

        var mime     = MimeFromFileName(doc.OriginalFileName);
        var fileName = string.IsNullOrWhiteSpace(doc.OriginalFileName)
            ? $"document-{docId}"
            : doc.OriginalFileName;

        return Results.File(plaintext, mime, fileName);
    }

    // ── DELETE /patients/me/documents/{docId} ─────────────────────────────────

    public static async Task<IResult> HandleDelete(
        int                  docId,
        HttpContext          httpContext,
        ApplicationDbContext db,
        CancellationToken    ct)
    {
        if (!TryGetPatientId(httpContext, out var patientId))
            return Results.Unauthorized();

        var doc = await db.ClinicalDocuments
            .FirstOrDefaultAsync(d => d.Id == docId && d.PatientId == patientId && !d.IsDeleted, ct);

        if (doc is null)
            return Results.NotFound(new { error = $"Document {docId} not found." });

        doc.IsDeleted    = true;
        doc.RetainUntil  = DateTimeOffset.UtcNow.AddYears(7);   // PHI 7-year retention
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool TryGetPatientId(HttpContext ctx, out int patientId)
    {
        var sub = ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
               ?? ctx.User.FindFirst("sub")?.Value;
        return int.TryParse(sub, out patientId);
    }

    private static string MapOcrStatus(OcrStatus status) => status switch
    {
        OcrStatus.Pending       => "Pending",
        OcrStatus.Extracted     => "Complete",
        OcrStatus.LowConfidence => "Complete",
        OcrStatus.NoData        => "Failed",
        _                       => "Pending"
    };

    internal static string MimeFromFileName(string fileName)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "pdf"  => "application/pdf",
            "jpg"  => "image/jpeg",
            "jpeg" => "image/jpeg",
            "png"  => "image/png",
            "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _      => "application/octet-stream"
        };
    }

    // Returns an error message string if magic bytes don't match; null if valid.
    private static string? ValidateMagicBytes(Stream stream, string contentType)
    {
        var header = new byte[8];
        var read   = stream.Read(header, 0, 8);

        if (contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("application/msword", StringComparison.OrdinalIgnoreCase))
        {
            // %PDF- signature
            if (read < 5 || header[0] != 0x25 || header[1] != 0x50 || header[2] != 0x44
                         || header[3] != 0x46 || header[4] != 0x2D)
                return "File does not contain a valid PDF signature (%PDF-).";
        }
        else if (contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase))
        {
            // JPEG: FF D8 FF
            if (read < 3 || header[0] != 0xFF || header[1] != 0xD8 || header[2] != 0xFF)
                return "File does not contain a valid JPEG signature.";
        }
        else if (contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase))
        {
            // PNG: 89 50 4E 47 0D 0A 1A 0A
            if (read < 8 || header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E
                         || header[3] != 0x47 || header[4] != 0x0D || header[5] != 0x0A
                         || header[6] != 0x1A || header[7] != 0x0A)
                return "File does not contain a valid PNG signature.";
        }
        else if (contentType.Equals(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            StringComparison.OrdinalIgnoreCase))
        {
            // DOCX is a ZIP: PK header 50 4B 03 04
            if (read < 4 || header[0] != 0x50 || header[1] != 0x4B
                         || header[2] != 0x03 || header[3] != 0x04)
                return "File does not contain a valid DOCX (ZIP) signature.";
        }

        return null;
    }
}
