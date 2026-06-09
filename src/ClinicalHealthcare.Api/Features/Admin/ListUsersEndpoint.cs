using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicalHealthcare.Api.Features.Admin;

/// <summary>
/// Vertical-slice endpoint: GET /admin/users
///
/// Returns a paginated, filterable list of non-patient user accounts.
/// Only Admin role may access this endpoint.
/// </summary>
public sealed class ListUsersEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/users", HandleListUsers)
           .RequireAuthorization("AdminOnly")
           .WithName("AdminListUsers")
           .WithTags("Admin")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status401Unauthorized)
           .Produces(StatusCodes.Status403Forbidden);
    }

    public static async Task<IResult> HandleListUsers(
        ApplicationDbContext db,
        string? search   = null,
        string? role     = null,
        string? active   = null,
        int     page     = 1,
        int     pageSize = 20,
        CancellationToken ct = default)
    {
        page     = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.UserAccounts.AsNoTracking()
            .Where(u => u.Role != "patient");  // admin user management only covers admin/staff

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(u =>
                u.Email.Contains(term) ||
                u.FirstName.Contains(term) ||
                u.LastName.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(role))
            query = query.Where(u => u.Role == role.ToLowerInvariant());

        if (active == "active")
            query = query.Where(u => u.IsActive);
        else if (active == "inactive")
            query = query.Where(u => !u.IsActive);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.FirstName,
                u.LastName,
                u.Role,
                u.IsActive,
                lastLoginAt = (string?)null   // reserved for future audit-derived last-login
            })
            .ToListAsync(ct);

        return Results.Ok(new { items, total, page, pageSize });
    }
}
