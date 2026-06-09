using System.IdentityModel.Tokens.Jwt;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicalHealthcare.Api.Features.Intake;

/// <summary>
/// Vertical-slice endpoint: GET /intake/{intakeGroupId}[?version=N]
///
/// Returns the requested version of an intake record (AC-002 / AC-003):
///   • No <c>version</c> query param → returns the latest version via the
///     EF Core default query filter (<c>IsLatest = true</c>).
///   • <c>?version=N</c> → bypasses the default filter to retrieve the
///     historical version N (<c>IgnoreQueryFilters</c>).
///   • Returns 404 if the <c>IntakeGroupId</c> or requested version is not found.
///
/// Accessible by patients, staff, and admin (IntakeReader policy).
/// </summary>
public sealed class GetIntakeEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthorization(options =>
        {
            if (options.GetPolicy("IntakeReader") is null)
                options.AddPolicy("IntakeReader", p => p.RequireRole("patient", "staff", "admin"));
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/intake/{intakeGroupId:guid}", HandleGetIntake)
           .RequireAuthorization("IntakeReader")
           .WithName("GetIntake")
           .WithTags("Intake")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status404NotFound);
    }

    // ── GET /intake/{intakeGroupId} ───────────────────────────────────────────

    public static async Task<IResult> HandleGetIntake(
        Guid                 intakeGroupId,
        int?                 version,
        HttpContext          httpContext,
        ApplicationDbContext db,
        CancellationToken    ct)
    {
        // Extract caller identity for ownership check (OWASP A01).
        var sub = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
               ?? httpContext.User.FindFirst("sub")?.Value;
        int.TryParse(sub, out var callerPatientId);
        IntakeRecord? record;

        if (version.HasValue)
        {
            // AC-003: historical version — bypass IsLatest default query filter.
            record = await db.IntakeRecords
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.IntakeGroupId == intakeGroupId && r.Version == version.Value, ct);
        }
        else
        {
            // AC-002: latest version — default query filter (IsLatest=true) applied automatically.
            record = await db.IntakeRecords
                .FirstOrDefaultAsync(r => r.IntakeGroupId == intakeGroupId, ct);
        }

        if (record is null)
            return Results.NotFound(new { error = "Intake record not found." });

        // F1 (OWASP A01): staff and admin may read any intake (cross-patient access by design).
        // All other authenticated callers (patients) may only read their own intake.
        var isStaffOrAdmin = httpContext.User.IsInRole("staff") || httpContext.User.IsInRole("admin");
        if (!isStaffOrAdmin && record.PatientId != callerPatientId)
            return Results.Forbid();

        return Results.Ok(new
        {
            intakeGroupId  = record.IntakeGroupId,
            version        = record.Version,
            isLatest       = record.IsLatest,
            source         = record.Source.ToString(),
            submittedAt    = record.SubmittedAt,
            chiefComplaint = record.ChiefComplaint,
            currentMeds    = record.CurrentMeds,
            allergies      = record.Allergies,
            medicalHistory = record.MedicalHistory,
        });
    }
}
