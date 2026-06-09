using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Email;

namespace ClinicalHealthcare.Api.Features.Dev;

/// <summary>
/// Development-only endpoint to retrieve captured emails for E2E testing.
/// Only registered when DevEmailService is active (SMTP_HOST not set).
/// </summary>
public sealed class DevEmailEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // No additional services needed
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        // Only register these endpoints if DevEmailService is in use
        // Check is performed at runtime via service type
        var smtpHost = Environment.GetEnvironmentVariable("SMTP_HOST");
        if (!string.IsNullOrEmpty(smtpHost))
            return; // Production mode — don't expose dev endpoints

        app.MapGet("/dev/email/{email}", HandleGetLastEmail)
           .WithName("GetLastDevEmail")
           .WithSummary("Get the last email sent to an address (dev only)")
           .WithTags("Dev")
           .AllowAnonymous();

        app.MapDelete("/dev/email", HandleClearEmails)
           .WithName("ClearDevEmails")
           .WithSummary("Clear all captured emails (dev only)")
           .WithTags("Dev")
           .AllowAnonymous();
    }

    private static IResult HandleGetLastEmail(string email)
    {
        var record = DevEmailService.GetLastEmail(email);
        
        if (record is null)
            return Results.NotFound(new { error = $"No email found for {email}" });

        return Results.Ok(new
        {
            to = record.To,
            subject = record.Subject,
            actionUrl = record.ActionUrl,
            sentAt = record.SentAt
        });
    }

    private static IResult HandleClearEmails()
    {
        DevEmailService.ClearAll();
        return Results.Ok(new { message = "All dev emails cleared" });
    }
}
