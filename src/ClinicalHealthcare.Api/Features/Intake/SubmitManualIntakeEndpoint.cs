using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicalHealthcare.Api.Features.Intake;

/// <summary>
/// Vertical-slice endpoint: POST /intake/manual
///
/// Creates a new <see cref="IntakeRecord"/> with <see cref="IntakeSource.Manual"/>
/// from a validated patient-submitted form (AC-001):
///   1. Validates the request DTO via data-annotation attributes → 422 on error (AC-002).
///   2. Rejects the request if the patient already has an active (<c>IsLatest=true</c>)
///      intake record → 409 (AC-003).
///   3. Persists a new <see cref="IntakeRecord"/> via EF Core parameterised queries (AC-004).
///   4. Returns 201 with the new record ID.
///
/// Patient ID is sourced from the authenticated JWT <c>sub</c> claim — never from
/// the request body (OWASP A01).
/// </summary>
public sealed class SubmitManualIntakeEndpoint : IEndpointDefinition
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
        app.MapPost("/intake/manual", HandleSubmitManualIntake)
           .RequireAuthorization("PatientOnly")
           .WithName("SubmitManualIntake")
           .WithTags("Intake")
           .Produces(StatusCodes.Status201Created)
           .Produces(StatusCodes.Status409Conflict)
           .Produces(StatusCodes.Status422UnprocessableEntity);
    }

    // ── POST /intake/manual ───────────────────────────────────────────────────

    public static async Task<IResult> HandleSubmitManualIntake(
        ManualIntakeRequest      request,
        HttpContext               httpContext,
        ApplicationDbContext      db,
        IInsurancePreCheckService insuranceCheck,
        CancellationToken         ct)
    {
        // Patient ID from JWT — not from request body (OWASP A01).
        var sub = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
               ?? httpContext.User.FindFirst("sub")?.Value;
        if (!int.TryParse(sub, out var patientId))
            return Results.Unauthorized();

        // Normalise: trim ChiefComplaint so [Required(AllowEmptyStrings=false)] also rejects all-whitespace input.
        request = request with { ChiefComplaint = request.ChiefComplaint.Trim() };

        // AC-002: validate DTO via data-annotation attributes.
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

        // AC-003: reject if patient already has an active (IsLatest=true) intake.
        // EF Core parameterised query — no raw SQL interpolation (AC-004).
        var hasActive = await db.IntakeRecords
            .AnyAsync(r => r.PatientId == patientId && r.IsLatest, ct);

        if (hasActive)
            return Results.Conflict(new { error = "Patient already has an active intake record." });

        // AC-001: create IntakeRecord with Source=Manual.
        var record = new IntakeRecord
        {
            PatientId      = patientId,
            Source         = IntakeSource.Manual,
            IntakeGroupId  = Guid.NewGuid(),
            Version        = 1,
            IsLatest       = true,
            SubmittedAt    = DateTime.UtcNow,
            ChiefComplaint = request.ChiefComplaint,
            CurrentMeds    = request.CurrentMeds,
            Allergies      = request.Allergies,
            MedicalHistory = request.MedicalHistory,
        };

        // TASK_032: soft insurance pre-check — non-blocking (AC-001).
        // Exception is caught and logged inside CheckAsync; always returns a status.
        record.InsuranceStatus = await insuranceCheck.CheckAsync(request.InsurerId, request.PlanCode, ct);

        db.IntakeRecords.Add(record);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/intake/{record.Id}", new { intakeRecordId = record.Id });
    }
}
