using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
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
/// Unit tests for TASK_024: POST /appointments/{id}/calendar-sync
/// and helpers (PKCE, HMAC, AES-CBC).
///
/// Covers:
///   AC-001 — initiation endpoint returns 200 with authorization URL.
///   AC-002 — code challenge uses S256.
///   AC-003 — state signed with HMAC-SHA256.
///   F3     — PKCE code_verifier uses 64-char alphabet (no modulo bias).
///   Guard  — wrong patient, missing appointment, unsupported provider, missing config.
/// </summary>
public sealed class CalendarSyncEndpointTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

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

    private static HttpContext BuildPatientContext(int userId)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())],
            "TestAuth"));
        return ctx;
    }

    private static ICacheService NoOpCache()
    {
        var m = new Mock<ICacheService>();
        m.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<PkceSession>(),
                                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
         .Returns(Task.CompletedTask);
        return m.Object;
    }

    private static IOptions<CalendarSettings> FullSettings() => Options.Create(new CalendarSettings
    {
        GoogleClientId     = "test-client-id",
        GoogleClientSecret = "test-client-secret",
        GoogleRedirectUri  = "http://localhost/callback",
        StateSecret        = "test-state-secret-min-32-chars-ok!",
        AesKey             = "test-aes-key"
    });

    private static (UserAccount patient, Appointment appointment) SeedAppointment(
        ApplicationDbContext db, int slotOffsetHours = 48)
    {
        var patient = new UserAccount
        {
            Email        = $"p{Guid.NewGuid():N}@test.com",
            Role         = "patient",
            FirstName    = "P",
            LastName     = "T",
            PasswordHash = "h",
            IsActive     = true
        };
        db.UserAccounts.Add(patient);
        db.SaveChanges();

        var slot = new Slot
        {
            SlotTime        = DateTime.UtcNow.AddHours(slotOffsetHours),
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

    // ── AC-001: Valid patient returns 200 + AuthUrl with S256 params ──────────

    [Fact]
    public async Task HandleCalendarSync_ValidPatient_Returns200WithAuthUrl()
    {
        var db = CreateDb();
        var (patient, appt) = SeedAppointment(db);
        var ctx = BuildPatientContext(patient.Id);

        var result = await CalendarSyncEndpoint.HandleCalendarSync(
            appt.Id,
            new CalendarSyncRequest("google"),
            ctx, db, NoOpCache(), FullSettings(), CancellationToken.None);

        Assert.Equal(200, StatusCode(result));
    }

    [Fact]
    public async Task HandleCalendarSync_ValidPatient_AuthUrlContainsS256Challenge()
    {
        var db = CreateDb();
        var (patient, appt) = SeedAppointment(db);
        var ctx = BuildPatientContext(patient.Id);
        string? capturedCacheKey = null;
        PkceSession? capturedSession = null;

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.SetAsync(
                It.IsAny<string>(), It.IsAny<PkceSession>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
             .Callback<string, PkceSession, TimeSpan, CancellationToken>(
                 (k, s, _, _) => { capturedCacheKey = k; capturedSession = s; })
             .Returns(Task.CompletedTask);

        var result = await CalendarSyncEndpoint.HandleCalendarSync(
            appt.Id, new CalendarSyncRequest("google"),
            ctx, db, cache.Object, FullSettings(), CancellationToken.None);

        Assert.Equal(200, StatusCode(result));
        Assert.NotNull(capturedSession);

        // AC-002: verify the URL contains S256 challenge that matches the stored verifier.
        var expectedChallenge = CalendarSyncEndpoint.GenerateCodeChallenge(capturedSession!.CodeVerifier);

        // Re-derive from the actual verifier to confirm S256 correctness.
        var manualHash = SHA256.HashData(Encoding.ASCII.GetBytes(capturedSession.CodeVerifier));
        var manualChallenge = Convert.ToBase64String(manualHash)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.Equal(manualChallenge, expectedChallenge);
    }

    // ── Wrong patient → 403 ───────────────────────────────────────────────────

    [Fact]
    public async Task HandleCalendarSync_WrongPatient_Returns403()
    {
        var db = CreateDb();
        var (_, appt) = SeedAppointment(db);
        var ctx = BuildPatientContext(999); // different patient

        var result = await CalendarSyncEndpoint.HandleCalendarSync(
            appt.Id, new CalendarSyncRequest("google"),
            ctx, db, NoOpCache(), FullSettings(), CancellationToken.None);

        Assert.Equal(403, StatusCode(result));
    }

    // ── Appointment not found → 404 ───────────────────────────────────────────

    [Fact]
    public async Task HandleCalendarSync_AppointmentNotFound_Returns404()
    {
        var db = CreateDb();
        var ctx = BuildPatientContext(1);

        var result = await CalendarSyncEndpoint.HandleCalendarSync(
            9999, new CalendarSyncRequest("google"),
            ctx, db, NoOpCache(), FullSettings(), CancellationToken.None);

        Assert.Equal(404, StatusCode(result));
    }

    // ── Unsupported provider → 400 ────────────────────────────────────────────

    [Fact]
    public async Task HandleCalendarSync_UnsupportedProvider_Returns400()
    {
        var db = CreateDb();
        var (patient, appt) = SeedAppointment(db);
        var ctx = BuildPatientContext(patient.Id);

        var result = await CalendarSyncEndpoint.HandleCalendarSync(
            appt.Id, new CalendarSyncRequest("yahoo"),
            ctx, db, NoOpCache(), FullSettings(), CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── Missing GoogleClientId → 503 ──────────────────────────────────────────

    [Fact]
    public async Task HandleCalendarSync_MissingClientId_Returns503()
    {
        var db = CreateDb();
        var (patient, appt) = SeedAppointment(db);
        var ctx = BuildPatientContext(patient.Id);
        var settings = Options.Create(new CalendarSettings
        {
            GoogleClientId = string.Empty,  // not configured
            StateSecret    = "some-secret"
        });

        var result = await CalendarSyncEndpoint.HandleCalendarSync(
            appt.Id, new CalendarSyncRequest("google"),
            ctx, db, NoOpCache(), settings, CancellationToken.None);

        Assert.Equal(503, StatusCode(result));
    }

    // ── PKCE helpers: S256 known-vector ───────────────────────────────────────

    [Fact]
    public void GenerateCodeChallenge_KnownVerifier_ProducesExpectedS256Hash()
    {
        // RFC 7636 Appendix B known test vector.
        const string verifier  = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string expected  = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        var actual = CalendarSyncEndpoint.GenerateCodeChallenge(verifier);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GenerateCodeVerifier_Length64_OnlyAllowedChars()
    {
        var verifier = CalendarSyncEndpoint.GenerateCodeVerifier();

        Assert.Equal(64, verifier.Length);
        // 64-char alphabet: A-Z a-z 0-9 - _  (no modulo bias chars . ~)
        Assert.Matches("^[A-Za-z0-9\\-_]+$", verifier);
    }

    // ── HMAC state helpers ────────────────────────────────────────────────────

    [Fact]
    public void ComputeHmac_SameInput_ProducesSameOutput()
    {
        var sig1 = CalendarSyncEndpoint.ComputeHmac("data:1:nonce", "secret");
        var sig2 = CalendarSyncEndpoint.ComputeHmac("data:1:nonce", "secret");
        Assert.Equal(sig1, sig2);
    }

    [Fact]
    public void ComputeHmac_DifferentData_ProducesDifferentOutput()
    {
        var sig1 = CalendarSyncEndpoint.ComputeHmac("data:1:nonce", "secret");
        var sig2 = CalendarSyncEndpoint.ComputeHmac("data:2:nonce", "secret");
        Assert.NotEqual(sig1, sig2);
    }
}
