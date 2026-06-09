using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClinicalHealthcare.Api.Features.Appointments;

/// <summary>
/// Vertical-slice endpoint: GET /slots
///
/// Returns available appointment slots for a given date.
/// Implements a Redis cache-aside pattern with TTL=60s (AC-001 / AC-002):
///   Cache hit  → return cached list; no SQL query.
///   Cache miss → query SQL Server; populate cache; return results.
///
/// Requires Patient role — staff/admin manage slots through back-office tooling.
/// </summary>
public sealed class GetSlotsEndpoint : IEndpointDefinition
{
    /// <summary>Redis key prefix for slot cache entries (one key per date).</summary>
    public const string CacheKeyPrefix = "slots:date:";

    /// <summary>Cache TTL for slot availability — 60 s per design.md (AC-001).</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // PatientOnly policy — guarded to prevent double-registration when both
        // GetSlotsEndpoint and BookAppointmentEndpoint call AddServices.
        services.AddAuthorization(options =>
        {
            if (options.GetPolicy("PatientOnly") is null)
                options.AddPolicy("PatientOnly", p => p.RequireRole("patient"));
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/slots", HandleGetSlots)
           .RequireAuthorization("AnyAuthenticated")
           .WithName("GetSlots")
           .WithTags("Appointments")
           .Produces<List<SlotDto>>(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status400BadRequest);
    }

    // ── DTO ────────────────────────────────────────────────────────────────────

    /// <param name="IsAvailable">
    /// Only present when the request includes <c>allSlots=true</c>.
    /// Omitted (null) for the standard available-only response so existing callers are unaffected.
    /// </param>
    public sealed record SlotDto(int Id, DateTime SlotTime, int DurationMinutes, bool? IsAvailable = null);

    // ── GET /slots ─────────────────────────────────────────────────────────────

    public static async Task<IResult> HandleGetSlots(
        [Microsoft.AspNetCore.Mvc.FromQuery] string? date,
        [Microsoft.AspNetCore.Mvc.FromQuery] bool?   allSlots,
        ICacheService                                cache,
        ApplicationDbContext                         db,
        CancellationToken                            ct)
    {
        // Validate and parse the date query parameter.
        if (string.IsNullOrWhiteSpace(date) || !DateOnly.TryParse(date, out var parsedDate))
            return Results.BadRequest(new { error = "A valid 'date' query parameter is required (e.g. ?date=2026-06-01)." });

        // allSlots=true is used by the calendar to detect fully-booked days.
        // A separate cache key is used so it never evicts the standard available-only entry.
        var fetchAll = allSlots == true;
        var cacheKey = fetchAll
            ? $"{CacheKeyPrefix}all:{parsedDate:yyyy-MM-dd}"
            : $"{CacheKeyPrefix}{parsedDate:yyyy-MM-dd}";

        // AC-001 — try Redis cache first.
        var cached = await cache.GetAsync<List<SlotDto>>(cacheKey, ct);
        if (cached is not null)
            return Results.Ok(cached);

        // AC-002 — cache miss: query database.
        var dayStart = parsedDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dayEnd   = dayStart.AddDays(1);
        var now      = DateTime.UtcNow;

        List<SlotDto> slots;
        if (fetchAll)
        {
            // Return ALL slots for the day (available + unavailable) so the UI can
            // determine if a day is fully booked vs having no slots scheduled.
            slots = await db.Slots
                .AsNoTracking()
                .Where(s => s.SlotTime >= dayStart && s.SlotTime < dayEnd)
                .OrderBy(s => s.SlotTime)
                .Select(s => new SlotDto(s.Id, s.SlotTime, s.DurationMinutes, s.IsAvailable))
                .ToListAsync(ct);
        }
        else
        {
            // Only return slots that are both available and still in the future.
            slots = await db.Slots
                .AsNoTracking()
                .Where(s => s.IsAvailable && s.SlotTime >= dayStart && s.SlotTime < dayEnd && s.SlotTime > now)
                .OrderBy(s => s.SlotTime)
                .Select(s => new SlotDto(s.Id, s.SlotTime, s.DurationMinutes, (bool?)null))
                .ToListAsync(ct);
        }

        // AC-002 — populate cache for next request.
        await cache.SetAsync(cacheKey, slots, CacheTtl, ct);

        return Results.Ok(slots);
    }
}
