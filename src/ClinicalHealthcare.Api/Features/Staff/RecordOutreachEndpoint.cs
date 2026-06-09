using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace ClinicalHealthcare.Api.Features.Staff;

/// <summary>
/// Vertical-slice endpoint: POST /appointments/{id}/outreach
///
/// Records a staff outreach attempt on a high-risk appointment (AC-002).
///   1. Requires StaffOrAdmin role.
///   2. Staff ID sourced from JWT sub claim — never from request body (OWASP A01).
///   3. Appointment must exist (404 if not).
///   4. Returns 201 with the created OutreachRecord ID.
/// </summary>
public sealed class RecordOutreachEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/appointments/{id:int}/outreach", HandleRecordOutreach)
           .RequireAuthorization("StaffOrAdmin")
           .WithName("RecordOutreach")
           .WithTags("Staff")
           .Produces(StatusCodes.Status201Created)
           .Produces(StatusCodes.Status404NotFound);
    }

    // ── POST /appointments/{id}/outreach ─────────────────────────────────────

    public static async Task<IResult> HandleRecordOutreach(
        int                  id,
        OutreachRequest      request,
        HttpContext          httpContext,
        ApplicationDbContext db,
        CancellationToken    ct)
    {
        // Staff ID from JWT — never from request body (OWASP A01).
        var sub = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
               ?? httpContext.User.FindFirst("sub")?.Value;
        if (!int.TryParse(sub, out var staffId))
            return Results.Unauthorized();

        var exists = await db.Appointments.AnyAsync(a => a.Id == id, ct);
        if (!exists)
            return Results.NotFound(new { error = $"Appointment {id} not found." });

        var record = new OutreachRecord
        {
            AppointmentId = id,
            StaffId       = staffId,
            Notes         = request.Notes,
        };
        db.OutreachRecords.Add(record);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/appointments/{id}/outreach/{record.Id}", new { outreachRecordId = record.Id });
    }
}

/// <summary>Request body for POST /appointments/{id}/outreach.</summary>
public sealed record OutreachRequest(string? Notes);
