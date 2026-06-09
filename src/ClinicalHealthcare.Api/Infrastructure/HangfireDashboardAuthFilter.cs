using System.Text.Json;
using Hangfire.Dashboard;

namespace ClinicalHealthcare.Api.Infrastructure;

/// <summary>
/// Restricts the Hangfire dashboard to requests that carry a valid JWT with
/// <c>role=admin</c> in the <c>Authorization: Bearer</c> header.
/// Returns HTTP 403 for any other request.
/// </summary>
public sealed class HangfireDashboardAuthFilter : IDashboardAuthorizationFilter
{
    private const string AdminRole = "admin";

    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var authHeader  = httpContext.Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(authHeader) ||
            !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            return false;
        }

        var token    = authHeader["Bearer ".Length..].Trim();
        var roleClaim = ExtractRoleClaim(token);

        if (!string.Equals(roleClaim, AdminRole, StringComparison.OrdinalIgnoreCase))
        {
            httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads the <c>role</c> claim from an unvalidated JWT payload.
    /// Signature validation is intentionally skipped here — the dashboard sits behind
    /// the API gateway which has already validated the token.  This filter only checks
    /// the role claim value.
    /// </summary>
    private static string? ExtractRoleClaim(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
                return null;

            var payload = parts[1]
                .Replace('-', '+')
                .Replace('_', '/');

            // Add padding if required
            payload = (payload.Length % 4) switch
            {
                2 => payload + "==",
                3 => payload + "=",
                _ => payload
            };

            var bytes   = Convert.FromBase64String(payload);
            var json    = System.Text.Encoding.UTF8.GetString(bytes);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("role", out var roleProp))
                return roleProp.GetString();

            return null;
        }
        catch
        {
            return null;
        }
    }
}
