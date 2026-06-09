using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ClinicalHealthcare.Infrastructure.Entities;

namespace ClinicalHealthcare.Infrastructure.Calendar;

/// <summary>
/// Production implementation of <see cref="IMicrosoftCalendarService"/>.
/// Uses <c>IHttpClientFactory</c> for both the Entra ID token endpoint and the Microsoft Graph API.
/// </summary>
public sealed class MicrosoftCalendarService : IMicrosoftCalendarService
{
    private readonly IHttpClientFactory _httpFactory;

    public MicrosoftCalendarService(IHttpClientFactory httpFactory)
        => _httpFactory = httpFactory;

    // ── Token Exchange ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<TokenExchangeResult> ExchangeCodeAsync(
        string            code,
        string            codeVerifier,
        string            clientId,
        string            clientSecret,
        string            redirectUri,
        string            tenantId,
        CancellationToken ct)
        => PostTokenAsync(new Dictionary<string, string>
        {
            ["code"]          = code,
            ["client_id"]     = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"]  = redirectUri,
            ["code_verifier"] = codeVerifier,
            ["grant_type"]    = "authorization_code",
            ["scope"]         = "Calendars.ReadWrite offline_access"
        }, tenantId, ct);

    /// <inheritdoc/>
    public Task<TokenExchangeResult> RefreshTokenAsync(
        string            refreshToken,
        string            clientId,
        string            clientSecret,
        string            tenantId,
        CancellationToken ct)
        => PostTokenAsync(new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["client_id"]     = clientId,
            ["client_secret"] = clientSecret,
            ["grant_type"]    = "refresh_token",
            ["scope"]         = "Calendars.ReadWrite offline_access"
        }, tenantId, ct);

    private async Task<TokenExchangeResult> PostTokenAsync(
        Dictionary<string, string> form,
        string                     tenantId,
        CancellationToken          ct)
    {
        var client = _httpFactory.CreateClient("MicrosoftOAuth");

        var response = await client.PostAsync(
            $"https://login.microsoftonline.com/{Uri.EscapeDataString(tenantId)}/oauth2/v2.0/token",
            new FormUrlEncodedContent(form), ct);

        response.EnsureSuccessStatusCode();

        var tokenResponse = await response.Content.ReadFromJsonAsync<MicrosoftTokenResponse>(ct)
            ?? throw new InvalidOperationException("Microsoft token endpoint returned empty response.");

        var expiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);
        return new TokenExchangeResult(tokenResponse.AccessToken, tokenResponse.RefreshToken, expiresAt);
    }

    // ── Calendar Event ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<string> CreateEventAsync(
        string            accessToken,
        Appointment       appointment,
        CancellationToken ct)
    {
        var slot = appointment.Slot
            ?? throw new InvalidOperationException("Appointment.Slot navigation property is not loaded.");

        var slotEnd = slot.SlotTime.AddMinutes(slot.DurationMinutes);

        var client = _httpFactory.CreateClient("MicrosoftGraph");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        // Graph event payload — ISO 8601 UTC datetimes.
        var eventBody = new
        {
            subject             = "Medical Appointment",
            start               = new { dateTime = slot.SlotTime.ToString("o"), timeZone = "UTC" },
            end                 = new { dateTime = slotEnd.ToString("o"),       timeZone = "UTC" },
            isReminderOn        = true,
            reminderMinutesBeforeStart = 30
        };

        var response = await client.PostAsJsonAsync(
            "https://graph.microsoft.com/v1.0/me/events", eventBody, ct);

        // AC-003 edge case: Graph 409 means the event already exists; treat as success.
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // The conflict response body is an error object (not the event), so attempt to
            // return an empty string sentinel — the caller must handle the empty ID.
            throw new MicrosoftGraphConflictException();
        }

        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<MicrosoftEventResponse>(ct)
            ?? throw new InvalidOperationException("Microsoft Graph returned empty response.");

        return created.Id;
    }

    // ── Internal response DTOs ────────────────────────────────────────────────

    private sealed class MicrosoftTokenResponse
    {
        [JsonPropertyName("access_token")]  public string  AccessToken  { get; set; } = string.Empty;
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")]    public int     ExpiresIn    { get; set; }
    }

    private sealed class MicrosoftEventResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    }
}

/// <summary>
/// Thrown by <see cref="MicrosoftCalendarService.CreateEventAsync"/> when Microsoft Graph
/// returns HTTP 409 Conflict — event already exists. The caller should treat this as success.
/// </summary>
public sealed class MicrosoftGraphConflictException : Exception
{
    public MicrosoftGraphConflictException()
        : base("Microsoft Graph returned 409 Conflict — event already exists.") { }
}
