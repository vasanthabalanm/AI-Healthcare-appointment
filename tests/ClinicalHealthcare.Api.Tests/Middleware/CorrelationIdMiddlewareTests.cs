using ClinicalHealthcare.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Xunit;

namespace ClinicalHealthcare.Api.Tests.Middleware;

/// <summary>
/// Unit tests for <see cref="CorrelationIdMiddleware"/> (US_006 / AC-001).
/// Uses <see cref="FireableResponseFeature"/> to manually trigger the
/// <c>Response.OnStarting</c> callback so the response header is populated.
/// </summary>
public sealed class CorrelationIdMiddlewareTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (DefaultHttpContext ctx, FireableResponseFeature feature)
        BuildContext(string? correlationId = null)
    {
        var feature = new FireableResponseFeature();
        var ctx     = new DefaultHttpContext();
        ctx.Features.Set<IHttpResponseFeature>(feature);

        if (correlationId is not null)
            ctx.Request.Headers["X-Correlation-ID"] = correlationId;

        return (ctx, feature);
    }

    private static async Task<string?> RunMiddlewareAsync(DefaultHttpContext ctx,
                                                          FireableResponseFeature feature)
    {
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(ctx);
        await feature.FireAsync(); // trigger OnStarting callbacks
        return ctx.Response.Headers["X-Correlation-ID"].FirstOrDefault();
    }

    // ── TC-001: absent header → new GUID generated ───────────────────────────

    [Fact]
    public async Task InvokeAsync_NoHeader_GeneratesNewGuid()
    {
        var (ctx, feature) = BuildContext(); // no header
        var headerValue    = await RunMiddlewareAsync(ctx, feature);

        Assert.NotNull(headerValue);
        Assert.True(
            Guid.TryParse(headerValue, out _),
            $"Expected a GUID but got: '{headerValue}'");
    }

    // ── TC-002: present header → same value echoed back ───────────────────────

    [Fact]
    public async Task InvokeAsync_HeaderPresent_EchoesHeader()
    {
        var (ctx, feature) = BuildContext("test-corr-123");
        var headerValue    = await RunMiddlewareAsync(ctx, feature);

        Assert.Equal("test-corr-123", headerValue);
    }

    // ── TC-003: X-Correlation-ID response header is always present ───────────

    [Theory]
    [InlineData(null)]
    [InlineData("provided-id")]
    public async Task InvokeAsync_AlwaysSetsResponseHeader(string? incomingId)
    {
        var (ctx, feature) = BuildContext(incomingId);
        await RunMiddlewareAsync(ctx, feature);

        Assert.True(
            ctx.Response.Headers.ContainsKey("X-Correlation-ID"),
            "Response must always contain the X-Correlation-ID header.");
    }

    // ── TC-002: header longer than 64 chars is truncated to 64 ───────────────

    [Fact]
    public async Task InvokeAsync_LongHeader_IsTruncatedTo64Characters()
    {
        var longId         = new string('x', 100);
        var (ctx, feature) = BuildContext(longId);
        var headerValue    = await RunMiddlewareAsync(ctx, feature);

        Assert.NotNull(headerValue);
        Assert.Equal(64, headerValue!.Length);
        Assert.Equal(new string('x', 64), headerValue);
    }

    // ── ES-001: empty string header is propagated as-is (NOT replaced with GUID)
    // The null-coalescing check triggers only on null (absent header),
    // not on empty string (present but empty header).

    [Fact]
    public async Task InvokeAsync_EmptyStringHeader_PropagatesEmptyString()
    {
        var (ctx, feature) = BuildContext(""); // present but empty
        var headerValue    = await RunMiddlewareAsync(ctx, feature);

        // Empty string is used as-is — it is the caller's responsibility to send a valid ID.
        Assert.Equal("", headerValue);
    }
}
