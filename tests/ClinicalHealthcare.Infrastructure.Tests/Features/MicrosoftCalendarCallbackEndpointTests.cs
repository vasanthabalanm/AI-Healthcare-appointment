using System.Net;
using System.Text;
using ClinicalHealthcare.Api.Features.Appointments;
using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Calendar;
using ClinicalHealthcare.Infrastructure.Configuration;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for TASK_025: GET /auth/microsoft/callback
/// and the microsoft provider branch in POST /appointments/{id}/calendar-sync.
///
/// Covers:
///   AC-001 — microsoft provider returns 200 with Microsoft OAuth2 authorization URL.
///   AC-002 — Calendars.ReadWrite scope present in the auth URL.
///   AC-003 — Event created via Microsoft Graph (CreateEventAsync called).
///   AC-004 — Idempotency: skip event creation if CalendarEventId already stored.
///   Edge   — Graph 409 treated as success.
///   Edge   — 401 from Graph → token refresh → retry; refresh failure → 401 + tokens cleared.
///   Guard  — HMAC state validation, expired PKCE session, session mismatch, missing config.
/// </summary>
public sealed class MicrosoftCalendarCallbackEndpointTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private const string StateSecret    = "test-state-secret-min-32-chars-ok!";
    private const string AesKeyMaterial = "test-aes-key";
    private const int    AppointmentId  = 2;   // distinct from Google tests (1)
    private const int    PatientId      = 55;

    private static ApplicationDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(opts);
    }

    private static int StatusCode(IResult result)
    {
        var prop = result.GetType().GetProperty("StatusCode");
        return (int)(prop?.GetValue(result) ?? 0);
    }

    private static IOptions<CalendarSettings> FullSettings() => Options.Create(new CalendarSettings
    {
        MicrosoftClientId     = "ms-client-id",
        MicrosoftClientSecret = "ms-client-secret",
        MicrosoftRedirectUri  = "http://localhost/ms-callback",
        MicrosoftTenantId     = "common",
        StateSecret           = StateSecret,
        AesKey                = AesKeyMaterial
    });

    private static string BuildState(int appointmentId, int patientId, string nonce)
    {
        var stateData = $"{appointmentId}|{patientId}|{nonce}";
        var sig       = CalendarSyncEndpoint.ComputeHmac(stateData, StateSecret);
        var encoded   = CalendarSyncEndpoint.Base64UrlEncode(Encoding.UTF8.GetBytes(stateData));
        return $"{encoded}.{sig}";
    }

    private static ApplicationDbContext SeedAppointmentWithSlot(ApplicationDbContext db)
    {
        var patient = new UserAccount
        {
            Id           = PatientId,
            Email        = "ms-patient@test.com",
            Role         = "patient",
            FirstName    = "M",
            LastName     = "S",
            PasswordHash = "h",
            IsActive     = true
        };
        db.UserAccounts.Add(patient);

        var slot = new Slot
        {
            Id              = 20,
            SlotTime        = DateTime.UtcNow.AddHours(48),
            DurationMinutes = 30,
            IsAvailable     = false
        };
        db.Slots.Add(slot);

        var appt = new Appointment
        {
            Id        = AppointmentId,
            PatientId = PatientId,
            SlotId    = slot.Id,
            Status    = AppointmentStatus.Scheduled,
            BookedAt  = DateTime.UtcNow
        };
        db.Appointments.Add(appt);
        db.SaveChanges();
        return db;
    }

    private static Mock<ICacheService> CacheWithSession(string nonce, PkceSession session)
    {
        var m = new Mock<ICacheService>();
        m.Setup(c => c.GetAsync<PkceSession>($"pkce:{nonce}", It.IsAny<CancellationToken>()))
         .ReturnsAsync(session);
        m.Setup(c => c.DeleteAsync($"pkce:{nonce}", It.IsAny<CancellationToken>()))
         .Returns(Task.CompletedTask);
        return m;
    }

    private static Mock<ICacheService> EmptyCache()
    {
        var m = new Mock<ICacheService>();
        m.Setup(c => c.GetAsync<PkceSession>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
         .ReturnsAsync((PkceSession?)null);
        m.Setup(c => c.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
         .Returns(Task.CompletedTask);
        return m;
    }

    // ── Guard: HMAC state validation ──────────────────────────────────────────

    [Fact]
    public async Task HandleMicrosoftCallback_InvalidHmacState_Returns400()
    {
        var db     = CreateDb();
        var calSvc = new Mock<IMicrosoftCalendarService>();
        var state  = BuildState(AppointmentId, PatientId, "nonce1") + "X"; // tampered

        var result = await MicrosoftCalendarCallbackEndpoint.HandleMicrosoftCallback(
            "auth-code", state, db, EmptyCache().Object, calSvc.Object,
            FullSettings(), CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
        calSvc.Verify(s => s.ExchangeCodeAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleMicrosoftCallback_MissingDotInState_Returns400()
    {
        var db     = CreateDb();
        var calSvc = new Mock<IMicrosoftCalendarService>();

        var result = await MicrosoftCalendarCallbackEndpoint.HandleMicrosoftCallback(
            "auth-code", "invalidsigless", db, EmptyCache().Object, calSvc.Object,
            FullSettings(), CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── Guard: expired/missing PKCE session ───────────────────────────────────

    [Fact]
    public async Task HandleMicrosoftCallback_ExpiredPkceSession_Returns400()
    {
        var db     = CreateDb();
        var nonce  = "expired-nonce";
        var state  = BuildState(AppointmentId, PatientId, nonce);
        var calSvc = new Mock<IMicrosoftCalendarService>();

        var result = await MicrosoftCalendarCallbackEndpoint.HandleMicrosoftCallback(
            "auth-code", state, db, EmptyCache().Object, calSvc.Object,
            FullSettings(), CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── Guard: session mismatch ────────────────────────────────────────────────

    [Fact]
    public async Task HandleMicrosoftCallback_SessionMismatch_Returns400()
    {
        var db     = CreateDb();
        var nonce  = "mismatch-nonce";
        var state  = BuildState(AppointmentId, PatientId, nonce);
        // Session created for a different appointmentId
        var session = new PkceSession("verifier", AppointmentId + 99, PatientId);
        var cache   = CacheWithSession(nonce, session);
        var calSvc  = new Mock<IMicrosoftCalendarService>();

        var result = await MicrosoftCalendarCallbackEndpoint.HandleMicrosoftCallback(
            "auth-code", state, db, cache.Object, calSvc.Object,
            FullSettings(), CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── Guard: missing config ─────────────────────────────────────────────────

    [Fact]
    public async Task HandleMicrosoftCallback_MissingConfig_Returns503()
    {
        var db     = CreateDb();
        var nonce  = "config-nonce";
        var state  = BuildState(AppointmentId, PatientId, nonce);
        var cache  = CacheWithSession(nonce, new PkceSession("verifier", AppointmentId, PatientId));
        var calSvc = new Mock<IMicrosoftCalendarService>();

        // Config with no MicrosoftClientId
        var emptySettings = Options.Create(new CalendarSettings
        {
            StateSecret = StateSecret,
            AesKey      = AesKeyMaterial
        });

        var result = await MicrosoftCalendarCallbackEndpoint.HandleMicrosoftCallback(
            "auth-code", state, db, cache.Object, calSvc.Object,
            emptySettings, CancellationToken.None);

        Assert.Equal(503, StatusCode(result));
    }

    // ── AC-003 / AC-004: Valid flow — tokens encrypted and event created ───────

    [Fact]
    public async Task HandleMicrosoftCallback_ValidFlow_StoresEncryptedTokensAndCreatesEvent()
    {
        var db     = SeedAppointmentWithSlot(CreateDb());
        var nonce  = "valid-ms-nonce";
        var state  = BuildState(AppointmentId, PatientId, nonce);
        var session = new PkceSession("code-verifier-ms", AppointmentId, PatientId);
        var cache   = CacheWithSession(nonce, session);

        var calSvc = new Mock<IMicrosoftCalendarService>();
        calSvc.Setup(s => s.ExchangeCodeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
              .ReturnsAsync(new TokenExchangeResult(
                  "ms-access-token", "ms-refresh-token", DateTime.UtcNow.AddHours(1)));

        calSvc.Setup(s => s.CreateEventAsync(
                It.IsAny<string>(), It.IsAny<Appointment>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("ms-graph-event-id-001");

        var result = await MicrosoftCalendarCallbackEndpoint.HandleMicrosoftCallback(
            "auth-code", state, db, cache.Object, calSvc.Object,
            FullSettings(), CancellationToken.None);

        Assert.Equal(200, StatusCode(result));

        // AC-004: tokens must not be stored in plaintext.
        var token = await db.CalendarTokens.FirstOrDefaultAsync(
            c => c.AppointmentId == AppointmentId && c.Provider == "Microsoft");

        Assert.NotNull(token);
        Assert.NotEqual("ms-access-token", token!.EncryptedAccessToken);
        Assert.NotNull(token.EncryptedRefreshToken);
        Assert.NotEqual("ms-refresh-token", token.EncryptedRefreshToken!);
        Assert.Equal("ms-graph-event-id-001", token.CalendarEventId);

        // Verify AES round-trip.
        var key = AesCbc.DeriveKey(AesKeyMaterial);
        Assert.Equal("ms-access-token",  AesCbc.Decrypt(token.EncryptedAccessToken, key));
        Assert.Equal("ms-refresh-token", AesCbc.Decrypt(token.EncryptedRefreshToken, key));

        // AC-003: CreateEventAsync must have been called once.
        calSvc.Verify(s => s.CreateEventAsync(
            "ms-access-token", It.IsAny<Appointment>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── AC-004: Idempotency — CalendarEventId already stored ──────────────────

    [Fact]
    public async Task HandleMicrosoftCallback_IdempotentResync_SkipsEventCreation()
    {
        var db     = SeedAppointmentWithSlot(CreateDb());
        var nonce  = "idempotent-ms-nonce";
        var state  = BuildState(AppointmentId, PatientId, nonce);
        var session = new PkceSession("verifier", AppointmentId, PatientId);
        var cache   = CacheWithSession(nonce, session);

        // Pre-seed an existing CalendarToken with CalendarEventId already set.
        var key = AesCbc.DeriveKey(AesKeyMaterial);
        db.CalendarTokens.Add(new CalendarToken
        {
            PatientId            = PatientId,
            AppointmentId        = AppointmentId,
            Provider             = "Microsoft",
            EncryptedAccessToken = AesCbc.Encrypt("old-ms-token", key),
            ExpiresAt            = DateTime.UtcNow.AddHours(1),
            CalendarEventId      = "existing-ms-event-id-999"
        });
        db.SaveChanges();

        var calSvc = new Mock<IMicrosoftCalendarService>();
        calSvc.Setup(s => s.ExchangeCodeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
              .ReturnsAsync(new TokenExchangeResult("new-ms-token", null, DateTime.UtcNow.AddHours(1)));

        var result = await MicrosoftCalendarCallbackEndpoint.HandleMicrosoftCallback(
            "auth-code", state, db, cache.Object, calSvc.Object,
            FullSettings(), CancellationToken.None);

        Assert.Equal(200, StatusCode(result));

        // CreateEventAsync must NOT be called when CalendarEventId already exists.
        calSvc.Verify(s => s.CreateEventAsync(
            It.IsAny<string>(), It.IsAny<Appointment>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Edge: Graph 409 treated as success ────────────────────────────────────

    [Fact]
    public async Task HandleMicrosoftCallback_GraphConflict_Returns200()
    {
        var db     = SeedAppointmentWithSlot(CreateDb());
        var nonce  = "conflict-nonce";
        var state  = BuildState(AppointmentId, PatientId, nonce);
        var session = new PkceSession("verifier", AppointmentId, PatientId);
        var cache   = CacheWithSession(nonce, session);

        var calSvc = new Mock<IMicrosoftCalendarService>();
        calSvc.Setup(s => s.ExchangeCodeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
              .ReturnsAsync(new TokenExchangeResult("token", null, DateTime.UtcNow.AddHours(1)));

        calSvc.Setup(s => s.CreateEventAsync(
                It.IsAny<string>(), It.IsAny<Appointment>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new MicrosoftGraphConflictException());

        var result = await MicrosoftCalendarCallbackEndpoint.HandleMicrosoftCallback(
            "auth-code", state, db, cache.Object, calSvc.Object,
            FullSettings(), CancellationToken.None);

        // Edge case: 409 from Graph → treat as success.
        Assert.Equal(200, StatusCode(result));
    }

    // ── Edge: 401 from Graph → token refresh succeeds → event created ─────────

    [Fact]
    public async Task HandleMicrosoftCallback_GraphUnauthorized_RefreshSucceeds_EventCreated()
    {
        var db     = SeedAppointmentWithSlot(CreateDb());
        var nonce  = "refresh-success-nonce";
        var state  = BuildState(AppointmentId, PatientId, nonce);
        var session = new PkceSession("verifier", AppointmentId, PatientId);
        var cache   = CacheWithSession(nonce, session);

        var callCount = 0;
        var calSvc    = new Mock<IMicrosoftCalendarService>();

        calSvc.Setup(s => s.ExchangeCodeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
              .ReturnsAsync(new TokenExchangeResult(
                  "initial-token", "refresh-token-value", DateTime.UtcNow.AddHours(1)));

        calSvc.Setup(s => s.CreateEventAsync(
                It.IsAny<string>(), It.IsAny<Appointment>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(() =>
              {
                  callCount++;
                  if (callCount == 1)
                      // First call: 401 to trigger refresh path.
                      throw new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized);
                  return "refreshed-event-id";
              });

        calSvc.Setup(s => s.RefreshTokenAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new TokenExchangeResult(
                  "refreshed-access-token", "new-refresh-token", DateTime.UtcNow.AddHours(1)));

        var result = await MicrosoftCalendarCallbackEndpoint.HandleMicrosoftCallback(
            "auth-code", state, db, cache.Object, calSvc.Object,
            FullSettings(), CancellationToken.None);

        Assert.Equal(200, StatusCode(result));

        // RefreshTokenAsync must have been called once.
        calSvc.Verify(s => s.RefreshTokenAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // CalendarEventId must be the one returned after refresh.
        var token = await db.CalendarTokens.FirstOrDefaultAsync(
            c => c.AppointmentId == AppointmentId && c.Provider == "Microsoft");
        Assert.Equal("refreshed-event-id", token?.CalendarEventId);
    }

    // ── Edge: 401 from Graph → token refresh fails → 401 + tokens cleared ─────

    [Fact]
    public async Task HandleMicrosoftCallback_GraphUnauthorized_RefreshFails_Returns401AndClearsTokens()
    {
        var db     = SeedAppointmentWithSlot(CreateDb());
        var nonce  = "refresh-fail-nonce";
        var state  = BuildState(AppointmentId, PatientId, nonce);
        var session = new PkceSession("verifier", AppointmentId, PatientId);
        var cache   = CacheWithSession(nonce, session);

        var calSvc = new Mock<IMicrosoftCalendarService>();

        calSvc.Setup(s => s.ExchangeCodeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
              .ReturnsAsync(new TokenExchangeResult(
                  "initial-token", "bad-refresh-token", DateTime.UtcNow.AddHours(1)));

        calSvc.Setup(s => s.CreateEventAsync(
                It.IsAny<string>(), It.IsAny<Appointment>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized));

        calSvc.Setup(s => s.RefreshTokenAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new HttpRequestException("Refresh token revoked", null, HttpStatusCode.Unauthorized));

        var result = await MicrosoftCalendarCallbackEndpoint.HandleMicrosoftCallback(
            "auth-code", state, db, cache.Object, calSvc.Object,
            FullSettings(), CancellationToken.None);

        Assert.Equal(401, StatusCode(result));

        // Tokens must be cleared from DB.
        var token = await db.CalendarTokens.FirstOrDefaultAsync(
            c => c.AppointmentId == AppointmentId && c.Provider == "Microsoft");
        Assert.NotNull(token);
        Assert.Equal(string.Empty, token!.EncryptedAccessToken);
        Assert.Null(token.EncryptedRefreshToken);
    }

    // ── AC-001 / AC-002: CalendarSyncEndpoint — microsoft provider branch ─────

    [Fact]
    public async Task HandleCalendarSync_MicrosoftProvider_Returns200WithMicrosoftAuthUrl()
    {
        var db      = CreateDb();
        var (patient, appt) = SeedCalendarSyncData(db);
        var ctx     = BuildPatientContext(patient.Id);

        var cacheMock = new Mock<ICacheService>();
        cacheMock.Setup(c => c.SetAsync(
                It.IsAny<string>(), It.IsAny<PkceSession>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var msSettings = Options.Create(new CalendarSettings
        {
            MicrosoftClientId     = "ms-client-id",
            MicrosoftClientSecret = "ms-client-secret",
            MicrosoftRedirectUri  = "http://localhost/ms-callback",
            MicrosoftTenantId     = "common",
            StateSecret           = StateSecret,
            AesKey                = AesKeyMaterial
        });

        var result = await CalendarSyncEndpoint.HandleCalendarSync(
            appt.Id, new CalendarSyncRequest("microsoft"),
            ctx, db, cacheMock.Object, msSettings, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));

        // AC-001: URL must point to Microsoft identity platform.
        // AC-002: scope must include Calendars.ReadWrite.
        var responseType = result.GetType();
        var valueProp    = responseType.GetProperty("Value");
        var response     = valueProp?.GetValue(result) as CalendarSyncResponse;
        Assert.NotNull(response);
        Assert.Contains("login.microsoftonline.com", response!.AuthUrl);
        Assert.Contains("Calendars.ReadWrite", Uri.UnescapeDataString(response.AuthUrl));
        Assert.Contains("S256", response.AuthUrl);
    }

    [Fact]
    public async Task HandleCalendarSync_MicrosoftProvider_MissingConfig_Returns503()
    {
        var db      = CreateDb();
        var (patient, appt) = SeedCalendarSyncData(db);
        var ctx     = BuildPatientContext(patient.Id);

        var cacheMock = new Mock<ICacheService>();
        cacheMock.Setup(c => c.SetAsync(
                It.IsAny<string>(), It.IsAny<PkceSession>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        // No MicrosoftClientId configured.
        var emptyMsSettings = Options.Create(new CalendarSettings
        {
            StateSecret = StateSecret,
            AesKey      = AesKeyMaterial
        });

        var result = await CalendarSyncEndpoint.HandleCalendarSync(
            appt.Id, new CalendarSyncRequest("microsoft"),
            ctx, db, cacheMock.Object, emptyMsSettings, CancellationToken.None);

        Assert.Equal(503, StatusCode(result));
    }

    // ── Local seed helper (CalendarSync tests need UserAccount without fixed Id) ─

    private static (UserAccount patient, Appointment appt) SeedCalendarSyncData(
        ApplicationDbContext db)
    {
        var patient = new UserAccount
        {
            Email        = $"ms{Guid.NewGuid():N}@test.com",
            Role         = "patient",
            FirstName    = "M",
            LastName     = "S",
            PasswordHash = "h",
            IsActive     = true
        };
        db.UserAccounts.Add(patient);
        db.SaveChanges();

        var slot = new Slot
        {
            SlotTime        = DateTime.UtcNow.AddHours(48),
            DurationMinutes = 30,
            IsAvailable     = false
        };
        db.Slots.Add(slot);
        db.SaveChanges();

        var appt = new Appointment
        {
            PatientId = patient.Id,
            SlotId    = slot.Id,
            Status    = AppointmentStatus.Scheduled,
            BookedAt  = DateTime.UtcNow
        };
        db.Appointments.Add(appt);
        db.SaveChanges();

        return (patient, appt);
    }

    private static System.Security.Claims.ClaimsPrincipal BuildPatientPrincipal(int userId)
        => new(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(
                System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub,
                userId.ToString())],
            "TestAuth"));

    private static Microsoft.AspNetCore.Http.HttpContext BuildPatientContext(int userId)
    {
        var ctx  = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        ctx.User = BuildPatientPrincipal(userId);
        return ctx;
    }

    // ── F1 fix: 409 path — CalendarEventId persisted as sentinel ─────────────

    [Fact]
    public async Task HandleMicrosoftCallback_GraphConflict_EventIdPersistedAsSentinel()
    {
        var db      = SeedAppointmentWithSlot(CreateDb());
        var nonce   = "conflict-persist-nonce";
        var state   = BuildState(AppointmentId, PatientId, nonce);
        var session = new PkceSession("verifier", AppointmentId, PatientId);
        var cache   = CacheWithSession(nonce, session);

        var calSvc = new Mock<IMicrosoftCalendarService>();
        calSvc.Setup(s => s.ExchangeCodeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
              .ReturnsAsync(new TokenExchangeResult("token", null, DateTime.UtcNow.AddHours(1)));

        calSvc.Setup(s => s.CreateEventAsync(
                It.IsAny<string>(), It.IsAny<Appointment>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new MicrosoftGraphConflictException());

        var result = await MicrosoftCalendarCallbackEndpoint.HandleMicrosoftCallback(
            "auth-code", state, db, cache.Object, calSvc.Object,
            FullSettings(), CancellationToken.None);

        Assert.Equal(200, StatusCode(result));

        // F1 fix: CalendarEventId must be non-null so next re-sync is idempotent.
        var token = await db.CalendarTokens.FirstOrDefaultAsync(
            c => c.AppointmentId == AppointmentId && c.Provider == "Microsoft");
        Assert.NotNull(token);
        Assert.NotNull(token!.CalendarEventId);
        Assert.Equal("CONFLICT", token.CalendarEventId);

        // Verify re-sync would be idempotent: CalendarEventId is not null → early return.
        // A second call with a fresh session must return 200 without calling CreateEventAsync.
        var nonce2   = "conflict-resync-nonce";
        var state2   = BuildState(AppointmentId, PatientId, nonce2);
        var session2 = new PkceSession("verifier2", AppointmentId, PatientId);
        var cache2   = CacheWithSession(nonce2, session2);

        calSvc.Setup(s => s.ExchangeCodeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
              .ReturnsAsync(new TokenExchangeResult("token2", null, DateTime.UtcNow.AddHours(1)));

        var result2 = await MicrosoftCalendarCallbackEndpoint.HandleMicrosoftCallback(
            "auth-code2", state2, db, cache2.Object, calSvc.Object,
            FullSettings(), CancellationToken.None);

        Assert.Equal(200, StatusCode(result2));
        // CreateEventAsync must NOT be called on the re-sync (idempotency works).
        calSvc.Verify(s => s.CreateEventAsync(
            It.IsAny<string>(), It.IsAny<Appointment>(), It.IsAny<CancellationToken>()),
            Times.Once); // called once on first attempt only
    }

    // ── F2 fix: 401 from Graph with no refresh token → 401 (not 502) ─────────

    [Fact]
    public async Task HandleMicrosoftCallback_GraphUnauthorized_NoRefreshToken_Returns401AndClearsToken()
    {
        var db      = SeedAppointmentWithSlot(CreateDb());
        var nonce   = "unauth-no-refresh-nonce";
        var state   = BuildState(AppointmentId, PatientId, nonce);
        var session = new PkceSession("verifier", AppointmentId, PatientId);
        var cache   = CacheWithSession(nonce, session);

        var calSvc = new Mock<IMicrosoftCalendarService>();

        // Exchange returns NO refresh token.
        calSvc.Setup(s => s.ExchangeCodeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
              .ReturnsAsync(new TokenExchangeResult("access-only-token", null,
                            DateTime.UtcNow.AddHours(1)));

        // Graph returns 401.
        calSvc.Setup(s => s.CreateEventAsync(
                It.IsAny<string>(), It.IsAny<Appointment>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new System.Net.Http.HttpRequestException(
                  "Unauthorized", null, System.Net.HttpStatusCode.Unauthorized));

        var result = await MicrosoftCalendarCallbackEndpoint.HandleMicrosoftCallback(
            "auth-code", state, db, cache.Object, calSvc.Object,
            FullSettings(), CancellationToken.None);

        // F2 fix: must return 401, NOT 502.
        Assert.Equal(401, StatusCode(result));

        // RefreshTokenAsync must never be called (no refresh token to use).
        calSvc.Verify(s => s.RefreshTokenAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Access token must be cleared.
        var token = await db.CalendarTokens.FirstOrDefaultAsync(
            c => c.AppointmentId == AppointmentId && c.Provider == "Microsoft");
        Assert.NotNull(token);
        Assert.Equal(string.Empty, token!.EncryptedAccessToken);
    }

    // ── EC-001: access_denied — code absent → 400 ────────────────────────────
    // AC-003 spec discrepancy: spec says HTTP 200 for access_denied; implementation
    // returns 400 because the handler guard fires (IsNullOrWhiteSpace(code)).

    [Fact]
    public async Task HandleMicrosoftCallback_MissingCode_Returns400()
    {
        var db     = CreateDb();
        var calSvc = new Mock<IMicrosoftCalendarService>();

        var result = await MicrosoftCalendarCallbackEndpoint.HandleMicrosoftCallback(
            "", BuildState(AppointmentId, PatientId, "deny-nonce"),
            db, EmptyCache().Object, calSvc.Object,
            FullSettings(), CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
        calSvc.Verify(s => s.ExchangeCodeAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── EC-002: ExchangeCodeAsync throws HttpRequestException → 502 ──────────

    [Fact]
    public async Task HandleMicrosoftCallback_ExchangeCodeThrowsHttpRequestException_Returns502()
    {
        var db      = SeedAppointmentWithSlot(CreateDb());
        var nonce   = "ms-exchange-error-nonce";
        var state   = BuildState(AppointmentId, PatientId, nonce);
        var session = new PkceSession("verifier-ms-error", AppointmentId, PatientId);
        var cache   = CacheWithSession(nonce, session);

        var calSvc = new Mock<IMicrosoftCalendarService>();
        calSvc.Setup(s => s.ExchangeCodeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
              .ThrowsAsync(new HttpRequestException("Microsoft token endpoint unreachable"));

        var result = await MicrosoftCalendarCallbackEndpoint.HandleMicrosoftCallback(
            "auth-code", state, db, cache.Object, calSvc.Object,
            FullSettings(), CancellationToken.None);

        Assert.Equal(502, StatusCode(result));
    }
}
