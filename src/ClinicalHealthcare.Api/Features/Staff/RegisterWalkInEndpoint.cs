using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Configuration;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClinicalHealthcare.Api.Features.Staff;

/// <summary>
/// Vertical-slice endpoint: POST /staff/patients/walkin
///
/// Registers a walk-in patient and adds them to today's queue (AC-002 to AC-005).
///   1. Requires StaffOrAdmin role.
///   2. Checks for existing patient by FirstName + LastName + DOB to avoid duplicates.
///   3. If not found → creates a minimal UserAccount with WalkIn=true, Role="patient".
///   4. Counts today's QueueEntry rows; if ≥ capacity and Override≠true → 409.
///   5. If Override → writes AuditLog entry (AC-004).
///   6. Inserts QueueEntry at next available Position for today.
///   7. Returns 201 with {patientId, queueEntryId, position}.
///
/// Staff ID is sourced from the authenticated JWT sub claim (OWASP A01).
/// </summary>
public sealed class RegisterWalkInEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/staff/patients/walkin", HandleRegisterWalkIn)
           .RequireAuthorization("StaffOrAdmin")
           .WithName("RegisterWalkIn")
           .WithTags("Staff")
           .Produces<WalkInRegistrationResult>(StatusCodes.Status201Created)
           .Produces(StatusCodes.Status409Conflict)
           .Produces(StatusCodes.Status422UnprocessableEntity);
    }

    // ── POST /staff/patients/walkin ──────────────────────────────────────────

    public static async Task<IResult> HandleRegisterWalkIn(
        WalkInRequest          request,
        HttpContext             httpContext,
        ApplicationDbContext    db,
        IOptions<AppSettings>  appSettings,
        CancellationToken       ct)
    {
        // Staff ID from JWT — never from request body (OWASP A01).
        var sub = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
               ?? httpContext.User.FindFirst("sub")?.Value;
        if (!int.TryParse(sub, out var staffId))
            return Results.Unauthorized();

        // Validate DTO.
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

        // Duplicate check: find existing patient by name + DOB to avoid creating a duplicate account.
        var existing = await db.UserAccounts
            .Where(u => u.Role == "patient"
                     && u.FirstName == request.FirstName
                     && u.LastName  == request.LastName
                     && u.DateOfBirth == request.DateOfBirth)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);

        int patientId;

        if (existing != 0)
        {
            // Link to existing patient rather than creating a duplicate (AC-002 edge case).
            patientId = existing;
        }
        else
        {
            // AC-002: create minimal walk-in patient profile.
            var newPatient = new UserAccount
            {
                Email        = $"walkin-{Guid.NewGuid():N}@walkin.internal",
                PasswordHash = string.Empty,   // walk-in accounts have no password
                Role         = "patient",
                FirstName    = request.FirstName,
                LastName     = request.LastName,
                DateOfBirth  = request.DateOfBirth,
                IsActive     = true,
                WalkIn       = true,
            };
            db.UserAccounts.Add(newPatient);
            await db.SaveChangesAsync(ct);
            patientId = newPatient.Id;
        }

        var today    = DateOnly.FromDateTime(DateTime.UtcNow);
        var capacity = appSettings.Value.QueueCapacity;

        // AC-003: count today's queue entries.
        var todayCount = await db.QueueEntries
            .CountAsync(q => q.QueueDate == today, ct);

        if (todayCount >= capacity && !request.Override)
            return Results.Conflict(new
            {
                error    = $"Queue capacity ({capacity}) reached for {today:yyyy-MM-dd}.",
                capacity,
                current  = todayCount,
            });

        // AC-005: next position = MAX(Position) for today + 1, or 1 if queue is empty.
        var nextPosition = todayCount == 0
            ? 1
            : await db.QueueEntries
                .Where(q => q.QueueDate == today)
                .MaxAsync(q => q.Position, ct) + 1;

        // Save QueueEntry first so entry.Id is available for the AuditLog INSERT.
        // AuditLogs is INSERT-only at the SQL Server GRANT level — no UPDATE is permitted.
        var entry = new QueueEntry
        {
            PatientId      = patientId,
            QueueDate      = today,
            Position       = nextPosition,
            Status         = QueueStatus.Waiting,
            IsWalkIn       = true,
            AddedByStaffId = staffId,
        };
        db.QueueEntries.Add(entry);
        await db.SaveChangesAsync(ct);

        // AC-004: capacity override → INSERT AuditLog with correct EntityId (no back-fill UPDATE).
        if (todayCount >= capacity && request.Override)
        {
            db.AuditLogs.Add(new AuditLog
            {
                EntityType = nameof(QueueEntry),
                EntityId   = entry.Id,
                ActorId    = staffId,
                Action     = "QUEUE_OVERRIDE",
                AfterValue = $"{{\"queueDate\":\"{today:yyyy-MM-dd}\",\"currentCount\":{todayCount},\"capacity\":{capacity}}}",
            });
            await db.SaveChangesAsync(ct);
        }

        return Results.Created(
            $"/staff/queue/{entry.Id}",
            new WalkInRegistrationResult(patientId, entry.Id, nextPosition));
    }
}

/// <summary>Request body for POST /staff/patients/walkin.</summary>
public sealed record WalkInRequest
{
    [Required(AllowEmptyStrings = false, ErrorMessage = "firstName is required.")]
    [MaxLength(100, ErrorMessage = "firstName must not exceed 100 characters.")]
    public string FirstName { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = "lastName is required.")]
    [MaxLength(100, ErrorMessage = "lastName must not exceed 100 characters.")]
    public string LastName { get; init; } = string.Empty;

    public DateOnly? DateOfBirth { get; init; }

    /// <summary>When true, bypasses capacity limit and writes an AuditLog entry (AC-004).</summary>
    public bool Override { get; init; } = false;
}

/// <summary>Response body for a successful walk-in registration (AC-002 / AC-005).</summary>
public sealed record WalkInRegistrationResult(
    int PatientId,
    int QueueEntryId,
    int Position);
