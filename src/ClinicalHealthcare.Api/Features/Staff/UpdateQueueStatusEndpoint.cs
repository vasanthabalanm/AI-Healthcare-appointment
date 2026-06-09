using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicalHealthcare.Api.Features.Staff;

/// <summary>
/// Vertical-slice endpoint: PATCH /staff/queue/{entryId}/status
///
/// Updates the status of a queue entry (Waiting → InProgress → Completed).
///   1. Requires StaffOrAdmin role.
///   2. Accepted transitions:
///      Waiting   → InProgress (staff clicks "See")
///      InProgress → Completed  (staff clicks "Done" — soft-removes the entry)
///   3. Returns 404 if the entry does not exist for today.
///   4. Returns 400 for invalid or illegal transitions.
/// </summary>
public sealed class UpdateQueueStatusEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPatch("/staff/queue/{entryId:int}/status", HandleUpdateStatus)
           .RequireAuthorization("StaffOrAdmin")
           .WithName("UpdateQueueStatus")
           .WithTags("Staff")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status404NotFound);
    }

    // ── PATCH /staff/queue/{entryId}/status ──────────────────────────────────

    public static async Task<IResult> HandleUpdateStatus(
        int                        entryId,
        UpdateQueueStatusRequest   body,
        ApplicationDbContext       db,
        CancellationToken          ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var entry = await db.QueueEntries
            .FirstOrDefaultAsync(q => q.Id == entryId && q.QueueDate == today, ct);

        if (entry is null)
            return Results.NotFound(new { error = $"Queue entry {entryId} not found for today." });

        switch (body.Status)
        {
            case "InProgress" when entry.Status == QueueStatus.Waiting:
                entry.Status = QueueStatus.CheckedIn;
                break;

            case "Completed" when entry.Status == QueueStatus.CheckedIn:
                entry.Status = QueueStatus.Removed;
                break;

            default:
                return Results.BadRequest(new
                {
                    error = $"Cannot transition from '{entry.Status}' to '{body.Status}'. " +
                            "Allowed: Waiting→InProgress, InProgress→Completed."
                });
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(new { entryId, status = body.Status });
    }
}

/// <summary>Request body for PATCH /staff/queue/{entryId}/status.</summary>
public sealed record UpdateQueueStatusRequest(string Status);
