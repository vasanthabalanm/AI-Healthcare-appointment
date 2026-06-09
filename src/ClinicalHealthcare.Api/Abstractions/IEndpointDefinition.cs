namespace ClinicalHealthcare.Api.Abstractions;

/// <summary>
/// Marker interface for vertical-slice endpoint definitions.
/// Each feature slice implements this to self-register its routes and DI services.
/// </summary>
public interface IEndpointDefinition
{
    /// <summary>Registers any DI services required by this slice.</summary>
    void AddServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Maps HTTP endpoints for this slice onto the application's route table.</summary>
    void MapEndpoints(IEndpointRouteBuilder app);
}
