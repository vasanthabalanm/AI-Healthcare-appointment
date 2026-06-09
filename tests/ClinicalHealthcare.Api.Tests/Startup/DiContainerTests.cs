using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace ClinicalHealthcare.Api.Tests.Startup;

/// <summary>
/// Validates DI container startup validation behaviour (AC-005 — US_002).
/// </summary>
public sealed class DiContainerTests
{
    // Nested classes used only in the circular-dependency test.
    private sealed class CircularA
    {
        public CircularA(CircularB _) { }
    }

    private sealed class CircularB
    {
        public CircularB(CircularA _) { }
    }

    // TC-003: Clean DI registrations build without exception
    [Fact]
    public void ServiceCollection_CleanRegistrations_BuildsWithoutException()
    {
        var services = new ServiceCollection();
        services.AddHealthChecks();
        services.AddLogging();

        var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.NotNull(provider.GetService<HealthCheckService>());
    }

    // EC-001: Circular dependency is surfaced at build time with ValidateOnBuild = true
    [Fact]
    public void ServiceCollection_CircularDependency_ThrowsAtBuild()
    {
        var services = new ServiceCollection();
        services.AddScoped<CircularA>();
        services.AddScoped<CircularB>();

        var ex = Assert.ThrowsAny<Exception>(() =>
            services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateOnBuild = true }));

        var message = (ex.InnerException ?? ex).Message;
        Assert.True(
            message.Contains(nameof(CircularA)) || message.Contains(nameof(CircularB)),
            $"Expected type name in exception message. Got: {message}");
    }
}
