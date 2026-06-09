using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicalHealthcare.Api.Features.Staff;

/// <summary>
/// Vertical-slice endpoint: PATCH /staff/queue/reorder
///
/// Accepts an ordered list of queue entry IDs and reassigns positions 1…N (AC-002).
///   1. Requires StaffOrAdmin role.
///   2. All current Waiting entry IDs for today MUST be present → 400 if any are missing.
///   3. Updates Position for each entry; EF Core checks RowVersion on each UPDATE.
///   4. <see cref="DbUpdateConcurrencyException"/> → 409 (AC-003).
///   5. Returns 200 on success.
/// </summary>
public sealed class ReorderQueueEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPatch("/staff/queue/reorder", HandleReorder)
           .RequireAuthorization("StaffOrAdmin")
           .WithName("ReorderQueue")
           .WithTags("Staff")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status409Conflict);
    }

    // ── PATCH /staff/queue/reorder ────────────────────────────────────────────

    public static async Task<IResult> HandleReorder(
        ReorderQueueRequest  request,
        ApplicationDbContext db,
        CancellationToken    ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Load all Waiting entries for today — tracked so EF Core applies concurrency checks.
        var entries = await db.QueueEntries
            .Where(q => q.QueueDate == today && q.Status == QueueStatus.Waiting)
            .ToListAsync(ct);

        // Edge case: all current Waiting IDs must be present in the request.
        var currentIds  = entries.Select(e => e.Id).ToHashSet();
        var requestedIds = request.OrderedIds.ToHashSet();

        if (!currentIds.SetEquals(requestedIds))
            return Results.BadRequest(new { error = "All queue entry IDs must be included in reorder." });

        // Build a lookup for fast access.
        var entryById = entries.ToDictionary(e => e.Id);

        // Assign new positions (1-based) in the order supplied by the caller.
        for (var i = 0; i < request.OrderedIds.Count; i++)
        {
            var entry = entryById[request.OrderedIds[i]];
            entry.Position = i + 1;

            // Apply the caller-supplied RowVersion so EF Core detects concurrent edits.
            if (request.RowVersions.TryGetValue(entry.Id, out var rv) && rv is { Length: > 0 })
                db.Entry(entry).OriginalValues[nameof(QueueEntry.RowVersion)] = rv;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // AC-003: another session modified one or more entries concurrently.
            return Results.Conflict(new { error = "Queue was modified concurrently. Refresh and retry." });
        }

        return Results.Ok(new { reordered = request.OrderedIds.Count });
    }
}

/// <summary>Request body for PATCH /staff/queue/reorder (AC-002).</summary>
public sealed record ReorderQueueRequest(
    /// <summary>All Waiting entry IDs for today, in the desired order.</summary>
    IReadOnlyList<int> OrderedIds,
    /// <summary>Map of entry ID → current RowVersion bytes for optimistic concurrency.</summary>
    IReadOnlyDictionary<int, byte[]> RowVersions);
