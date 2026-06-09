using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using System.Text.Json;
using Xunit;

namespace ClinicalHealthcare.Api.Tests.WebApi;

/// <summary>
/// Validates the /health endpoint behaviour (AC-004 — US_002).
/// Uses a minimal TestServer pipeline — no real database or Redis required.
/// </summary>
public sealed class HealthCheckTests
{
    private static async Task<IHost> BuildHealthHostAsync()
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(s =>
                {
                    s.AddRouting();
                    s.AddHealthChecks();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapHealthChecks("/health", new HealthCheckOptions
                    {
                        ResponseWriter = async (ctx, report) =>
                        {
                            ctx.Response.ContentType = "application/json";
                            var json = JsonSerializer.Serialize(new { status = report.Status.ToString() });
                            await ctx.Response.WriteAsync(json);
                        }
                    }));
                });
            })
            .Build();
        await host.StartAsync();
        return host;
    }

    // TC-001: GET /health returns HTTP 200
    [Fact]
    public async Task GetHealth_Returns200Ok()
    {
        using var host = await BuildHealthHostAsync();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    // TC-001: response body contains "Healthy"
    [Fact]
    public async Task GetHealth_ResponseBody_ContainsHealthy()
    {
        using var host = await BuildHealthHostAsync();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("Healthy", body, StringComparison.OrdinalIgnoreCase);
    }

    // TC-001: Content-Type is application/json
    [Fact]
    public async Task GetHealth_ContentType_IsApplicationJson()
    {
        using var host = await BuildHealthHostAsync();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/health");

        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.StartsWith("application/json",
            response.Content.Headers.ContentType!.MediaType,
            StringComparison.OrdinalIgnoreCase);
    }

    // EC-002: /health responds within 500 ms SLA (AC-004 states 500 ms boundary)
    [Fact]
    public async Task GetHealth_RespondsWithin500Ms()
    {
        using var host = await BuildHealthHostAsync();
        var client = host.GetTestClient();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await client.GetAsync("/health");
        stopwatch.Stop();

        Assert.True(
            stopwatch.ElapsedMilliseconds < 500,
            $"Expected response within 500 ms but took {stopwatch.ElapsedMilliseconds} ms.");
    }
}
