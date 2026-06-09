using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Calendar;
using ClinicalHealthcare.Infrastructure.Configuration;
using ClinicalHealthcare.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicalHealthcare.Api.Features.Appointments;

/// <summary>
/// Vertical-slice endpoint: POST /appointments/{id}/calendar-sync
///
/// Initiates the Google or Microsoft Calendar OAuth2 PKCE flow for an appointment.
/// Supported providers: "google" (TASK_024) and "microsoft" (TASK_025).
///
/// Flow:
///   1. Validate patient owns the appointment.
///   2. Generate PKCE code_verifier (RFC 7636 §4.1) and code_challenge (S256, §4.2).
///   3. Build HMAC-signed state parameter and cache the PKCE session in Redis.
///   4. Return the provider-specific OAuth2 authorization URL for the client to redirect to.
/// </summary>
public sealed class CalendarSyncEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthorization(options =>
        {
            if (options.GetPolicy("PatientOnly") is null)
                options.AddPolicy("PatientOnly", p => p.RequireRole("patient"));
        });

        // Register CalendarSettings from environment variables — never from appsettings.json.
        // Guard: services.Configure<T> registers IConfigureOptions<T>, not IOptions<T> directly.
        if (services.All(d => d.ServiceType != typeof(IConfigureOptions<CalendarSettings>)))
        {
            services.Configure<CalendarSettings>(opts =>
            {
                opts.GoogleClientId        = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID")        ?? string.Empty;
                opts.GoogleClientSecret     = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET")    ?? string.Empty;
                opts.GoogleRedirectUri      = Environment.GetEnvironmentVariable("GOOGLE_REDIRECT_URI")     ?? "http://localhost:5153/auth/google/callback";
                opts.MicrosoftClientId      = Environment.GetEnvironmentVariable("MICROSOFT_CLIENT_ID")     ?? string.Empty;
                opts.MicrosoftClientSecret  = Environment.GetEnvironmentVariable("MICROSOFT_CLIENT_SECRET") ?? string.Empty;
                opts.MicrosoftRedirectUri   = Environment.GetEnvironmentVariable("MICROSOFT_REDIRECT_URI")  ?? "http://localhost:5153/auth/microsoft/callback";
                opts.MicrosoftTenantId      = Environment.GetEnvironmentVariable("MICROSOFT_TENANT_ID")     ?? "common";
                opts.StateSecret            = Environment.GetEnvironmentVariable("CALENDAR_STATE_SECRET")   ?? string.Empty;
                opts.AesKey                 = Environment.GetEnvironmentVariable("CLINICAL_AES_KEY")        ?? string.Empty;
            });
        }

        if (services.All(d => d.ServiceType != typeof(IHttpClientFactory)))
            services.AddHttpClient();

        services.AddScoped<IGoogleCalendarService, GoogleCalendarService>();
        services.AddScoped<IMicrosoftCalendarService, MicrosoftCalendarService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/appointments/{id:int}/calendar-sync", HandleCalendarSync)
           .RequireAuthorization("PatientOnly")
           .WithName("CalendarSync")
           .WithTags("Calendar")
           .Produces<CalendarSyncResponse>(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status403Forbidden)
           .Produces(StatusCodes.Status404NotFound)
           .Produces(StatusCodes.Status503ServiceUnavailable);
    }

    // ── POST /appointments/{id}/calendar-sync ──────────────────────────────────

    public static async Task<IResult> HandleCalendarSync(
        int                          id,
        CalendarSyncRequest          request,
        HttpContext                  httpContext,
        ApplicationDbContext         db,
        ICacheService                cache,
        IOptions<CalendarSettings>   settings,
        CancellationToken            ct)
    {
        // Guard: provider must be "google" or "microsoft".
        var isMicrosoft = string.Equals(request.Provider, "microsoft", StringComparison.OrdinalIgnoreCase);
        var isGoogle    = string.Equals(request.Provider, "google",    StringComparison.OrdinalIgnoreCase);
        if (!isGoogle && !isMicrosoft)
            return Results.BadRequest("Unsupported provider. Use \"google\" or \"microsoft\".");

        // Guard: required env vars must be present for the selected provider.
        var cfg = settings.Value;
        if (isMicrosoft && string.IsNullOrEmpty(cfg.MicrosoftClientId))
            return Results.Problem("Microsoft Calendar integration is not configured.", statusCode: 503);
        if (isGoogle && string.IsNullOrEmpty(cfg.GoogleClientId))
            return Results.Problem("Google Calendar integration is not configured.", statusCode: 503);
        if (string.IsNullOrEmpty(cfg.StateSecret))
            return Results.Problem("Calendar state secret is not configured.", statusCode: 503);

        // Resolve patient from JWT sub claim.
        var subClaim = httpContext.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                    ?? httpContext.User.FindFirst("sub")?.Value;
        if (!int.TryParse(subClaim, out var patientId))
            return Results.Unauthorized();

        // Verify appointment exists and belongs to this patient.
        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (appointment is null)
            return Results.NotFound();
        if (appointment.PatientId != patientId)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        // ── PKCE (RFC 7636) ────────────────────────────────────────────────────
        var codeVerifier  = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);

        // ── Signed state (AC-003) ──────────────────────────────────────────────
        // nonce binds this session to the Redis lookup key.
        var nonce     = Base64UrlEncode(RandomNumberGenerator.GetBytes(16));
        var stateData = $"{id}|{patientId}|{nonce}";
        var stateSig  = ComputeHmac(stateData, cfg.StateSecret);
        var state     = $"{Base64UrlEncode(Encoding.UTF8.GetBytes(stateData))}.{stateSig}";

        // Store code_verifier in Redis keyed by nonce (10-minute PKCE window).
        await cache.SetAsync(
            $"pkce:{nonce}",
            new PkceSession(codeVerifier, id, patientId),
            TimeSpan.FromMinutes(10),
            ct);

        // ── Build OAuth2 authorization URL (provider-specific) ────────────────
        string authUrl;

        if (isMicrosoft)
        {
            // AC-001 / AC-002: Microsoft identity platform; scope includes Calendars.ReadWrite.
            var tenantId = string.IsNullOrEmpty(cfg.MicrosoftTenantId) ? "common" : cfg.MicrosoftTenantId;
            var msQuery = new Dictionary<string, string>
            {
                ["response_type"]         = "code",
                ["client_id"]             = cfg.MicrosoftClientId,
                ["redirect_uri"]          = cfg.MicrosoftRedirectUri,
                ["scope"]                 = "Calendars.ReadWrite offline_access",
                ["code_challenge"]        = codeChallenge,
                ["code_challenge_method"] = "S256",
                ["state"]                 = state,
                ["response_mode"]         = "query",
                ["prompt"]                = "consent"
            };
            authUrl = $"https://login.microsoftonline.com/{Uri.EscapeDataString(tenantId)}/oauth2/v2.0/authorize?" +
                string.Join("&", msQuery.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        }
        else
        {
            var gQuery = new Dictionary<string, string>
            {
                ["response_type"]         = "code",
                ["client_id"]             = cfg.GoogleClientId,
                ["redirect_uri"]          = cfg.GoogleRedirectUri,
                ["scope"]                 = "https://www.googleapis.com/auth/calendar.events",
                ["code_challenge"]        = codeChallenge,
                ["code_challenge_method"] = "S256",
                ["state"]                 = state,
                ["access_type"]           = "offline",
                ["prompt"]                = "consent"
            };
            authUrl = "https://accounts.google.com/o/oauth2/v2/auth?" +
                string.Join("&", gQuery.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        }

        return Results.Ok(new CalendarSyncResponse(authUrl));
    }

    // ── PKCE helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a cryptographically random code_verifier per RFC 7636 §4.1.
    /// Length: 64 characters drawn from a 64-char alphabet (power of 2) to eliminate
    /// modulo bias. Alphabet: [A-Z a-z 0-9 - _] (64 chars exactly).
    /// </summary>
    public static string GenerateCodeVerifier()
    {
        // 64-char alphabet eliminates modulo bias: 256 / 64 = 4 exactly (no remainder).
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var bytes = RandomNumberGenerator.GetBytes(64);
        return new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
    }

    /// <summary>
    /// Computes the S256 code_challenge: BASE64URL(SHA256(ASCII(code_verifier))).
    /// RFC 7636 §4.2 — S256 method.
    /// </summary>
    public static string GenerateCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(hash);
    }

    /// <summary>HMAC-SHA256 of <paramref name="data"/> using the UTF-8 bytes of <paramref name="secret"/>.</summary>
    public static string ComputeHmac(string data, string secret)
    {
        var sig = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(data));
        return Base64UrlEncode(sig);
    }

    /// <summary>URL-safe Base64 encoding with no padding (RFC 4648 §5).</summary>
    public static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Request body for calendar sync initiation.</summary>
public sealed record CalendarSyncRequest(string Provider);

/// <summary>Response containing the Google OAuth2 authorization URL.</summary>
public sealed record CalendarSyncResponse(string AuthUrl);

/// <summary>PKCE session stored in Redis between the sync request and the OAuth callback.</summary>
public sealed record PkceSession(string CodeVerifier, int AppointmentId, int PatientId);
