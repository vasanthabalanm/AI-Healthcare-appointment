using System.IdentityModel.Tokens.Jwt;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClinicalHealthcare.Api.Features.Appointments;

/// <summary>
/// Vertical-slice endpoint: POST /waitlist
///
/// Adds the authenticated patient to the appointment waitlist.
///
/// Duplicate guard (AC-002):
///   An application-level <see cref="DbSet{T}.AnyAsync"/> check is performed before insert
///   to give a clear 409 with a meaningful message. The filtered partial unique index on
///   <c>(PatientId) WHERE [Status] = 0</c> (from us_008) acts as a second safety net;
///   any race-condition duplicate is caught via <see cref="DbUpdateException"/> and also
///   surfaced as HTTP 409.
/// </summary>
public sealed class JoinWaitlistEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // PatientOnly policy — registered by GetSlotsEndpoint/BookAppointmentEndpoint first;
        // guard prevents double-registration.
        services.AddAuthorization(options =>
        {
            if (options.GetPolicy("PatientOnly") is null)
                options.AddPolicy("PatientOnly", p => p.RequireRole("patient"));
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/waitlist", HandleJoinWaitlist)
           .RequireAuthorization("PatientOnly")
           .WithName("JoinWaitlist")
           .WithTags("Appointments")
           .Produces(StatusCodes.Status201Created)
           .Produces(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status409Conflict);
    }

    // ── POST /waitlist ────────────────────────────────────────────────────────

    public static async Task<IResult> HandleJoinWaitlist(
        JoinWaitlistRequest  request,
        HttpContext          httpContext,
        ApplicationDbContext db,
        CancellationToken    ct)
    {
        // Extract authenticated patient's user ID from JWT sub claim.
        var subClaim = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                    ?? httpContext.User.FindFirst("sub")?.Value;

        if (!int.TryParse(subClaim, out var patientId))
            return Results.Unauthorized();

        // AC-003 — reject past dates immediately.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (request.PreferredSlotDate < today)
            return Results.BadRequest(new { error = "Cannot join waitlist for a past slot." });

        // F1 — validate PreferredSlotId before insert so an FK violation never reaches SaveChangesAsync.
        // Without this check a bad slot reference would throw DbUpdateException and return a misleading 409.
        if (request.PreferredSlotId.HasValue)
        {
            var slotExists = await db.Slots.AnyAsync(s => s.Id == request.PreferredSlotId.Value, ct);
            if (!slotExists)
                return Results.BadRequest(new { error = "Preferred slot not found." });
        }

        // AC-002 — application-level duplicate guard for a clear error message.
        var hasActive = await db.WaitlistEntries
            .AnyAsync(w => w.PatientId == patientId && w.Status == WaitlistStatus.Active, ct);

        if (hasActive)
            return Results.Conflict(new { error = "You already have an active waitlist entry. Remove it before joining again." });

        var entry = new WaitlistEntry
        {
            PatientId       = patientId,
            PreferredSlotId = request.PreferredSlotId,
            Status          = WaitlistStatus.Active,
            QueuedAt        = DateTime.UtcNow,
        };
        db.WaitlistEntries.Add(entry);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException?.Message.Contains("UIX_WaitlistEntries_PatientId_Active",
                      StringComparison.OrdinalIgnoreCase) == true)
        {
            // AC-004 — race-condition duplicate insert caught by the DB partial unique index.
            return Results.Conflict(new { error = "You already have an active waitlist entry." });
        }

        // AC-001 — return 201 with the new waitlist entry ID.
        return Results.Created($"/waitlist/{entry.Id}", new { waitlistEntryId = entry.Id });
    }
}
