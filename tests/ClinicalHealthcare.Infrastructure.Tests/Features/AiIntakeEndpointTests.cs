using ClinicalHealthcare.Api.Features.Intake;
using ClinicalHealthcare.Infrastructure.AI;
using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for TASK_029 — AI conversational intake endpoints.
/// Verifies AC-001 to AC-006 and all guard branches.
/// </summary>
public sealed class AiIntakeEndpointTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ICacheService NoOpCache()
    {
        var m = new Mock<ICacheService>();
        m.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<AiIntakeSession>(),
                                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
         .Returns(Task.CompletedTask);
        m.Setup(c => c.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
         .Returns(Task.CompletedTask);
        return m.Object;
    }

    private static Mock<IRasaIntakeService> RasaMock(
        string replyText   = "Hello! How can I help?",
        double confidence  = 0.95)
    {
        var mock = new Mock<IRasaIntakeService>();
        mock.Setup(r => r.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RasaMessage(replyText, confidence));
        return mock;
    }

    private static HttpContext BuildPatientContext(int userId)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(
                    System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub,
                    userId.ToString())],
                "TestAuth"));
        return ctx;
    }

    private static int StatusCode(IResult result)
    {
        // IStatusCodeHttpResult covers OkHttpResult, NotFoundHttpResult, JsonHttpResult<T>, etc.
        if (result is Microsoft.AspNetCore.Http.IStatusCodeHttpResult sc && sc.StatusCode is not null)
            return sc.StatusCode.Value;
        var prop = result.GetType().GetProperty("StatusCode");
        return (int)(prop?.GetValue(result) ?? 0);
    }

    /// <summary>
    /// ForbidHttpResult.StatusCode returns null — check the type name instead.
    /// </summary>
    private static bool IsForbid(IResult result) =>
        result.GetType().Name == "ForbidHttpResult";

    // ── AC-001: StartAiIntake returns sessionId and greeting ─────────────────

    [Fact]
    public async Task StartAiIntake_Valid_ReturnsSessionId()
    {
        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<AiIntakeSession>(),
                                    It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        cache.Setup(c => c.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var rasa  = RasaMock("Welcome to AI intake!");
        var ctx   = BuildPatientContext(42);

        var result = await StartAiIntakeEndpoint.HandleStartAiIntake(
            ctx, cache.Object, rasa.Object, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));

        // Redis session was stored with TTL=900s (AC-002).
        cache.Verify(
            c => c.SetAsync(
                It.Is<string>(k => k.StartsWith("ai-intake:")),
                It.IsAny<AiIntakeSession>(),
                TimeSpan.FromSeconds(900),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── AC-005: Rasa unavailable → 503 ──────────────────────────────────────

    [Fact]
    public async Task StartAiIntake_RasaUnavailable_Returns503()
    {
        var cache = Mock.Of<ICacheService>(c =>
            c.SetAsync(It.IsAny<string>(), It.IsAny<AiIntakeSession>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()) == Task.CompletedTask &&
            c.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()) == Task.CompletedTask);

        var rasa = new Mock<IRasaIntakeService>();
        rasa.Setup(r => r.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RasaUnavailableException("down"));

        var result = await StartAiIntakeEndpoint.HandleStartAiIntake(
            BuildPatientContext(42), cache, rasa.Object, CancellationToken.None);

        Assert.Equal(503, StatusCode(result));
    }

    // ── AC-003: low confidence → no field committed ──────────────────────────

    [Fact]
    public async Task SendAiMessage_LowConfidence_FieldNotCommitted()
    {
        var existingSession = new AiIntakeSession
        {
            SessionId      = "sess-001",
            PatientId      = 7,
            ConfirmedFields = new(),
        };

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<AiIntakeSession>("ai-intake:sess-001", It.IsAny<CancellationToken>()))
             .ReturnsAsync(existingSession);
        cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<AiIntakeSession>(),
                                    It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var rasa   = RasaMock("Could you rephrase?", confidence: 0.50);
        var ctx    = BuildPatientContext(7);
        var req    = new SendAiMessageRequest("sess-001", "maybe something", "chiefComplaint");

        var result = await SendAiMessageEndpoint.HandleSendAiMessage(
            req, ctx, cache.Object, rasa.Object, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));
        // Field must NOT be in confirmedFields — check that SetAsync was called with empty dict.
        cache.Verify(
            c => c.SetAsync(
                "ai-intake:sess-001",
                It.Is<AiIntakeSession>(s => !s.ConfirmedFields.ContainsKey("chiefComplaint")),
                TimeSpan.FromSeconds(900),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── AC-003: high confidence → field committed ────────────────────────────

    [Fact]
    public async Task SendAiMessage_HighConfidence_FieldCommitted()
    {
        var existingSession = new AiIntakeSession
        {
            SessionId      = "sess-002",
            PatientId      = 9,
            ConfirmedFields = new(),
        };

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<AiIntakeSession>("ai-intake:sess-002", It.IsAny<CancellationToken>()))
             .ReturnsAsync(existingSession);
        cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<AiIntakeSession>(),
                                    It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var rasa = RasaMock("Got it!", confidence: 0.92);
        var ctx  = BuildPatientContext(9);
        var req  = new SendAiMessageRequest("sess-002", "headache", "chiefComplaint");

        var result = await SendAiMessageEndpoint.HandleSendAiMessage(
            req, ctx, cache.Object, rasa.Object, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));
        cache.Verify(
            c => c.SetAsync(
                "ai-intake:sess-002",
                It.Is<AiIntakeSession>(s => s.ConfirmedFields.ContainsKey("chiefComplaint")),
                TimeSpan.FromSeconds(900),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── AC-002: TTL reset on each message ────────────────────────────────────

    [Fact]
    public async Task SendAiMessage_AlwaysResetsTtl()
    {
        var session = new AiIntakeSession { SessionId = "sess-003", PatientId = 11 };

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<AiIntakeSession>("ai-intake:sess-003", It.IsAny<CancellationToken>()))
             .ReturnsAsync(session);
        cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<AiIntakeSession>(),
                                    It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var rasa = RasaMock(confidence: 0.30); // below threshold
        var req  = new SendAiMessageRequest("sess-003", "hi", null);

        await SendAiMessageEndpoint.HandleSendAiMessage(
            req, BuildPatientContext(11), cache.Object, rasa.Object, CancellationToken.None);

        // TTL must be refreshed even on low-confidence response.
        cache.Verify(
            c => c.SetAsync("ai-intake:sess-003", It.IsAny<AiIntakeSession>(),
                            TimeSpan.FromSeconds(900), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Guard: session expired → 404 ─────────────────────────────────────────

    [Fact]
    public async Task SendAiMessage_SessionExpired_Returns404()
    {
        var cache = Mock.Of<ICacheService>(c =>
            c.GetAsync<AiIntakeSession>(It.IsAny<string>(), It.IsAny<CancellationToken>()) ==
            Task.FromResult<AiIntakeSession?>(null));

        var rasa = RasaMock();
        var req  = new SendAiMessageRequest("missing-session", "hello", null);

        var result = await SendAiMessageEndpoint.HandleSendAiMessage(
            req, BuildPatientContext(1), cache, rasa.Object, CancellationToken.None);

        Assert.Equal(404, StatusCode(result));
    }

    // ── AC-005: Rasa unavailable during message → 503 ────────────────────────

    [Fact]
    public async Task SendAiMessage_RasaUnavailable_Returns503()
    {
        var session = new AiIntakeSession { SessionId = "sess-503", PatientId = 5 };
        var cache   = Mock.Of<ICacheService>(c =>
            c.GetAsync<AiIntakeSession>("ai-intake:sess-503", It.IsAny<CancellationToken>()) ==
            Task.FromResult<AiIntakeSession?>(session));

        var rasa = new Mock<IRasaIntakeService>();
        rasa.Setup(r => r.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RasaUnavailableException("down"));

        var result = await SendAiMessageEndpoint.HandleSendAiMessage(
            new SendAiMessageRequest("sess-503", "hello", null),
            BuildPatientContext(5), cache, rasa.Object, CancellationToken.None);

        Assert.Equal(503, StatusCode(result));
    }

    // ── AC-004: switch-to-manual returns confirmedFields + deletes session ────

    [Fact]
    public async Task SwitchToManual_ReturnsConfirmedFieldsAndDeletesSession()
    {
        var session = new AiIntakeSession
        {
            SessionId       = "sess-switch",
            PatientId       = 20,
            ConfirmedFields = new() { ["chiefComplaint"] = "headache" },
        };

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<AiIntakeSession>("ai-intake:sess-switch", It.IsAny<CancellationToken>()))
             .ReturnsAsync(session);
        cache.Setup(c => c.DeleteAsync("ai-intake:sess-switch", It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var result = await SwitchToManualEndpoint.HandleSwitchToManual(
            new SwitchToManualRequest("sess-switch"),
            BuildPatientContext(20), cache.Object, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));
        cache.Verify(c => c.DeleteAsync("ai-intake:sess-switch", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── AC-004: switch-to-manual session expired → 404 ───────────────────────

    [Fact]
    public async Task SwitchToManual_SessionExpired_Returns404()
    {
        var cache = Mock.Of<ICacheService>(c =>
            c.GetAsync<AiIntakeSession>(It.IsAny<string>(), It.IsAny<CancellationToken>()) ==
            Task.FromResult<AiIntakeSession?>(null));

        var result = await SwitchToManualEndpoint.HandleSwitchToManual(
            new SwitchToManualRequest("gone"), BuildPatientContext(1), cache, CancellationToken.None);

        Assert.Equal(404, StatusCode(result));
    }
    // ── AC-006: CompleteAiIntake creates IntakeRecord with Source=AI ───────────────

    [Fact]
    public async Task CompleteAiIntake_Valid_CreatesIntakeRecordWithSourceAI()
    {
        await using var db = BuildDb();
        var session = new AiIntakeSession
        {
            SessionId       = "sess-complete",
            PatientId       = 15,
            ConfirmedFields = new() { ["chiefComplaint"] = "headache", ["allergies"] = "penicillin" },
        };

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<AiIntakeSession>("ai-intake:sess-complete", It.IsAny<CancellationToken>()))
             .ReturnsAsync(session);
        cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<AiIntakeSession>(),
                                    It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        cache.Setup(c => c.DeleteAsync("ai-intake:sess-complete", It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        var result = await CompleteAiIntakeEndpoint.HandleCompleteAiIntake(
            new CompleteAiIntakeRequest("sess-complete"),
            BuildPatientContext(15), db, cache.Object, CancellationToken.None);

        Assert.Equal(201, StatusCode(result));
        var record = db.IntakeRecords.Single();
        Assert.Equal(IntakeSource.AI, record.Source);
        Assert.Equal(15, record.PatientId);
        Assert.Equal("headache", record.ChiefComplaint);
        Assert.Equal("penicillin", record.Allergies);
        cache.Verify(c => c.DeleteAsync("ai-intake:sess-complete", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── AC-006: session expired → 404 ─────────────────────────────────────────────

    [Fact]
    public async Task CompleteAiIntake_SessionExpired_Returns404()
    {
        await using var db = BuildDb();
        var cache = Mock.Of<ICacheService>(c =>
            c.GetAsync<AiIntakeSession>(It.IsAny<string>(), It.IsAny<CancellationToken>()) ==
            Task.FromResult<AiIntakeSession?>(null));

        var result = await CompleteAiIntakeEndpoint.HandleCompleteAiIntake(
            new CompleteAiIntakeRequest("gone"),
            BuildPatientContext(1), db, cache, CancellationToken.None);

        Assert.Equal(404, StatusCode(result));
    }

    // ── AC-006: idempotent retry returns existing record ───────────────────────────

    [Fact]
    public async Task CompleteAiIntake_AlreadyCompleted_ReturnsExistingRecord()
    {
        await using var db = BuildDb();
        var session = new AiIntakeSession
        {
            SessionId               = "sess-idem",
            PatientId               = 17,
            ConfirmedFields         = new(),
            CompletedIntakeRecordId = 99,
        };

        var cache = new Mock<ICacheService>();
        cache.Setup(c => c.GetAsync<AiIntakeSession>("ai-intake:sess-idem", It.IsAny<CancellationToken>()))
             .ReturnsAsync(session);

        var result = await CompleteAiIntakeEndpoint.HandleCompleteAiIntake(
            new CompleteAiIntakeRequest("sess-idem"),
            BuildPatientContext(17), db, cache.Object, CancellationToken.None);

        Assert.Equal(201, StatusCode(result));
        Assert.Empty(db.IntakeRecords); // no new record created
    }

    // ── Ownership guards → 403 ─────────────────────────────────────────────────────

    [Fact]
    public async Task SendAiMessage_WrongPatient_Returns403()
    {
        var session = new AiIntakeSession { SessionId = "sess-403a", PatientId = 99 };
        var cache   = Mock.Of<ICacheService>(c =>
            c.GetAsync<AiIntakeSession>("ai-intake:sess-403a", It.IsAny<CancellationToken>()) ==
            Task.FromResult<AiIntakeSession?>(session));

        var result = await SendAiMessageEndpoint.HandleSendAiMessage(
            new SendAiMessageRequest("sess-403a", "hello", null),
            BuildPatientContext(1), cache, RasaMock().Object, CancellationToken.None);

        Assert.True(IsForbid(result));
    }

    [Fact]
    public async Task CompleteAiIntake_WrongPatient_Returns403()
    {
        await using var db = BuildDb();
        var session = new AiIntakeSession { SessionId = "sess-403b", PatientId = 99 };
        var cache   = Mock.Of<ICacheService>(c =>
            c.GetAsync<AiIntakeSession>("ai-intake:sess-403b", It.IsAny<CancellationToken>()) ==
            Task.FromResult<AiIntakeSession?>(session));

        var result = await CompleteAiIntakeEndpoint.HandleCompleteAiIntake(
            new CompleteAiIntakeRequest("sess-403b"),
            BuildPatientContext(1), db, cache, CancellationToken.None);

        Assert.True(IsForbid(result));
    }

    [Fact]
    public async Task SwitchToManual_WrongPatient_Returns403()
    {
        var session = new AiIntakeSession { SessionId = "sess-403c", PatientId = 99 };
        var cache   = Mock.Of<ICacheService>(c =>
            c.GetAsync<AiIntakeSession>("ai-intake:sess-403c", It.IsAny<CancellationToken>()) ==
            Task.FromResult<AiIntakeSession?>(session));

        var result = await SwitchToManualEndpoint.HandleSwitchToManual(
            new SwitchToManualRequest("sess-403c"),
            BuildPatientContext(1), cache, CancellationToken.None);

        Assert.True(IsForbid(result));
    }

    // ── EC-001: empty sessionId → 400 Bad Request ──────────────────────────────

    [Fact]
    public async Task SendAiMessage_EmptySessionId_Returns400()
    {
        var cache = Mock.Of<ICacheService>();
        var rasa  = RasaMock();
        var req   = new SendAiMessageRequest(string.Empty, "hello", null);

        var result = await SendAiMessageEndpoint.HandleSendAiMessage(
            req, BuildPatientContext(1), cache, rasa.Object, CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
        rasa.Verify(r => r.SendMessageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── EC-002: empty message text → 400 Bad Request ──────────────────────────

    [Fact]
    public async Task SendAiMessage_EmptyMessage_Returns400()
    {
        var session = new AiIntakeSession { SessionId = "sess-empty", PatientId = 5 };
        var cache   = Mock.Of<ICacheService>(c =>
            c.GetAsync<AiIntakeSession>("ai-intake:sess-empty", It.IsAny<CancellationToken>()) ==
            Task.FromResult<AiIntakeSession?>(session));
        var rasa = RasaMock();
        var req  = new SendAiMessageRequest("sess-empty", string.Empty, null);

        var result = await SendAiMessageEndpoint.HandleSendAiMessage(
            req, BuildPatientContext(5), cache, rasa.Object, CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
        rasa.Verify(r => r.SendMessageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── EC-003: confidence exactly at 0.70 → inclusive boundary → committed ───

    [Fact]
    public void IsSufficientConfidence_ExactlyAtDefaultThreshold_ReturnsTrue()
    {
        // 0.70 is the default threshold — inclusive (>=).
        Assert.True(IRasaIntakeService.IsSufficientConfidence(0.70));
    }

    // ── AI-001: default threshold 0.70 — values above and at boundary ─────────

    [Theory]
    [InlineData(0.70)]
    [InlineData(0.71)]
    [InlineData(1.00)]
    public void IsSufficientConfidence_DefaultThreshold_ReturnsTrue(double confidence)
    {
        // No env var set — default threshold of 0.70 applies.
        Assert.True(IRasaIntakeService.IsSufficientConfidence(confidence));
    }

    // ── AI-002: env var AI_EXTRACTION_CONFIDENCE_THRESHOLD overrides default ───

    [Fact]
    public void IsSufficientConfidence_EnvVarOverride_UsesCustomThreshold()
    {
        Environment.SetEnvironmentVariable("AI_EXTRACTION_CONFIDENCE_THRESHOLD", "0.80");
        try
        {
            // 0.75 is above default 0.70 but below custom 0.80 → should return false.
            Assert.False(IRasaIntakeService.IsSufficientConfidence(0.75));
            // 0.80 is at the custom threshold → should return true.
            Assert.True(IRasaIntakeService.IsSufficientConfidence(0.80));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AI_EXTRACTION_CONFIDENCE_THRESHOLD", null);
        }
    }

    // ── AI-003: confidence 0.69 (just below default threshold) → false ────────

    [Fact]
    public void IsSufficientConfidence_BelowDefaultThreshold_ReturnsFalse()
    {
        Assert.False(IRasaIntakeService.IsSufficientConfidence(0.69));
    }

    // ── Helper: per-test InMemory database ──────────────────────────────────────

    private static ApplicationDbContext BuildDb()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(opts);
    }}
