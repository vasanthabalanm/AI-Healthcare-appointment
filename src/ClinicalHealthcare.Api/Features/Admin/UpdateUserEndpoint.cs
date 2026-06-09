using System.Security.Claims;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Helpers;
using Microsoft.EntityFrameworkCore;

namespace ClinicalHealthcare.Api.Features.Admin;

/// <summary>
/// Vertical-slice endpoint: PATCH /admin/users/{id}
///
/// Updates user details and/or active status. Enforces the last-active-Admin
/// guard (AC-003) and writes an AuditLog UPDATE entry with before/after JSON
/// snapshots for every change (AC-004).
/// Requires the caller to be authenticated with the "admin" role.
/// </summary>
public sealed class UpdateUserEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // AddAuthorization is idempotent — safe to call from multiple slices.
        services.AddAuthorization(options =>
        {
            if (options.GetPolicy("AdminOnly") is null)
                options.AddPolicy("AdminOnly", p => p.RequireRole("admin"));
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPatch("/admin/users/{id:int}", HandleUpdateUser)
           .RequireAuthorization("AdminOnly")
           .WithName("AdminUpdateUser")
           .WithTags("Admin")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status404NotFound)
           .Produces(StatusCodes.Status409Conflict);
    }

    // ── PATCH /admin/users/{id} ─────────────────────────────────────────────

    public static async Task<IResult> HandleUpdateUser(
        int id,
        UpdateUserRequest request,
        ApplicationDbContext db,
        HttpContext httpContext,
        CancellationToken ct)
    {
        // Load target user — include soft-deleted so admins can see the full picture.
        var account = await db.UserAccounts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (account is null)
            return Results.NotFound(new { error = $"User account {id} not found." });

        // AC-004 — capture before state before any mutation.
        var before = CreateUserEndpoint.Snapshot(account);

        // AC-003 — guard: cannot deactivate the last active Admin account.
        if (request.IsActive is false && account.Role == "admin" && account.IsActive)
        {
            var activeAdminCount = await db.UserAccounts
                .IgnoreQueryFilters()
                .CountAsync(u => u.Role == "admin" && u.IsActive && !u.IsDeleted, ct);

            if (activeAdminCount <= 1)
                return Results.Conflict(new
                {
                    error = "Cannot deactivate the last active Admin account."
                });
        }

        // AC-002 — apply non-null fields.
        if (request.FirstName is not null)
        {
            var trimmedFirst = request.FirstName.Trim();
            if (trimmedFirst.Length == 0)
                return Results.UnprocessableEntity(new { errors = new { firstName = new[] { "First name cannot be blank." } } });
            account.FirstName = trimmedFirst;
        }

        if (request.LastName is not null)
        {
            var trimmedLast = request.LastName.Trim();
            if (trimmedLast.Length == 0)
                return Results.UnprocessableEntity(new { errors = new { lastName = new[] { "Last name cannot be blank." } } });
            account.LastName = trimmedLast;
        }

        if (request.IsActive is not null)
            account.IsActive = request.IsActive.Value;

        // AC-004 — stage AuditLog UPDATE entry; commit atomically with entity change.
        var actorId = CreateUserEndpoint.ExtractActorId(httpContext);
        AuditLogHelper.Stage(
            db,
            entityType:    "UserAccount",
            entityId:      account.Id,
            actorId:       actorId,
            action:        "UPDATE",
            before:        before,
            after:         CreateUserEndpoint.Snapshot(account),
            correlationId: httpContext.TraceIdentifier);

        await db.SaveChangesAsync(ct);

        return Results.Ok(new { message = "User account updated successfully.", userId = account.Id });
    }
}
