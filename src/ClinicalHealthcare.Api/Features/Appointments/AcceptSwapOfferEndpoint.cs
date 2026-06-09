using System.IdentityModel.Tokens.Jwt;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClinicalHealthcare.Api.Features.Appointments;

/// <summary>
/// Vertical-slice endpoint: POST /waitlist/{id}/accept
///
/// Allows the authenticated patient to accept a swap offer.
///
/// AC-004 — Performs an EF Core transaction:
///   1. Verifies the WaitlistEntry is in <see cref="WaitlistStatus.OfferSent"/> state
///      and the offer window has not elapsed.
///   2. Loads the offered Slot and verifies ownership via JWT sub claim.
///   3. Creates a new <see cref="Appointment"/> with <see cref="AppointmentStatus.Scheduled"/>.
///   4. Marks the Slot unavailable (rowversion concurrency token prevents double-accept race).
///   5. Sets the WaitlistEntry to <see cref="WaitlistStatus.Fulfilled"/>.
///   6. Invalidates the Redis slot cache for the booking date.
///
/// A concurrent second accept on the same entry is detected by the rowversion mismatch
/// on the Slot row and returns HTTP 409.
/// </summary>
public sealed class AcceptSwapOfferEndpoint : IEndpointDefinition
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
        app.MapPost("/waitlist/{id:int}/accept", HandleAcceptSwapOffer)
           .RequireAuthorization("PatientOnly")
           .WithName("AcceptSwapOffer")
           .WithTags("Appointments")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status409Conflict);
    }

    // ── POST /waitlist/{id}/accept ────────────────────────────────────────────

    public static async Task<IResult> HandleAcceptSwapOffer(
        int                  id,
        HttpContext          httpContext,
        ApplicationDbContext db,
        ICacheService        cache,
        CancellationToken    ct)
    {
        // Extract authenticated patient's user ID from JWT sub claim.
        var subClaim = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                    ?? httpContext.User.FindFirst("sub")?.Value;

        if (!int.TryParse(subClaim, out var patientId))
            return Results.Unauthorized();

        // Load entry — must exist and belong to this patient.
        var entry = await db.WaitlistEntries.FirstOrDefaultAsync(w => w.Id == id, ct);

        if (entry is null)
            return Results.BadRequest(new { error = "Waitlist entry not found." });

        if (entry.PatientId != patientId)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        if (entry.Status != WaitlistStatus.OfferSent)
            return Results.BadRequest(new { error = "No active swap offer on this waitlist entry." });

        if (entry.OfferExpiresAt is null || entry.OfferExpiresAt <= DateTime.UtcNow)
            return Results.BadRequest(new { error = "Swap offer has expired." });

        if (!entry.OfferedSlotId.HasValue)
            return Results.BadRequest(new { error = "No slot associated with this offer." });

        // Load slot with rowversion for optimistic concurrency check.
        var slot = await db.Slots.FirstOrDefaultAsync(s => s.Id == entry.OfferedSlotId.Value, ct);

        if (slot is null)
            return Results.BadRequest(new { error = "Offered slot not found." });

        // AC-004 — atomic transaction: appointment + slot + entry in one write.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        try
        {
            // Hold the slot (should already be unavailable; set defensively).
            slot.IsAvailable = false;

            var appointment = new Appointment
            {
                PatientId = patientId,
                SlotId    = slot.Id,
                Status    = AppointmentStatus.Scheduled,
                BookedAt  = DateTime.UtcNow,
            };
            db.Appointments.Add(appointment);

            entry.Status = WaitlistStatus.Fulfilled;

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            // Invalidate slot cache for the booking date.
            var dateKey = $"{GetSlotsEndpoint.CacheKeyPrefix}{DateOnly.FromDateTime(slot.SlotTime):yyyy-MM-dd}";
            await cache.DeleteAsync(dateKey, ct);

            return Results.Ok(new { appointmentId = appointment.Id });
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            // AC-004 race: another patient accepted the same offer concurrently.
            return Results.Conflict(new { error = "Slot no longer available — offer may have been accepted by another session." });
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
}
