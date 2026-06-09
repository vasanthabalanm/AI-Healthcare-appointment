using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.AI;
using ClinicalHealthcare.Infrastructure.Cache;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicalHealthcare.Api.Features.Intake;

/// <summary>
/// Vertical-slice endpoint: POST /intake/ai/start
///
/// Starts a new AI conversational intake session (AC-001 / AC-002):
///   1. Generates a CSPRNG session ID.
///   2. Creates an empty <see cref="AiIntakeSession"/> in Redis with TTL=900s.
///   3. Sends a greeting to Rasa and returns its reply + the session ID.
///
/// Returns HTTP 503 when Rasa is unavailable (AC-005).
/// </summary>
public sealed class StartAiIntakeEndpoint : IEndpointDefinition
{
    internal const int SessionTtlSeconds = 900;
    internal static readonly TimeSpan SessionTtl = TimeSpan.FromSeconds(SessionTtlSeconds);
    internal static string CacheKey(string sessionId) => $"ai-intake:{sessionId}";

    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/intake/ai/start", HandleStartAiIntake)
           .RequireAuthorization("PatientOnly")
           .WithName("StartAiIntake")
           .WithTags("Intake")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status503ServiceUnavailable);
    }

    // ── POST /intake/ai/start ─────────────────────────────────────────────────

    public static async Task<IResult> HandleStartAiIntake(
        HttpContext         httpContext,
        ICacheService       cache,
        IRasaIntakeService   rasa,
        CancellationToken   ct)
    {
        var patientId = ExtractPatientId(httpContext);
        if (patientId is null)
            return Results.Unauthorized();

        // Generate a cryptographically-random session ID.
        var sessionId = Guid.NewGuid().ToString("N");

        // Persist empty session with TTL=900s (AC-002).
        var session = new AiIntakeSession
        {
            SessionId = sessionId,
            PatientId = patientId.Value,
        };
        await cache.SetAsync(CacheKey(sessionId), session, SessionTtl, ct);

        // Send greeting to Rasa — on failure return 503 (AC-005).
        try
        {
            var greeting = await rasa.SendMessageAsync(sessionId, "/greet", ct);
            return Results.Ok(new
            {
                sessionId,
                message = greeting.Text ?? "Hello! I'll help you complete your intake form."
            });
        }
        catch (RasaUnavailableException)
        {
            // Clean up the orphaned Redis key before returning 503.
            await cache.DeleteAsync(CacheKey(sessionId), ct);
            return Results.Json(
                new { error = "AI intake service unavailable" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    internal static int? ExtractPatientId(HttpContext ctx)
    {
        var sub = ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
               ?? ctx.User.FindFirst("sub")?.Value;
        return int.TryParse(sub, out var id) ? id : null;
    }
}
