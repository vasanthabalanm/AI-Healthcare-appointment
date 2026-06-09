using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Api.Features.Patients;
using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Jobs;
using ClinicalHealthcare.Infrastructure.Security;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace ClinicalHealthcare.Api.Features.ClinicalDocs;

/// <summary>
/// Vertical-slice endpoint: POST /patients/{id}/documents
///
/// Accepts a PDF upload, scans it with ClamAV, encrypts it with AES-256-CBC,
/// writes the encrypted file to disk, inserts a <see cref="ClinicalDocument"/> row,
/// and enqueues an OCR job (AC-001 through AC-005).
///
/// Security:
///   1. Requires StaffOrAdmin role.
///   2. Staff ID sourced from JWT sub claim — never from the request body (OWASP A01).
///   3. PDF validated by MIME type AND magic bytes — prevents content-type spoofing.
///   4. ClamAV scan cannot be bypassed: unavailable daemon → 503, no upload accepted.
///   5. AES-256-CBC with per-file random IV; key from CLINICAL_AES_KEY env var only.
/// </summary>
public sealed class UploadDocumentEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/patients/{id:int}/documents", HandleUpload)
           .RequireAuthorization("StaffOrAdmin")
           .WithName("UploadDocument")
           .WithTags("ClinicalDocs")
           .Produces(StatusCodes.Status201Created)
           .Produces(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status401Unauthorized)
           .Produces(StatusCodes.Status404NotFound)
           .Produces(StatusCodes.Status422UnprocessableEntity)
           .Produces(StatusCodes.Status503ServiceUnavailable)
           .DisableAntiforgery();
    }

    // ── POST /patients/{id}/documents ─────────────────────────────────────────

    public static async Task<IResult> HandleUpload(
        int                  id,
        IFormFile            file,
        HttpContext          httpContext,
        ApplicationDbContext db,
        IClamAvScanService   clamAv,
        IAesEncryptionService aes,
        IBackgroundJobClient jobs,
        ICacheService        cache,
        CancellationToken    ct)
    {
        // Staff ID from JWT — never from request body (OWASP A01).
        var sub = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
               ?? httpContext.User.FindFirst("sub")?.Value;
        if (!int.TryParse(sub, out var staffId))
            return Results.Unauthorized();

        // AC-001: PDF MIME type check.
        if (!string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { error = "Only PDF files are accepted." });

        // AC-001: Max 10 MB.
        const long MaxFileSizeBytes = 10L * 1024 * 1024;
        if (file.Length > MaxFileSizeBytes)
            return Results.BadRequest(new { error = "File exceeds the 10 MB limit." });

        // Read entire file into a seekable MemoryStream (≤10 MB validated above).
        using var ms = new MemoryStream((int)file.Length);
        await file.CopyToAsync(ms, ct);
        ms.Position = 0;

        // AC-001: Magic bytes — first 5 bytes must be %PDF- (0x25 0x50 0x44 0x46 0x2D).
        var magic = new byte[5];
        var read  = ms.Read(magic, 0, 5);
        if (read < 5 || magic[0] != 0x25 || magic[1] != 0x50 || magic[2] != 0x44
                     || magic[3] != 0x46 || magic[4] != 0x2D)
        {
            return Results.BadRequest(new { error = "File does not contain a valid PDF signature (%PDF-)." });
        }
        ms.Position = 0;

        // Verify patient exists.
        var patientExists = await db.UserAccounts.AnyAsync(u => u.Id == id, ct);
        if (!patientExists)
            return Results.NotFound(new { error = $"Patient {id} not found." });

        // AC-002: ClamAV scan — unavailable → 503; infected → 422.
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

        // AC-003: Encrypt with AES-256-CBC; IV is randomly generated per file.
        var (ciphertext, iv) = aes.Encrypt(ms);

        // Write encrypted file: Base64(IV):Base64(ciphertext)
        var storagePath = Environment.GetEnvironmentVariable("DOCUMENT_STORAGE_PATH")
                       ?? Path.GetTempPath();
        var fileName    = $"{Guid.NewGuid()}.enc";
        var filePath    = Path.Combine(storagePath, fileName);
        await File.WriteAllTextAsync(filePath,
            $"{Convert.ToBase64String(iv)}:{Convert.ToBase64String(ciphertext)}", ct);

        // AC-004: Insert ClinicalDocument after the encrypted file is on disk.
        var document = new ClinicalDocument
        {
            PatientId          = id,
            UploadedByStaffId  = staffId,
            OriginalFileName   = file.FileName,
            EncryptedBlobPath  = filePath,
            VirusScanResult    = VirusScanResult.Clean,
        };
        db.ClinicalDocuments.Add(document);
        await db.SaveChangesAsync(ct);

        // AC-005: Enqueue OCR job after the row is committed.
        jobs.Enqueue<OcrDocumentJob>(j => j.ExecuteAsync(document.Id, null!));

        // AC-005 (TASK_044): Invalidate the 360° view cache so the next GET reassembles.
        await cache.DeleteAsync($"{Get360ViewEndpoint.CacheKeyPrefix}{id}", ct);

        return Results.Created($"/patients/{id}/documents/{document.Id}", new { documentId = document.Id });
    }
}
