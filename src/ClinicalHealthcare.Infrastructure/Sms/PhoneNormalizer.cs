using PhoneNumbers;

namespace ClinicalHealthcare.Infrastructure.Sms;

/// <summary>
/// Normalizes raw phone-number strings to E.164 format using libphonenumber-csharp (AC-002).
///
/// Returns null for any input that cannot be parsed or is not a valid phone number,
/// allowing callers to skip SMS gracefully rather than crashing (AC-003).
/// </summary>
public static class PhoneNormalizer
{
    private static readonly PhoneNumberUtil _util = PhoneNumberUtil.GetInstance();

    /// <summary>
    /// Attempts to parse and normalize <paramref name="rawNumber"/> to E.164 format.
    /// </summary>
    /// <param name="rawNumber">Raw phone number string (any format).</param>
    /// <param name="defaultRegion">
    /// ISO 3166-1 alpha-2 region code used as a hint when the number has no country prefix.
    /// Defaults to "US". For international deployments configure via <c>AppSettings.SmsDefaultRegion</c>
    /// or store numbers in E.164 format (with leading '+') to make this hint irrelevant.
    /// Pass null or empty to require an explicit country code.
    /// </param>
    /// <returns>
    /// E.164-formatted number (e.g. "+14155552671"), or null if the input is invalid.
    /// </returns>
    public static string? ToE164(string? rawNumber, string defaultRegion = "US")
    {
        if (string.IsNullOrWhiteSpace(rawNumber))
            return null;

        try
        {
            var parsed = _util.Parse(rawNumber, defaultRegion);
            if (!_util.IsValidNumber(parsed))
                return null;

            return _util.Format(parsed, PhoneNumberFormat.E164);
        }
        catch (NumberParseException)
        {
            return null;
        }
    }
}
