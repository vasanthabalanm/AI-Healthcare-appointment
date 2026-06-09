using ClinicalHealthcare.Infrastructure.Entities;

namespace ClinicalHealthcare.Infrastructure.Calendar;

/// <summary>
/// Abstraction over the Microsoft Graph Calendar API and Entra ID OAuth2 token operations.
/// Follows the same interface shape as <see cref="IGoogleCalendarService"/> for consistency.
/// Adds <see cref="RefreshTokenAsync"/> to support access-token renewal without a new auth flow.
/// </summary>
public interface IMicrosoftCalendarService
{
    /// <summary>
    /// Exchanges an authorization code for OAuth2 tokens using PKCE (RFC 7636).
    /// Calls <c>https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token</c>.
    /// </summary>
    /// <returns>Access token, refresh token, and expiry UTC.</returns>
    Task<TokenExchangeResult> ExchangeCodeAsync(
        string            code,
        string            codeVerifier,
        string            clientId,
        string            clientSecret,
        string            redirectUri,
        string            tenantId,
        CancellationToken ct);

    /// <summary>
    /// Exchanges a refresh token for a new access token.
    /// Calls <c>https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token</c>
    /// with <c>grant_type=refresh_token</c>.
    /// </summary>
    /// <exception cref="HttpRequestException">Thrown when the refresh request fails (401/400).</exception>
    Task<TokenExchangeResult> RefreshTokenAsync(
        string            refreshToken,
        string            clientId,
        string            clientSecret,
        string            tenantId,
        CancellationToken ct);

    /// <summary>
    /// Creates a Microsoft Outlook Calendar event for <paramref name="appointment"/>
    /// via <c>POST https://graph.microsoft.com/v1.0/me/events</c> (AC-003).
    /// </summary>
    /// <returns>The Microsoft Graph event ID of the created event.</returns>
    /// <exception cref="HttpRequestException">Thrown on non-success Graph API responses.</exception>
    Task<string> CreateEventAsync(
        string            accessToken,
        Appointment       appointment,
        CancellationToken ct);
}
