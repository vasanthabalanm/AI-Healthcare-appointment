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
/// Vertical-slice endpoint: GET /auth/microsoft/callback
///
/// Handles the Microsoft Entra ID OAuth2 callback after user consent (AC-001/AC-003/AC-004).
///
/// Flow:
///   1. Validate HMAC-signed state — prevents CSRF (same mechanism as Google callback).
///   2. Retrieve PKCE code_verifier from Redis via nonce embedded in state.
///   3. Exchange authorization code for tokens using code_verifier.
///   4. Encrypt tokens with AES-256-CBC (same key as TASK_024) and upsert CalendarToken.
///   5. Idempotency check (AC-004) — if CalendarEventId already exists, skip event creation.
///   6. Create Microsoft Outlook Calendar event via Graph API (AC-003).
///   7. On 401 from Graph — attempt token refresh; if refresh fails, clear tokens and return 401.
///   8. On 409 from Graph — treat as success (event already created via another path).
/// </summary>
public sealed class MicrosoftCalendarCallbackEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        // CalendarSettings and IMicrosoftCalendarService are registered by CalendarSyncEndpoint.
        // No additional services needed here.
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        // AllowAnonymous — called by Microsoft's redirect, not by the SPA.
        // CSRF is prevented by HMAC-signed state rather than JWT.
        app.MapGet("/auth/microsoft/callback", HandleMicrosoftCallback)
           .AllowAnonymous()
           .WithName("MicrosoftCalendarCallback")
           .WithTags("Calendar")
           .Produces<MicrosoftCallbackResponse>(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status400BadRequest)
           .Produces(StatusCodes.Status401Unauthorized)
           .Produces(StatusCodes.Status503ServiceUnavailable);
    }

    // ── GET /auth/microsoft/callback ──────────────────────────────────────────

    public static async Task<IResult> HandleMicrosoftCallback(
        string                               code,
        string                               state,
        ApplicationDbContext                 db,
        ICacheService                        cache,
        [FromServices] IMicrosoftCalendarService calendarSvc,
        IOptions<CalendarSettings>           settings,
        CancellationToken                    ct)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return Results.BadRequest("Missing code or state parameter.");

        var cfg = settings.Value;
        if (string.IsNullOrEmpty(cfg.MicrosoftClientId) || string.IsNullOrEmpty(cfg.AesKey))
            return Results.Problem("Microsoft Calendar integration is not configured.", statusCode: 503);

        // ── 1. Verify HMAC state ───────────────────────────────────────────────
        var dotIdx = state.LastIndexOf('.');
        if (dotIdx < 0)
            return Results.BadRequest("Invalid state format.");

        var encodedData = state[..dotIdx];
        var receivedSig = state[(dotIdx + 1)..];

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

        // One-time use — delete immediately.
        await cache.DeleteAsync($"pkce:{nonce}", ct);

        // Defence in depth: state must match the cached session.
        if (session.AppointmentId != appointmentId || session.PatientId != patientId)
            return Results.BadRequest("State/session mismatch.");

        // ── 4. Exchange code for tokens ────────────────────────────────────────
        var tenantId = string.IsNullOrEmpty(cfg.MicrosoftTenantId) ? "common" : cfg.MicrosoftTenantId;

        TokenExchangeResult tokens;
        try
        {
            tokens = await calendarSvc.ExchangeCodeAsync(
                code, session.CodeVerifier,
                cfg.MicrosoftClientId, cfg.MicrosoftClientSecret,
                cfg.MicrosoftRedirectUri, tenantId, ct);
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem(
                $"Token exchange failed: {ex.Message}",
                statusCode: StatusCodes.Status502BadGateway);
        }

        // ── 5. Encrypt tokens (AC-004) and upsert CalendarToken ───────────────
        var aesKey           = AesCbc.DeriveKey(cfg.AesKey);
        var encryptedAccess  = AesCbc.Encrypt(tokens.AccessToken, aesKey);
        var encryptedRefresh = tokens.RefreshToken is not null
            ? AesCbc.Encrypt(tokens.RefreshToken, aesKey)
            : null;

        var existing = await db.CalendarTokens
            .FirstOrDefaultAsync(c => c.AppointmentId == appointmentId && c.Provider == "Microsoft", ct);

        var existingEventId = existing?.CalendarEventId;

        if (existing is null)
        {
            existing = new CalendarToken
            {
                PatientId     = patientId,
                AppointmentId = appointmentId,
                Provider      = "Microsoft"
            };
            db.CalendarTokens.Add(existing);
        }

        existing.EncryptedAccessToken  = encryptedAccess;
        existing.EncryptedRefreshToken = encryptedRefresh;
        existing.ExpiresAt             = tokens.ExpiresAtUtc;

        await db.SaveChangesAsync(ct);

        // ── 6. Idempotency: skip event creation if already done (AC-004) ──────
        // Guard on `is not null` — a sentinel value ("CONFLICT") also satisfies idempotency.
        if (existingEventId is not null)
            return Results.Ok(new MicrosoftCallbackResponse(existingEventId, Idempotent: true));

        // ── 7. Load appointment + slot, create Microsoft Calendar event ────────
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
        catch (MicrosoftGraphConflictException)
        {
            // AC-003 edge case: Graph 409 — event already exists via another path.
            // Persist a sentinel so the idempotency guard fires on all future re-syncs.
            // The real Graph event ID cannot be recovered from a 409 response body.
            existing.CalendarEventId = "CONFLICT";
            await db.SaveChangesAsync(ct);
            return Results.Ok(new MicrosoftCallbackResponse("CONFLICT", Idempotent: true));
        }
        catch (HttpRequestException ex) when (
            ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            // Token refresh path: access token was rejected by Graph — attempt refresh.
            TokenExchangeResult refreshed;
            try
            {
                // EncryptedRefreshToken is guaranteed non-null here — checked above.
                var storedRefresh = AesCbc.Decrypt(existing.EncryptedRefreshToken!, aesKey);
                refreshed = await calendarSvc.RefreshTokenAsync(
                    storedRefresh,
                    cfg.MicrosoftClientId, cfg.MicrosoftClientSecret, tenantId, ct);
            }
            catch
            {
                // Refresh failed — clear stored tokens and prompt re-auth.
                existing.EncryptedAccessToken  = string.Empty;
                existing.EncryptedRefreshToken = null;
                await db.SaveChangesAsync(ct);
                return Results.Unauthorized();
            }

            // Re-encrypt and persist the refreshed tokens.
            existing.EncryptedAccessToken  = AesCbc.Encrypt(refreshed.AccessToken, aesKey);
            existing.EncryptedRefreshToken = refreshed.RefreshToken is not null
                ? AesCbc.Encrypt(refreshed.RefreshToken, aesKey)
                : null;
            existing.ExpiresAt = refreshed.ExpiresAtUtc;

            // Retry event creation with the new access token.
            try
            {
                eventId = await calendarSvc.CreateEventAsync(refreshed.AccessToken, appointment, ct);
            }
            catch (HttpRequestException retryEx)
            {
                await db.SaveChangesAsync(ct);
                return Results.Problem(
                    $"Failed to create Microsoft Calendar event after token refresh: {retryEx.Message}",
                    statusCode: StatusCodes.Status502BadGateway);
            }
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem(
                $"Failed to create Microsoft Calendar event: {ex.Message}",
                statusCode: StatusCodes.Status502BadGateway);
        }

        // Persist the event ID for idempotency on future re-sync.
        existing.CalendarEventId = eventId;
        await db.SaveChangesAsync(ct);

        return Results.Ok(new MicrosoftCallbackResponse(eventId, Idempotent: false));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>URL-safe Base64 decode (RFC 4648 §5).</summary>
    internal static byte[] Base64UrlDecode(string input)
    {
        var base64  = input.Replace('-', '+').Replace('_', '/');
        var padding = (4 - base64.Length % 4) % 4;
        base64      = base64.PadRight(base64.Length + padding, '=');
        return Convert.FromBase64String(base64);
    }
}

/// <summary>Response returned after a successful Microsoft Calendar OAuth2 callback.</summary>
public sealed record MicrosoftCallbackResponse(string CalendarEventId, bool Idempotent);
