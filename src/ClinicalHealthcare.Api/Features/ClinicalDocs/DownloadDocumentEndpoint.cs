using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;

namespace ClinicalHealthcare.Api.Features.ClinicalDocs;

/// <summary>
/// Vertical-slice endpoint: GET /patients/{id}/documents/{docId}
///
/// Streams a decrypted clinical document PDF to the caller (AC-005).
///   1. Requires StaffOrAdmin role (AC-003).
///   2. Validates that the document belongs to the requested patient (IDOR prevention).
///   3. Decrypts in-memory via <see cref="IAesEncryptionService.Decrypt"/> — no temp file (AC-002).
///   4. <see cref="CryptographicException"/> (wrong key / corrupt file) → 422 (AC-004).
///   5. Key from <c>CLINICAL_AES_KEY</c> env var or DPAPI fallback (AC-001, handled in service).
/// </summary>
public sealed class DownloadDocumentEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/patients/{id:int}/documents/{docId:int}", HandleDownload)
           .RequireAuthorization("StaffOrAdmin")
           .WithName("DownloadDocument")
           .WithTags("ClinicalDocs")
           .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
           .Produces(StatusCodes.Status404NotFound)
           .Produces(StatusCodes.Status422UnprocessableEntity);
    }

    // ── GET /patients/{id}/documents/{docId} ─────────────────────────────────

    public static async Task<IResult> HandleDownload(
        int                   id,
        int                   docId,
        ApplicationDbContext  db,
        IAesEncryptionService aes,
        CancellationToken     ct)
    {
        // Load document with soft-delete filter applied by global query filter.
        var doc = await db.ClinicalDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == docId, ct);

        if (doc is null)
            return Results.NotFound(new { error = $"Document {docId} not found." });

        // IDOR guard: document must belong to the requested patient (AC-003).
        if (doc.PatientId != id)
            return Results.NotFound(new { error = $"Document {docId} not found for patient {id}." });

        // AC-002 + AC-004: decrypt in-memory; CryptographicException → 422.
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

        // AC-005: stream decrypted PDF to the caller.
        var fileName = string.IsNullOrWhiteSpace(doc.OriginalFileName)
            ? $"document-{docId}.pdf"
            : doc.OriginalFileName;

        return Results.File(plaintext, "application/pdf", fileName);
    }
}
