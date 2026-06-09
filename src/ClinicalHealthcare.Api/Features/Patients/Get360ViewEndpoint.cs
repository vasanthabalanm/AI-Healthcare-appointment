using System.IdentityModel.Tokens.Jwt;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicalHealthcare.Api.Features.Patients;

/// <summary>
/// Vertical-slice endpoint: GET /patients/{id}/view360
///
/// Returns the assembled 360° patient view. Cache-aside pattern against Redis (TTL=300s).
///
/// Cache hit  → deserialize from Redis; no PostgreSQL query (AC-001).
/// Cache miss → assemble from PostgreSQL; populate Redis (AC-002).
/// Redis down → log warning + assemble directly; still return 200 (edge-case).
/// No documents → return empty clinicalFields + hint (AC-005).
///
/// Security: Requires StaffOrAdmin role (OWASP A01).
/// </summary>
public sealed class Get360ViewEndpoint : IEndpointDefinition
{
    internal const string CacheKeyPrefix = "view360:";
    internal const int    CacheTtlSeconds = 300;

    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/patients/{id:int}/view360", HandleGet360View)
           .RequireAuthorization("StaffOrAdmin")
           .WithName("GetPatient360View")
           .WithTags("Patients")
           .Produces<PatientView360Dto>(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status401Unauthorized)
           .Produces(StatusCodes.Status403Forbidden)
           .Produces(StatusCodes.Status404NotFound);
    }

    // ── GET /patients/{id}/view360 ────────────────────────────────────────────

    public static async Task<IResult> HandleGet360View(
        int                id,
        ApplicationDbContext sqlDb,
        ClinicalDbContext   pgDb,
        ICacheService       cache,
        CancellationToken   ct)
    {
        var cacheKey = $"{CacheKeyPrefix}{id}";

        // AC-001: Cache hit — return without hitting PostgreSQL.
        var cached = await cache.GetAsync<PatientView360Dto>(cacheKey, ct);
        if (cached is not null)
            return Results.Ok(cached);

        // Verify patient exists.
        var patient = await sqlDb.UserAccounts
            .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted, ct);
        if (patient is null)
            return Results.NotFound(new { error = $"Patient {id} not found." });

        // AC-002: Assemble from PostgreSQL.
        var fields = await pgDb.ExtractedClinicalFields
            .Where(f => f.PatientId == id)
            .OrderByDescending(f => f.ExtractedAt)
            .ToListAsync(ct);

        var unresolvedCount = await pgDb.ConflictFlags
            .CountAsync(c => c.PatientId == id && c.Status == ConflictFlagStatus.Unresolved, ct);

        var grouped = fields
            .GroupBy(f => f.FieldType.ToString())
            .ToDictionary(
                g => g.Key,
                g => g.Select(f => new ClinicalFieldSummary
                {
                    FieldName       = f.FieldName,
                    FieldValue      = f.FieldValue,
                    ConfidenceScore = f.ConfidenceScore,
                    ExtractedAt     = f.ExtractedAt
                }).ToList());

        // Supplement with IntakeRecord fields when no extracted-document data is present
        // for the "Intake" key (AI intake / manual intake forms store in IntakeRecords,
        // not ExtractedClinicalFields).
        if (!grouped.ContainsKey("Intake"))
        {
            var intake = await sqlDb.IntakeRecords
                .FirstOrDefaultAsync(r => r.PatientId == id, ct);

            if (intake is not null)
            {
                var intakeEntries = new List<ClinicalFieldSummary>();
                if (!string.IsNullOrWhiteSpace(intake.ChiefComplaint))
                    intakeEntries.Add(new ClinicalFieldSummary { FieldName = "Chief Complaint",     FieldValue = intake.ChiefComplaint, ConfidenceScore = 1.0, ExtractedAt = intake.SubmittedAt });
                if (!string.IsNullOrWhiteSpace(intake.CurrentMeds))
                    intakeEntries.Add(new ClinicalFieldSummary { FieldName = "Current Medications", FieldValue = intake.CurrentMeds,    ConfidenceScore = 1.0, ExtractedAt = intake.SubmittedAt });
                if (!string.IsNullOrWhiteSpace(intake.Allergies))
                    intakeEntries.Add(new ClinicalFieldSummary { FieldName = "Allergies",           FieldValue = intake.Allergies,      ConfidenceScore = 1.0, ExtractedAt = intake.SubmittedAt });
                if (!string.IsNullOrWhiteSpace(intake.MedicalHistory))
                    intakeEntries.Add(new ClinicalFieldSummary { FieldName = "Medical History",     FieldValue = intake.MedicalHistory, ConfidenceScore = 1.0, ExtractedAt = intake.SubmittedAt });

                if (intakeEntries.Count > 0)
                    grouped["Intake"] = intakeEntries;
            }
        }

        var hasDocuments = await sqlDb.ClinicalDocuments
            .AnyAsync(d => d.PatientId == id && !d.IsDeleted, ct);

        var view = new PatientView360Dto
        {
            PatientId           = id,
            FirstName           = patient.FirstName,
            LastName            = patient.LastName,
            Email               = patient.Email,
            VerificationStatus  = patient.VerificationStatus,
            ClinicalFields      = grouped,
            UnresolvedConflicts = unresolvedCount,
            Hint                = hasDocuments ? null : "No clinical documents uploaded yet"
        };

        // Populate Redis cache; Redis outage is handled gracefully inside CacheService.
        await cache.SetAsync(cacheKey, view, TimeSpan.FromSeconds(CacheTtlSeconds), ct);

        return Results.Ok(view);
    }
}
