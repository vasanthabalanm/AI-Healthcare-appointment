using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ClinicalHealthcare.Api.Tests.WebApi;

/// <summary>
/// Validates that Swagger UI is gated to the Development environment (AC-002 — US_002).
/// </summary>
public sealed class SwaggerTests
{
    private static async Task<IHost> BuildSwaggerHostAsync(string environment)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseEnvironment(environment);
                web.ConfigureServices((ctx, services) =>
                {
                    services.AddRouting();
                    if (ctx.HostingEnvironment.IsDevelopment())
                    {
                        services.AddEndpointsApiExplorer();
                        services.AddSwaggerGen();
                    }
                });
                web.Configure((ctx, app) =>
                {
                    if (ctx.HostingEnvironment.IsDevelopment())
                    {
                        app.UseSwagger();
                        app.UseSwaggerUI();
                    }
                    app.UseRouting();
                });
            })
            .Build();
        await host.StartAsync();
        return host;
    }

    // TC-002: Swagger UI accessible in Development
    [Fact]
    public async Task SwaggerUI_InDevelopment_Returns200()
    {
        using var host = await BuildSwaggerHostAsync("Development");
        var client = host.GetTestClient();

        var response = await client.GetAsync("/swagger/index.html");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    // TC-005: Swagger UI not served in Production → 404
    [Fact]
    public async Task SwaggerUI_InProduction_Returns404()
    {
        using var host = await BuildSwaggerHostAsync("Production");
        var client = host.GetTestClient();

        var response = await client.GetAsync("/swagger/index.html");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}
