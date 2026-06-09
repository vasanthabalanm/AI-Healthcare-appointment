using System.Text;
using ClinicalHealthcare.Api.Infrastructure;
using Hangfire;
using Hangfire.AspNetCore;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Infrastructure;

/// <summary>
/// Unit tests for <see cref="HangfireDashboardAuthFilter"/> JWT-role access control (US_004).
/// </summary>
public sealed class HangfireDashboardAuthFilterTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a minimal unsigned JWT (header.payload.sig) with the given role claim.
    /// The filter decodes the payload but does NOT validate the signature — that is
    /// intentional per the source code comment (gateway already validated it).
    /// </summary>
    private static string CreateJwtWithRole(string role)
    {
        var header  = Base64UrlEncode("{\"alg\":\"HS256\",\"typ\":\"JWT\"}");
        var payload = Base64UrlEncode($"{{\"role\":\"{role}\"}}");
        return $"{header}.{payload}.sig";
    }

    private static string Base64UrlEncode(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes)
                      .TrimEnd('=')
                      .Replace('+', '-')
                      .Replace('/', '_');
    }

    /// <summary>
    /// Builds an <see cref="AspNetCoreDashboardContext"/> wrapping the supplied
    /// <see cref="HttpContext"/> so that <c>context.GetHttpContext()</c> works correctly.
    /// </summary>
    private static DashboardContext BuildDashboardContext(HttpContext httpContext)
    {
        // AspNetCoreDashboardContext requires a non-null IServiceProvider on HttpContext.RequestServices.
        httpContext.RequestServices ??= new ServiceCollection().BuildServiceProvider();
        var storage = new Mock<JobStorage>().Object;
        return new AspNetCoreDashboardContext(storage, new DashboardOptions(), httpContext);
    }

    // ── TC-004: Bearer JWT with role=admin → Authorize returns true ───────────

    [Fact]
    public void Authorize_AdminRoleJwt_ReturnsTrue()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = $"Bearer {CreateJwtWithRole("admin")}";

        var filter = new HangfireDashboardAuthFilter();
        var result = filter.Authorize(BuildDashboardContext(httpContext));

        Assert.True(result);
    }

    // TC-004: role claim is case-insensitive — "Admin" also grants access
    [Fact]
    public void Authorize_AdminRoleCapitalized_ReturnsTrue()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = $"Bearer {CreateJwtWithRole("Admin")}";

        var filter = new HangfireDashboardAuthFilter();
        var result = filter.Authorize(BuildDashboardContext(httpContext));

        Assert.True(result);
    }

    // ── TC-005: Bearer JWT with role=staff → 403 and returns false ────────────

    [Fact]
    public void Authorize_NonAdminRoleJwt_ReturnsFalseAnd403()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = $"Bearer {CreateJwtWithRole("staff")}";

        var filter = new HangfireDashboardAuthFilter();
        var result = filter.Authorize(BuildDashboardContext(httpContext));

        Assert.False(result);
        Assert.Equal(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);
    }

    // TC-005: no Authorization header at all → 403 and returns false
    [Fact]
    public void Authorize_NoAuthorizationHeader_ReturnsFalseAnd403()
    {
        var httpContext = new DefaultHttpContext(); // no header
        var filter = new HangfireDashboardAuthFilter();

        var result = filter.Authorize(BuildDashboardContext(httpContext));

        Assert.False(result);
        Assert.Equal(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);
    }

    // TC-005: malformed JWT (not 3 parts) → 403 and returns false
    [Fact]
    public void Authorize_MalformedJwt_ReturnsFalseAnd403()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer notavalidjwt";

        var filter = new HangfireDashboardAuthFilter();
        var result = filter.Authorize(BuildDashboardContext(httpContext));

        Assert.False(result);
        Assert.Equal(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);
    }
}
