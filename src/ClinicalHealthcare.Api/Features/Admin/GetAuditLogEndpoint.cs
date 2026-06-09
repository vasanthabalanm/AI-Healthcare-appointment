using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicalHealthcare.Api.Features.Admin;

/// <summary>
/// Vertical-slice endpoint: GET /audit
///
/// Returns a paginated view of the AuditLog table ordered by OccurredAt DESC.
/// Only Admin role may access this endpoint (AC-005).
/// </summary>
public sealed class GetAuditLogEndpoint : IEndpointDefinition
{
    /// <summary>Default and maximum rows returned per page (AC-001).</summary>
    public const int DefaultPageSize = 50;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // Authorization is centralised in Program.cs; the idempotent guard prevents
        // duplicate policy registration when multiple slices call AddAuthorization.
        services.AddAuthorization(options =>
        {
            if (options.GetPolicy("AdminOnly") is null)
                options.AddPolicy("AdminOnly", p => p.RequireRole("admin"));
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/audit", HandleGetAuditLog)
           .RequireAuthorization("AdminOnly")
           .WithName("GetAuditLog")
           .WithTags("Audit")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status401Unauthorized)
           .Produces(StatusCodes.Status403Forbidden);
    }

    // ── GET /audit?page=1&pageSize=50 ────────────────────────────────────────

    public static async Task<IResult> HandleGetAuditLog(
        ApplicationDbContext db,
        int page     = 1,
        int pageSize = DefaultPageSize,
        CancellationToken ct = default)
    {
        // Clamp to valid range: page ≥ 1, pageSize 1–DefaultPageSize.
        page     = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, DefaultPageSize);

        var totalCount = await db.AuditLogs.CountAsync(ct);

        var items = await (
            from a in db.AuditLogs.AsNoTracking()
                            .OrderByDescending(x => x.OccurredAt)
                            .Skip((page - 1) * pageSize)
                            .Take(pageSize)
            join u in db.UserAccounts.IgnoreQueryFilters()
                on a.ActorId equals (int?)u.Id into uj
            from u in uj.DefaultIfEmpty()
            select new
            {
                id         = a.Id,
                timestamp  = a.OccurredAt,
                actorId    = a.ActorId ?? 0,
                actorName  = u != null ? u.FirstName + " " + u.LastName : "System",
                actorRole  = u != null ? u.Role : "system",
                action     = a.Action,
                detail     = a.AfterValue ?? a.BeforeValue ?? a.Action,
                entityId   = a.EntityId.ToString(),
                entityType = a.EntityType,
            }
        ).ToListAsync(ct);

        var pageCount = totalCount == 0 ? 0 : (int)Math.Ceiling((double)totalCount / pageSize);

        return Results.Ok(new
        {
            items,
            total      = totalCount,
            pageCount,
            page,
            pageSize
        });
    }
}
