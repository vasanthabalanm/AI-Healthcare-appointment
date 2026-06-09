using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ClinicalHealthcare.Infrastructure.Entities;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;

namespace ClinicalHealthcare.Infrastructure.Calendar;

/// <summary>
/// Production implementation of <see cref="IGoogleCalendarService"/>.
/// Uses <c>HttpClient</c> for the token exchange and the Google Calendar v3 SDK
/// for event creation.
/// </summary>
public sealed class GoogleCalendarService : IGoogleCalendarService
{
    private readonly IHttpClientFactory _httpFactory;

    public GoogleCalendarService(IHttpClientFactory httpFactory)
        => _httpFactory = httpFactory;

    // ── Token Exchange ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<TokenExchangeResult> ExchangeCodeAsync(
        string            code,
        string            codeVerifier,
        string            clientId,
        string            clientSecret,
        string            redirectUri,
        CancellationToken ct)
    {
        using var client = _httpFactory.CreateClient("GoogleOAuth");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"]          = code,
            ["client_id"]     = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"]  = redirectUri,
            ["code_verifier"] = codeVerifier,
            ["grant_type"]    = "authorization_code"
        });

        var response = await client.PostAsync("https://oauth2.googleapis.com/token", form, ct);
        response.EnsureSuccessStatusCode();

        var tokenResponse = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(ct)
            ?? throw new InvalidOperationException("Google token endpoint returned empty response.");

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
        var credential = GoogleCredential.FromAccessToken(accessToken);
        using var service = new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName       = "ClinicalHealthcare"
        });

        var slot     = appointment.Slot
            ?? throw new InvalidOperationException("Appointment.Slot navigation property is not loaded.");
        var slotEnd  = slot.SlotTime.AddMinutes(slot.DurationMinutes);

        var calEvent = new Event
        {
            Summary = "Medical Appointment",
            Start   = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(slot.SlotTime, TimeSpan.Zero) },
            End     = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(slotEnd,       TimeSpan.Zero) },
            Reminders = new Event.RemindersData
            {
                UseDefault = false,
                Overrides  =
                [
                    new EventReminder { Method = "popup", Minutes = 30 }
                ]
            }
        };

        var created = await service.Events.Insert(calEvent, "primary").ExecuteAsync(ct);
        return created.Id;
    }

    // ── Internal response DTO ─────────────────────────────────────────────────

    private sealed class GoogleTokenResponse
    {
        [JsonPropertyName("access_token")]  public string  AccessToken  { get; set; } = string.Empty;
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")]    public int     ExpiresIn    { get; set; }
    }
}
