using System.Reflection;

namespace ClinicalHealthcare.Api.Abstractions;

/// <summary>
/// Extension methods that discover and register all <see cref="IEndpointDefinition"/>
/// implementations found in the given assembly, enabling zero-touch vertical-slice wiring.
/// </summary>
public static class EndpointDefinitionExtensions
{
    /// <summary>
    /// Scans <paramref name="assembly"/> for concrete <see cref="IEndpointDefinition"/>
    /// types, registers each with DI (transient), and calls <c>AddServices</c> on each.
    /// </summary>
    public static IServiceCollection AddEndpointDefinitions(
        this IServiceCollection services,
        Assembly assembly,
        IConfiguration configuration)
    {
        var definitions = GetDefinitions(assembly);

        foreach (var definition in definitions)
        {
            services.AddTransient(definition.GetType());
            definition.AddServices(services, configuration);
        }

        services.AddSingleton(definitions);
        return services;
    }

    /// <summary>
    /// Calls <c>MapEndpoints</c> on every registered <see cref="IEndpointDefinition"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapEndpointDefinitions(
        this IEndpointRouteBuilder app)
    {
        var definitions = app.ServiceProvider
            .GetService<IEndpointDefinition[]>();

        if (definitions is null) { return app; }

        foreach (var definition in definitions)
        {
            definition.MapEndpoints(app);
        }

        return app;
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private static IEndpointDefinition[] GetDefinitions(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => typeof(IEndpointDefinition).IsAssignableFrom(t)
                        && t is { IsAbstract: false, IsInterface: false })
            .Select(t => (IEndpointDefinition)Activator.CreateInstance(t)!)
            .ToArray();

}
