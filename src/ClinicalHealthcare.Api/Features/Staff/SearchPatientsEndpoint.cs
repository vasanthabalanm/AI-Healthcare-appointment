using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicalHealthcare.Api.Features.Staff;

/// <summary>
/// Vertical-slice endpoint: GET /staff/patients/search
///
/// Returns matching patients by partial name and/or DOB filter (AC-001).
///   1. Requires StaffOrAdmin role.
///   2. Performs case-insensitive LIKE search via <c>EF.Functions.Like</c> on FirstName + LastName.
///   3. Optionally filters by exact DateOfBirth.
///   4. Returns [{id, fullName, dob, email}].
/// </summary>
public sealed class SearchPatientsEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/staff/patients/search", HandleSearch)
           .RequireAuthorization("StaffOrAdmin")
           .WithName("SearchPatients")
           .WithTags("Staff")
           .Produces<IReadOnlyList<PatientSearchResult>>(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status400BadRequest);
    }

    // ── GET /staff/patients/search?q=&dob= ────────────────────────────────────

    public static async Task<IResult> HandleSearch(
        string?              q,
        DateOnly?            dob,
        ApplicationDbContext db,
        CancellationToken    ct)
    {
        // q is optional — if neither q nor dob is provided return empty list to avoid full-table scans.
        if (string.IsNullOrWhiteSpace(q) && dob is null)
            return Results.Ok(Array.Empty<PatientSearchResult>());

        var pattern = string.IsNullOrWhiteSpace(q) ? null : $"%{q.Trim()}%";

        // AC-001: parameterised LIKE on concatenated full name; optional DOB filter.
        // EF.Functions.Like is translated to SQL LIKE — no client-side evaluation.
        var query = db.UserAccounts
            .Where(u => u.Role == "patient")
            .AsNoTracking();

        if (pattern is not null)
            query = query.Where(u =>
                EF.Functions.Like(u.FirstName + " " + u.LastName, pattern) ||
                EF.Functions.Like(u.LastName  + " " + u.FirstName, pattern));

        if (dob is not null)
            query = query.Where(u => u.DateOfBirth == dob);

        var results = await query
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .Select(u => new PatientSearchResult(
                u.Id,
                u.FirstName + " " + u.LastName,
                u.DateOfBirth,
                u.Email))
            .Take(50)   // safety cap — prevents unbounded result sets
            .ToListAsync(ct);

        return Results.Ok(results);
    }
}

/// <summary>Patient search result DTO (AC-001).</summary>
public sealed record PatientSearchResult(
    int      Id,
    string   FullName,
    DateOnly? Dob,
    string   Email);
