using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Jobs;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicalHealthcare.Api.Features.Coding;

/// <summary>
/// Vertical-slice endpoint: POST /patients/{id}/generate-codes
///
/// Enqueues <see cref="GenerateIcd10CodesJob"/> or <see cref="GenerateCptCodesJob"/> for a
/// Verified patient depending on the <c>type</c> query parameter (AC-001).
///
/// Guards:
///   1. Patient not found → 404.
///   2. Patient VerificationStatus != Verified → 409.
///   3. type param not in {ICD10, CPT} → 400.
///
/// On success: Hangfire job enqueued; returns 202 Accepted.
/// ICD-10 and CPT jobs are independent — both can run concurrently (AC-001).
///
/// Security: Requires StaffOrAdmin role (OWASP A01).
/// </summary>
public sealed class GenerateCodesEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/patients/{id:int}/generate-codes", HandleGenerateCodes)
           .RequireAuthorization("StaffOrAdmin")
           .WithName("GenerateCodes")
           .WithTags("Coding")
           .Produces(StatusCodes.Status202Accepted)
           .Produces(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status401Unauthorized)
           .Produces(StatusCodes.Status403Forbidden)
           .Produces(StatusCodes.Status404NotFound)
           .Produces(StatusCodes.Status409Conflict);
    }

    // ── POST /patients/{id}/generate-codes ───────────────────────────────────

    public static async Task<IResult> HandleGenerateCodes(
        int                  id,
        [FromQuery] string   type,
        ApplicationDbContext sqlDb,
        IBackgroundJobClient jobs,
        CancellationToken    ct)
    {
        // Supported types: ICD10 and CPT.
        var normalizedType = type?.ToUpperInvariant();
        if (normalizedType is not ("ICD10" or "CPT"))
            return Results.BadRequest(new { error = "Unsupported code type. Supported: ICD10, CPT" });

        var patient = await sqlDb.UserAccounts
            .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted, ct);
        if (patient is null)
            return Results.NotFound(new { error = $"Patient {id} not found." });

        // AC-001: Patient must be Verified before codes can be generated (Trust-First).
        if (patient.VerificationStatus != VerificationStatus.Verified)
            return Results.Conflict(new
            {
                error = "Patient record must be verified before generating codes.",
                verificationStatus = patient.VerificationStatus.ToString()
            });

        // Enqueue the appropriate job (AC-001); ICD-10 and CPT are independent.
        if (normalizedType == "ICD10")
            jobs.Enqueue<GenerateIcd10CodesJob>(j => j.ExecuteAsync(id, CancellationToken.None));
        else
            jobs.Enqueue<GenerateCptCodesJob>(j => j.ExecuteAsync(id, CancellationToken.None));

        return Results.Accepted($"/patients/{id}/codes", new
        {
            message   = $"{normalizedType} code generation job enqueued.",
            patientId = id
        });
    }
}
