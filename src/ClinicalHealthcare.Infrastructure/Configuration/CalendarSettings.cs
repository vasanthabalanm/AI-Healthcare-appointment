namespace ClinicalHealthcare.Infrastructure.Configuration;

/// <summary>
/// Configuration for Google and Microsoft Calendar OAuth2 PKCE sync (TASK_024 / TASK_025).
/// All sensitive values are sourced from environment variables — never from appsettings.json.
/// </summary>
public sealed class CalendarSettings
{
    public const string SectionName = "CalendarSettings";

    // ── Google ────────────────────────────────────────────────────────────────

    /// <summary>Google OAuth2 client ID (env: GOOGLE_CLIENT_ID).</summary>
    public string GoogleClientId { get; set; } = string.Empty;

    /// <summary>Google OAuth2 client secret (env: GOOGLE_CLIENT_SECRET).</summary>
    public string GoogleClientSecret { get; set; } = string.Empty;

    /// <summary>Registered OAuth2 redirect URI (env: GOOGLE_REDIRECT_URI).</summary>
    public string GoogleRedirectUri { get; set; } = "http://localhost:5153/auth/google/callback";

    // ── Microsoft (Entra ID) ──────────────────────────────────────────────────

    /// <summary>Microsoft OAuth2 (Entra ID) client ID (env: MICROSOFT_CLIENT_ID).</summary>
    public string MicrosoftClientId { get; set; } = string.Empty;

    /// <summary>Microsoft OAuth2 client secret (env: MICROSOFT_CLIENT_SECRET).</summary>
    public string MicrosoftClientSecret { get; set; } = string.Empty;

    /// <summary>Registered Microsoft OAuth2 redirect URI (env: MICROSOFT_REDIRECT_URI).</summary>
    public string MicrosoftRedirectUri { get; set; } = "http://localhost:5153/auth/microsoft/callback";

    /// <summary>
    /// Azure AD tenant ID or "common" for multi-tenant personal + org accounts
    /// (env: MICROSOFT_TENANT_ID). Defaults to "common".
    /// </summary>
    public string MicrosoftTenantId { get; set; } = "common";

    // ── Shared ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Secret used to HMAC-sign the OAuth2 state parameter (env: CALENDAR_STATE_SECRET).
    /// Must be at least 32 characters.
    /// </summary>
    public string StateSecret { get; set; } = string.Empty;

    /// <summary>
    /// Key material for AES-256-CBC token encryption (env: CLINICAL_AES_KEY).
    /// A 32-byte key is derived from this value via SHA-256 at runtime.
    /// </summary>
    public string AesKey { get; set; } = string.Empty;
}
