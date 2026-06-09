using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClinicalHealthcare.Api.Features.Admin;

/// <summary>
/// Vertical-slice endpoint: GET /admin/metrics/code-agreement?days=N
///
/// Computes the AI-Human agreement rate over a rolling time window.
///
/// Agreement rate = count(Accepted where committedCode == suggestedCode) / totalActioned.
///
/// Parameter rules:
///   - <c>days</c> default 30 when absent.
///   - <c>days &lt;= 0</c> → 422.
///   - <c>days &gt; 365</c> → capped to 365 + WARNING log.
///
/// Guards:
///   - <c>totalActioned == 0</c> → 200 with <c>agreementRate:null</c> + message (AC-004).
///
/// Security: Admin role only (AC-003).
/// </summary>
public sealed class CodeAgreementMetricEndpoint : IEndpointDefinition
{
    internal const int DefaultDays = 30;
    internal const int MaxDays     = 365;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthorization(options =>
        {
            if (options.GetPolicy("AdminOnly") is null)
                options.AddPolicy("AdminOnly", p => p.RequireRole("admin"));
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/metrics/code-agreement", HandleGetCodeAgreement)
           .RequireAuthorization("AdminOnly")
           .WithName("GetCodeAgreementMetric")
           .WithTags("Admin")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status401Unauthorized)
           .Produces(StatusCodes.Status403Forbidden)
           .Produces(StatusCodes.Status422UnprocessableEntity);
    }

    // ── GET /admin/metrics/code-agreement?days=N ──────────────────────────────

    public static async Task<IResult> HandleGetCodeAgreement(
        ClinicalDbContext                            pgDb,
        ILogger<CodeAgreementMetricEndpoint>         logger,
        int                                          days = DefaultDays,
        CancellationToken                            ct   = default)
    {
        // AC-005: validate days parameter.
        if (days <= 0)
            return Results.UnprocessableEntity(
                new { error = "days parameter must be greater than 0" });

        if (days > MaxDays)
        {
            logger.LogWarning("days capped to {MaxDays} (requested {Days})", MaxDays, days);
            days = MaxDays;
        }

        var windowStart = DateTime.UtcNow.AddDays(-days);

        // Load actioned rows in window (Status != Pending, VerifiedAt in range).
        // ToList is intentional — all three counts share the same in-memory set.
        var actioned = await pgDb.MedicalCodeSuggestions
            .AsNoTracking()
            .Where(s => s.Status != SuggestionStatus.Pending
                     && s.VerifiedAt >= windowStart)
            .Select(s => new { s.Status, s.SuggestedCode, s.CommittedCode })
            .ToListAsync(ct);

        var totalActioned = actioned.Count;

        // AC-004: zero actioned — return full DTO with zero sub-counts (AC-001 contract).
        if (totalActioned == 0)
            return Results.Ok(new
            {
                agreementRate = (double?)null,
                totalActioned = 0,
                accepted      = 0,
                modified      = 0,
                rejected      = 0,
                message       = "No code suggestions actioned in the selected period",
                windowDays    = days
            });

        // AC-002: agreement = Accepted AND committedCode == suggestedCode (or committedCode is null).
        var acceptedUnmodified = actioned.Count(s =>
            s.Status == SuggestionStatus.Accepted
            && (s.CommittedCode is null || s.CommittedCode == s.SuggestedCode));

        var accepted = actioned.Count(s => s.Status == SuggestionStatus.Accepted);
        var modified = actioned.Count(s =>
            s.Status == SuggestionStatus.Modified
            || (s.Status == SuggestionStatus.Accepted
                && s.CommittedCode is not null
                && s.CommittedCode != s.SuggestedCode));
        var rejected = actioned.Count(s => s.Status == SuggestionStatus.Rejected);

        var agreementRate = (double)acceptedUnmodified / totalActioned;

        // AC-001: return full DTO.
        // Note: accepted + modified + rejected may exceed totalActioned by design.
        // An Accepted row where committedCode != suggestedCode appears in both
        // 'accepted' (Status=Accepted) and 'modified' (human changed the AI code).
        // This is intentional: 'accepted' = "human accepted at all",
        // 'modified' = "human changed the suggested code".
        return Results.Ok(new
        {
            agreementRate = Math.Round(agreementRate, 4),
            totalActioned = totalActioned,
            accepted      = accepted,
            modified      = modified,
            rejected      = rejected,
            windowDays    = days
        });
    }
}
