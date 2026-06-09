using ClinicalHealthcare.Infrastructure.Entities;

namespace ClinicalHealthcare.Infrastructure.Calendar;

/// <summary>
/// Abstraction over the Google Calendar API and OAuth2 token exchange.
/// Keeping both operations in one interface allows a single mock in tests.
/// </summary>
public interface IGoogleCalendarService
{
    /// <summary>
    /// Exchanges an authorization code for OAuth2 tokens using PKCE.
    /// </summary>
    /// <returns>Access token, optional refresh token, and expiry UTC.</returns>
    Task<TokenExchangeResult> ExchangeCodeAsync(
        string            code,
        string            codeVerifier,
        string            clientId,
        string            clientSecret,
        string            redirectUri,
        CancellationToken ct);

    /// <summary>
    /// Creates a Google Calendar event for <paramref name="appointment"/>.
    /// </summary>
    /// <returns>The created Google Calendar event ID.</returns>
    Task<string> CreateEventAsync(
        string            accessToken,
        Appointment       appointment,
        CancellationToken ct);
}

/// <summary>Result of a successful OAuth2 authorization code exchange.</summary>
public sealed record TokenExchangeResult(
    string  AccessToken,
    string? RefreshToken,
    DateTime ExpiresAtUtc);
