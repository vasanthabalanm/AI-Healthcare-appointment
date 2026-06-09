using Serilog.Core;
using Serilog.Events;

namespace ClinicalHealthcare.Infrastructure.Logging;

/// <summary>
/// Serilog destructuring policy that replaces known PHI (Protected Health Information)
/// property values with the literal string <c>[REDACTED]</c> before they reach any sink.
///
/// Covered properties (case-insensitive match): Email, DateOfBirth, PhoneNumber,
/// FirstName, LastName, Address, SSN.
/// </summary>
public sealed class PhiRedactingDestructuringPolicy : IDestructuringPolicy
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

    private const string RedactedValue = "[REDACTED]";

    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        out LogEventPropertyValue result)
    {
        // Only intercept complex objects — primitives are handled by Serilog directly.
        if (value is null || value.GetType().IsPrimitive || value is string)
        {
            result = null!;
            return false;
        }

        var properties = value.GetType().GetProperties(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        // If no PHI property names exist on this object, let the default policy handle it.
        bool hasPhiProperty = Array.Exists(properties, p => PhiPropertyNames.Contains(p.Name));
        if (!hasPhiProperty)
        {
            result = null!;
            return false;
        }

        var logProperties = new List<LogEventProperty>(properties.Length);

        foreach (var prop in properties)
        {
            LogEventPropertyValue propValue;

            if (PhiPropertyNames.Contains(prop.Name))
            {
                propValue = new ScalarValue(RedactedValue);
            }
            else
            {
                object? rawValue = null;
                try { rawValue = prop.GetValue(value); }
                catch { /* skip unreadable properties */ }

                propValue = propertyValueFactory.CreatePropertyValue(rawValue, destructureObjects: true);
            }

            logProperties.Add(new LogEventProperty(prop.Name, propValue));
        }

        result = new StructureValue(logProperties);
        return true;
    }
}
