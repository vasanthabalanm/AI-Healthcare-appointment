using Serilog.Core;
using Serilog.Events;

namespace ClinicalHealthcare.Infrastructure.Logging;

/// <summary>
/// Serilog enricher that redacts PHI scalar properties from every log event.
///
/// Works as a complement to <see cref="PhiRedactingDestructuringPolicy"/>:
/// <list type="bullet">
///   <item><see cref="PhiRedactingDestructuringPolicy"/> covers complex objects logged
///         via the <c>{@obj}</c> destructuring operator.</item>
///   <item>This enricher covers scalar PHI values logged directly as named message-template
///         properties — e.g. <c>Log.Information("{Email}", value)</c>.</item>
/// </list>
/// Both must be registered for full PHI coverage.
/// </summary>
public sealed class PhiRedactingEnricher : ILogEventEnricher
{
    private static readonly HashSet<string> PhiPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Email",
        "DateOfBirth",
        "PhoneNumber",
        "FirstName",
        "LastName",
        "Address",
        "SSN"
    };

    private static readonly ScalarValue RedactedScalar = new("[REDACTED]");

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var name in PhiPropertyNames)
        {
            if (logEvent.Properties.ContainsKey(name))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, RedactedScalar));
            }
        }
    }
}
