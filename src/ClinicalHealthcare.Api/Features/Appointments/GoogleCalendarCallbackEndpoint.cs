using System.Security.Cryptography;
using System.Text;
using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Calendar;
using ClinicalHealthcare.Infrastructure.Configuration;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClinicalHealthcare.Api.Features.Appointments;

/// <summary>
/// Vertical-slice endpoint: GET /auth/google/callback
///
/// Handles the Google OAuth2 callback after user consent (AC-001/AC-003/AC-004).
///
/// Flow:
///   1. Validate HMAC-signed state (AC-003) — prevents CSRF.
///   2. Retrieve PKCE code_verifier from Redis via nonce embedded in state.
///   3. Exchange authorization code for tokens using code_verifier.
///   4. Encrypt tokens with AES-256-CBC (AC-004) and upsert CalendarToken.
///   5. Idempotency check — if CalendarEventId already exists, skip event creation.
///   6. Create Google Calendar event and persist the CalendarEventId.
/// </summary>
public sealed class GoogleCalendarCallbackEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // CalendarSettings and IGoogleCalendarService are registered by CalendarSyncEndpoint.
        // No additional services needed here.
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        // AllowAnonymous — this endpoint is called by Google's redirect, not by the SPA.
        // CSRF is prevented by HMAC-signed state (AC-003) rather than JWT.
        app.MapGet("/auth/google/callback", HandleGoogleCallback)
           .AllowAnonymous()
           .WithName("GoogleCalendarCallback")
           .WithTags("Calendar")
           .Produces<CalendarCallbackResponse>(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status503ServiceUnavailable);
    }

    // ── GET /auth/google/callback ──────────────────────────────────────────────

    public static async Task<IResult> HandleGoogleCallback(
        string                       code,
        string                       state,
        ApplicationDbContext         db,
        ICacheService                cache,
        [FromServices] IGoogleCalendarService       calendarSvc,
        IOptions<CalendarSettings>   settings,
        CancellationToken            ct)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return Results.BadRequest("Missing code or state parameter.");

        var cfg = settings.Value;
        if (string.IsNullOrEmpty(cfg.GoogleClientId) || string.IsNullOrEmpty(cfg.AesKey))
            return Results.Problem("Google Calendar integration is not configured.", statusCode: 503);

        // ── 1. Verify HMAC state (AC-003) ─────────────────────────────────────
        var dotIdx = state.LastIndexOf('.');
        if (dotIdx < 0)
            return Results.BadRequest("Invalid state format.");

        var encodedData   = state[..dotIdx];
        var receivedSig   = state[(dotIdx + 1)..];

        string stateData;
        try { stateData = Encoding.UTF8.GetString(Base64UrlDecode(encodedData)); }
        catch { return Results.BadRequest("Invalid state encoding."); }

        var expectedSig = CalendarSyncEndpoint.ComputeHmac(stateData, cfg.StateSecret);

        // Constant-time comparison prevents timing attacks.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedSig),
                Encoding.UTF8.GetBytes(receivedSig)))
            return Results.BadRequest("Invalid state signature.");

        // ── 2. Parse state payload ─────────────────────────────────────────────
        var parts = stateData.Split('|');
        if (parts.Length != 3
            || !int.TryParse(parts[0], out var appointmentId)
            || !int.TryParse(parts[1], out var patientId))
            return Results.BadRequest("Malformed state payload.");

        var nonce = parts[2];

        // ── 3. Retrieve PKCE code_verifier from Redis ──────────────────────────
        var session = await cache.GetAsync<PkceSession>($"pkce:{nonce}", ct);
        if (session is null)
            return Results.BadRequest("PKCE session expired or not found. Restart the calendar sync flow.");

        // Clean up the PKCE entry immediately — one-time use.
        await cache.DeleteAsync($"pkce:{nonce}", ct);

        // Sanity: ensure state matches the session (defence in depth).
        if (session.AppointmentId != appointmentId || session.PatientId != patientId)
            return Results.BadRequest("State/session mismatch.");

        // ── 4. Exchange code for tokens ────────────────────────────────────────
        TokenExchangeResult tokens;
        try
        {
            tokens = await calendarSvc.ExchangeCodeAsync(
                code, session.CodeVerifier,
                cfg.GoogleClientId, cfg.GoogleClientSecret,
                cfg.GoogleRedirectUri, ct);
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem(
                $"Token exchange failed: {ex.Message}",
                statusCode: StatusCodes.Status502BadGateway);
        }

        // ── 5. Encrypt tokens (AC-004) and upsert CalendarToken ───────────────
        var aesKey = AesCbc.DeriveKey(cfg.AesKey);
        var encryptedAccess  = AesCbc.Encrypt(tokens.AccessToken,  aesKey);
        var encryptedRefresh = tokens.RefreshToken is not null
            ? AesCbc.Encrypt(tokens.RefreshToken, aesKey)
            : null;

        var existing = await db.CalendarTokens
            .FirstOrDefaultAsync(c => c.AppointmentId == appointmentId && c.Provider == "Google", ct);

        string? existingEventId = existing?.CalendarEventId;

        if (existing is null)
        {
            existing = new CalendarToken
            {
                PatientId     = patientId,
                AppointmentId = appointmentId,
                Provider      = "Google",
            };
            db.CalendarTokens.Add(existing);
        }

        existing.EncryptedAccessToken  = encryptedAccess;
        existing.EncryptedRefreshToken = encryptedRefresh;
        existing.ExpiresAt             = tokens.ExpiresAtUtc;

        await db.SaveChangesAsync(ct);

        // ── 6. Idempotency: skip event creation if already done ────────────────
        if (!string.IsNullOrEmpty(existingEventId))
            return Results.Ok(new CalendarCallbackResponse(existingEventId, Idempotent: true));

        // ── 7. Load appointment + slot, create Google Calendar event ──────────
        var appointment = await db.Appointments
            .Include(a => a.Slot)
            .FirstOrDefaultAsync(a => a.Id == appointmentId, ct);

        if (appointment is null)
            return Results.NotFound();

        string eventId;
        try
        {
            eventId = await calendarSvc.CreateEventAsync(tokens.AccessToken, appointment, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or Google.GoogleApiException)
        {
            return Results.Problem(
                $"Failed to create Google Calendar event: {ex.Message}",
                statusCode: StatusCodes.Status502BadGateway);
        }

        // Persist the event ID for idempotency on future re-sync.
        existing.CalendarEventId = eventId;
        await db.SaveChangesAsync(ct);

        return Results.Ok(new CalendarCallbackResponse(eventId, Idempotent: false));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>URL-safe Base64 decode (RFC 4648 §5 — reverses <see cref="CalendarSyncEndpoint.Base64UrlEncode"/>).</summary>
    internal static byte[] Base64UrlDecode(string input)
    {
        var base64 = input.Replace('-', '+').Replace('_', '/');
        var padding = (4 - base64.Length % 4) % 4;
        base64 = base64.PadRight(base64.Length + padding, '=');
        return Convert.FromBase64String(base64);
    }
}

/// <summary>Response returned after a successful OAuth2 callback.</summary>
public sealed record CalendarCallbackResponse(string CalendarEventId, bool Idempotent);
