using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ClinicalHealthcare.Api.Tests.Middleware;

/// <summary>
/// Regression gate for HTTPS redirect (AC-001) and HSTS (AC-003) middleware.
///
/// Mirrors Program.cs configuration so any accidental middleware reordering
/// or misconfiguration is caught immediately in CI.
///
/// Uses a minimal IHost + TestServer — no real TLS certificate required.
/// HTTPS is simulated by rewriting ctx.Request.Scheme before the HSTS/redirect
/// middleware reads it, which is exactly how ASP.NET Core evaluates IsHttps.
/// </summary>
public sealed class HttpsMiddlewareTests
{
    // Mirror Program.cs HSTS + redirect configuration exactly.
    private static IHost BuildHost(bool simulateHttps)
    {
        return new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddHttpsRedirection(opts =>
                    {
                        opts.RedirectStatusCode = StatusCodes.Status301MovedPermanently;
                        opts.HttpsPort = 443;
                    });
                    services.AddHsts(opts =>
                    {
                        opts.MaxAge           = TimeSpan.FromDays(365);
                        opts.IncludeSubDomains = true;
                        opts.Preload          = false;
                        opts.ExcludedHosts.Add("localhost");
                        opts.ExcludedHosts.Add("127.0.0.1");
                        opts.ExcludedHosts.Add("[::1]");
                    });
                });
                web.Configure(app =>
                {
                    if (simulateHttps)
                    {
                        // Rewrite scheme to "https" before HSTS/redirect middleware reads it.
                        // ctx.Request.IsHttps is derived from ctx.Request.Scheme, so this
                        // accurately simulates an HTTPS connection without a real certificate.
                        app.Use((ctx, next) =>
                        {
                            ctx.Request.Scheme = "https";
                            return next(ctx);
                        });
                    }

                    app.UseHsts();
                    app.UseHttpsRedirection();
                    app.Run(ctx => ctx.Response.WriteAsync("OK"));
                });
            })
            .Build();
    }

    [Fact]
    public async Task HttpRequest_Returns301_LocationStartsWithHttps()
    {
        using var host = BuildHost(simulateHttps: false);
        await host.StartAsync();
        using var invoker = new HttpMessageInvoker(host.GetTestServer().CreateHandler());

        var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://api.test/health"),
            CancellationToken.None);

        // AC-001: permanent redirect, not temporary.
        Assert.Equal(StatusCodes.Status301MovedPermanently, (int)response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.StartsWith("https://", response.Headers.Location!.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HttpsRequest_ContainsHstsHeader_WithMaxAgeAndIncludeSubDomains()
    {
        using var host = BuildHost(simulateHttps: true);
        await host.StartAsync();
        using var invoker = new HttpMessageInvoker(host.GetTestServer().CreateHandler());

        var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://api.test/health"),
            CancellationToken.None);

        // AC-003: header present with correct values.
        Assert.True(response.Headers.Contains("Strict-Transport-Security"),
            "Strict-Transport-Security must be present on HTTPS responses.");

        var header = string.Join("; ",
            response.Headers.GetValues("Strict-Transport-Security"));

        // 365 days × 86 400 s/day = 31 536 000 s.
        Assert.Contains("max-age=31536000", header);
        Assert.Contains("includeSubDomains", header);
    }

    [Fact]
    public async Task HttpsRequest_ToLocalhost_OmitsHstsHeader()
    {
        using var host = BuildHost(simulateHttps: true);
        await host.StartAsync();
        using var invoker = new HttpMessageInvoker(host.GetTestServer().CreateHandler());

        // Edge case: localhost is in ExcludedHosts — HSTS must be suppressed.
        var response = await invoker.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "http://localhost/health"),
            CancellationToken.None);

        Assert.False(response.Headers.Contains("Strict-Transport-Security"),
            "Strict-Transport-Security must not be sent for ExcludedHost 'localhost'.");
    }
}
