using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicalHealthcare.Api.Features.Intake;

/// <summary>
/// Vertical-slice endpoint: PATCH /intake/{intakeGroupId}
///
/// Updates the latest intake record version (AC-001):
///   1. Loads the current <c>IsLatest=true</c> record for the given <c>IntakeGroupId</c>.
///   2. Validates patient ownership (JWT PatientId == record.PatientId).
///   3. Validates request DTO field lengths via data-annotation attributes → 422 on error.
///   4. Merges incoming (non-null) values with the current record's values.
///   5. No-op PATCH (same values after merge) → returns 200 without a new version (AC-004).
///   6. Changed values → marks current <c>IsLatest=false</c>, inserts new version
///      with <c>Version = current.Version + 1</c> and <c>IsLatest=true</c> (AC-001).
///
/// Prior versions are inherently read-only — the route targets only the latest via
/// the EF Core default query filter (AC-005).
/// Patient ID is sourced from the authenticated JWT <c>sub</c> claim (OWASP A01).
/// </summary>
public sealed class UpdateIntakeEndpoint : IEndpointDefinition
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
        app.MapPatch("/intake/{intakeGroupId:guid}", HandleUpdateIntake)
           .RequireAuthorization("PatientOnly")
           .WithName("UpdateIntake")
           .WithTags("Intake")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status403Forbidden)
           .Produces(StatusCodes.Status404NotFound)
           .Produces(StatusCodes.Status422UnprocessableEntity);
    }

    // ── PATCH /intake/{intakeGroupId} ─────────────────────────────────────────

    public static async Task<IResult> HandleUpdateIntake(
        Guid                 intakeGroupId,
        UpdateIntakeRequest  request,
        HttpContext          httpContext,
        ApplicationDbContext db,
        CancellationToken    ct)
    {
        // Patient ID from JWT — not from request body (OWASP A01).
        var sub = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
               ?? httpContext.User.FindFirst("sub")?.Value;
        if (!int.TryParse(sub, out var patientId))
            return Results.Unauthorized();

        // Trim optional ChiefComplaint so whitespace-only updates are caught by MaxLength validation.
        if (request.ChiefComplaint is not null)
            request = request with { ChiefComplaint = request.ChiefComplaint.Trim() };

        // Validate DTO field lengths via data-annotation attributes → 422 on violation.
        var validationErrors = new List<ValidationResult>();
        var validationContext = new ValidationContext(request);
        if (!Validator.TryValidateObject(request, validationContext, validationErrors, validateAllProperties: true))
        {
            var errors = validationErrors
                .GroupBy(e => e.MemberNames.FirstOrDefault() ?? string.Empty)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage ?? string.Empty).ToArray());

            return Results.ValidationProblem(errors, statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        // Load the current latest version. Default query filter (IsLatest=true) applies automatically.
        var current = await db.IntakeRecords
            .FirstOrDefaultAsync(r => r.IntakeGroupId == intakeGroupId, ct);

        if (current is null)
            return Results.NotFound(new { error = "Intake record not found." });

        // AC-005: ownership guard — patient may only edit their own intake.
        if (current.PatientId != patientId)
            return Results.Forbid();

        // Merge: null or empty-string means "keep current value" (empty string has no clinical meaning).
        var newChiefComplaint = (string.IsNullOrEmpty(request.ChiefComplaint) ? null : request.ChiefComplaint)
                             ?? current.ChiefComplaint;
        var newCurrentMeds    = request.CurrentMeds    ?? current.CurrentMeds;
        var newAllergies      = request.Allergies      ?? current.Allergies;
        var newMedicalHistory = request.MedicalHistory ?? current.MedicalHistory;

        // AC-004: no-op detection — return 200 if nothing changed.
        var hasChanges =
            newChiefComplaint != current.ChiefComplaint ||
            newCurrentMeds    != current.CurrentMeds    ||
            newAllergies      != current.Allergies      ||
            newMedicalHistory != current.MedicalHistory;

        if (!hasChanges)
        {
            return Results.Ok(new
            {
                intakeGroupId = current.IntakeGroupId,
                version       = current.Version,
                noOp          = true,
            });
        }

        // AC-001: create new version — mark current IsLatest=false, insert new row.
        current.IsLatest = false;

        var newVersion = new IntakeRecord
        {
            PatientId      = current.PatientId,
            Source         = current.Source,
            IntakeGroupId  = current.IntakeGroupId,
            Version        = current.Version + 1,
            IsLatest       = true,
            SubmittedAt    = DateTime.UtcNow,
            ChiefComplaint = newChiefComplaint,
            CurrentMeds    = newCurrentMeds,
            Allergies      = newAllergies,
            MedicalHistory = newMedicalHistory,
        };

        db.IntakeRecords.Add(newVersion);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            intakeGroupId = newVersion.IntakeGroupId,
            version       = newVersion.Version,
            noOp          = false,
        });
    }
}
