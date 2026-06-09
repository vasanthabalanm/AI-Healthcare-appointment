using System.Security.Claims;
using System.Text.Json;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace ClinicalHealthcare.Api.Authorization;

/// <summary>
/// Middleware that writes an <c>RBAC-Violation</c> AuditLog entry whenever a
/// request is terminated with HTTP 403 Forbidden (AC-002).
///
/// Placed <em>after</em> <c>UseAuthorization()</c> in the middleware pipeline.
/// Uses <c>Response.OnStarting</c> to intercept the response before headers are
/// flushed, ensuring the audit entry is committed atomically with the 403.
/// </summary>
public sealed class RbacViolationHandler
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RequestDelegate _next;
    private readonly IServiceScopeFactory _scopeFactory;

    public RbacViolationHandler(RequestDelegate next, IServiceScopeFactory scopeFactory)
    {
        _next = next;
        _scopeFactory = scopeFactory;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(WriteViolationAuditIfForbiddenAsync, context);
        await _next(context);
    }

    private async Task WriteViolationAuditIfForbiddenAsync(object state)
    {
        var context = (HttpContext)state;
        if (context.Response.StatusCode != StatusCodes.Status403Forbidden) return;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db             = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var policyProvider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();

            var actorIdStr = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? context.User.FindFirst("sub")?.Value;
            _ = int.TryParse(actorIdStr, out var actorId);

            var actualRole = context.User.FindFirst(ClaimTypes.Role)?.Value
                          ?? context.User.FindFirst("role")?.Value
                          ?? "anonymous";

            var requiredRoles = await ResolveRequiredRolesAsync(context, policyProvider);

            db.AuditLogs.Add(new AuditLog
            {
                EntityType    = "Endpoint",
                EntityId      = 0,
                ActorId       = actorId > 0 ? actorId : null,
                Action        = "RBAC-Violation",
                BeforeValue   = null,
                AfterValue    = JsonSerializer.Serialize(new
                {
                    attemptedEndpoint = context.Request.Path.Value,
                    requiredRole      = requiredRoles,
                    actualRole
                }, _json),
                OccurredAt    = DateTime.UtcNow,
                CorrelationId = context.TraceIdentifier
            });

            await db.SaveChangesAsync();
        }
        catch
        {
            // Audit failures must never suppress the primary 403 response.
        }
    }

    /// <summary>
    /// Reads <see cref="IAuthorizeData.Roles"/> first; falls back to resolving
    /// the named policy and extracting its <see cref="RolesAuthorizationRequirement"/>.
    /// </summary>
    private static async Task<string> ResolveRequiredRolesAsync(
        HttpContext context,
        IAuthorizationPolicyProvider policyProvider)
    {
        var endpoint      = context.GetEndpoint();
        var authorizeData = endpoint?.Metadata.GetOrderedMetadata<IAuthorizeData>() ?? [];

        foreach (var data in authorizeData)
        {
            if (!string.IsNullOrEmpty(data.Roles))
                return data.Roles;

            if (!string.IsNullOrEmpty(data.Policy))
            {
                var policy = await policyProvider.GetPolicyAsync(data.Policy);
                if (policy is not null)
                {
                    return string.Join(", ", policy.Requirements
                        .OfType<RolesAuthorizationRequirement>()
                        .SelectMany(r => r.AllowedRoles)
                        .Distinct());
                }
            }
        }

        return string.Empty;
    }
}
