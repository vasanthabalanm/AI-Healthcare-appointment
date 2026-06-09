using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicalHealthcare.Api.Features.Staff;

/// <summary>
/// Vertical-slice endpoint: DELETE /staff/queue/{entryId}
///
/// Soft-removes a patient from today's queue by setting Status=Removed (AC-004).
///   1. Requires StaffOrAdmin role.
///   2. Returns 404 if the entry does not exist or is not today's.
///   3. Returns 200 on success; entry will no longer appear in GET /staff/queue.
/// </summary>
public sealed class RemoveQueueEntryEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapDelete("/staff/queue/{entryId:int}", HandleRemove)
           .RequireAuthorization("StaffOrAdmin")
           .WithName("RemoveQueueEntry")
           .WithTags("Staff")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status404NotFound);
    }

    // ── DELETE /staff/queue/{entryId} ─────────────────────────────────────────

    public static async Task<IResult> HandleRemove(
        int                  entryId,
        ApplicationDbContext db,
        CancellationToken    ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var entry = await db.QueueEntries
            .FirstOrDefaultAsync(q => q.Id == entryId && q.QueueDate == today, ct);

        if (entry is null)
            return Results.NotFound(new { error = $"Queue entry {entryId} not found for today." });

        // AC-004: soft-remove — Status=Removed; entry excluded from GET /staff/queue filter.
        entry.Status = QueueStatus.Removed;
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { removedEntryId = entryId });
    }
}
