using ClinicalHealthcare.Api.Features.Auth;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Email;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for TASK_012: patient registration and email verification.
///
/// HTTP-layer concerns (rate limiting 429, header content-type) are not tested here —
/// those require an integration test host. The handler functions are called directly,
/// giving fast, isolated coverage of all business-logic acceptance criteria.
/// </summary>
public sealed class RegisterEndpointTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static IPasswordHasher<string> CreateHasher() =>
        new PasswordHasher<string>();

    private static IEmailService CreateFakeEmail() => new FakeEmailService();

    private static IConfiguration CreateConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:BaseUrl"] = "https://localhost:7001"
            })
            .Build();

    private static RegisterRequest ValidRequest(string email = "alice@test.com") => new()
    {
        Email     = email,
        Password  = "P@ssword1",
        FirstName = "Alice",
        LastName  = "Smith"
    };

    // ── Status-code helper ────────────────────────────────────────────────────

    private static int StatusCode(IResult result)
    {
        var sc = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        return sc.StatusCode ?? throw new InvalidOperationException("StatusCode was null");
    }

    // ── AC-001: POST /auth/register returns 201 ───────────────────────────────

    [Fact]
    public async Task Register_WithValidRequest_Returns201()
    {
        await using var db = CreateDb();

        var result = await RegisterEndpoint.HandleRegister(
            ValidRequest(), db, CreateHasher(), CreateFakeEmail(),
            new DefaultHttpContext(), CreateConfig(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status201Created, StatusCode(result));
    }

    // ── AC-005: UserAccount created with Role=patient, IsActive=false ─────────

    [Fact]
    public async Task Register_CreatesAccount_WithPatientRole_AndIsActiveFalse()
    {
        await using var db = CreateDb();

        await RegisterEndpoint.HandleRegister(
            ValidRequest("bob@test.com"), db, CreateHasher(), CreateFakeEmail(),
            new DefaultHttpContext(), CreateConfig(), CancellationToken.None);

        var account = await db.UserAccounts.IgnoreQueryFilters()
            .FirstAsync(u => u.Email == "bob@test.com");
        Assert.Equal("patient", account.Role);
        Assert.False(account.IsActive);
    }

    // ── AC-002: Token generated, stored hashed, valid 24h ────────────────────

    [Fact]
    public async Task Register_SetsVerificationTokenHash_And24hExpiry()
    {
        await using var db = CreateDb();
        var before = DateTime.UtcNow;

        await RegisterEndpoint.HandleRegister(
            ValidRequest("carol@test.com"), db, CreateHasher(), CreateFakeEmail(),
            new DefaultHttpContext(), CreateConfig(), CancellationToken.None);

        var account = await db.UserAccounts.IgnoreQueryFilters()
            .FirstAsync(u => u.Email == "carol@test.com");
        Assert.NotNull(account.VerificationTokenHash);
        Assert.NotNull(account.VerificationTokenExpiry);
        var expiry = account.VerificationTokenExpiry!.Value;
        Assert.InRange(expiry, before.AddHours(23).AddMinutes(59), before.AddHours(24).AddSeconds(5));
    }

    [Fact]
    public async Task Register_TokenHash_IsDifferentFromRawToken()
    {
        await using var db = CreateDb();
        var fakeEmail = new FakeEmailService();

        await RegisterEndpoint.HandleRegister(
            ValidRequest("dave@test.com"), db, CreateHasher(), fakeEmail,
            new DefaultHttpContext(), CreateConfig(), CancellationToken.None);

        // The email contains the raw token in the verify link
        Assert.NotNull(fakeEmail.LastHtmlBody);
        Assert.Contains("/auth/verify-email?token=", fakeEmail.LastHtmlBody);

        var account = await db.UserAccounts.IgnoreQueryFilters()
            .FirstAsync(u => u.Email == "dave@test.com");

        // Stored hash must not equal the raw token sent in the email
        var rawToken = ExtractTokenFromLink(fakeEmail.LastHtmlBody!);
        Assert.NotEqual(rawToken, account.VerificationTokenHash);
    }

    [Fact]
    public async Task Register_SendsVerificationEmail_ToCorrectAddress()
    {
        await using var db = CreateDb();
        var fakeEmail = new FakeEmailService();

        await RegisterEndpoint.HandleRegister(
            ValidRequest("eve@test.com"), db, CreateHasher(), fakeEmail,
            new DefaultHttpContext(), CreateConfig(), CancellationToken.None);

        Assert.Equal("eve@test.com", fakeEmail.LastToEmail);
        Assert.NotNull(fakeEmail.LastSubject);
        Assert.Contains("Verify", fakeEmail.LastSubject, StringComparison.OrdinalIgnoreCase);
    }

    // ── AC-003: Duplicate email returns 409 ───────────────────────────────────

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        await using var db = CreateDb();

        await RegisterEndpoint.HandleRegister(
            ValidRequest("frank@test.com"), db, CreateHasher(), CreateFakeEmail(),
            new DefaultHttpContext(), CreateConfig(), CancellationToken.None);

        var result = await RegisterEndpoint.HandleRegister(
            ValidRequest("frank@test.com"), db, CreateHasher(), CreateFakeEmail(),
            new DefaultHttpContext(), CreateConfig(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, StatusCode(result));
    }

    [Fact]
    public async Task Register_DuplicateEmail_CaseInsensitive_Returns409()
    {
        await using var db = CreateDb();

        await RegisterEndpoint.HandleRegister(
            ValidRequest("Grace@test.com"), db, CreateHasher(), CreateFakeEmail(),
            new DefaultHttpContext(), CreateConfig(), CancellationToken.None);

        var result = await RegisterEndpoint.HandleRegister(
            ValidRequest("grace@TEST.COM"), db, CreateHasher(), CreateFakeEmail(),
            new DefaultHttpContext(), CreateConfig(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status409Conflict, StatusCode(result));
    }

    // ── VerifyEmail: activates account on valid token ─────────────────────────

    [Fact]
    public async Task VerifyEmail_WithValidToken_ActivatesAccount()
    {
        await using var db = CreateDb();
        var fakeEmail = new FakeEmailService();

        await RegisterEndpoint.HandleRegister(
            ValidRequest("henry@test.com"), db, CreateHasher(), fakeEmail,
            new DefaultHttpContext(), CreateConfig(), CancellationToken.None);

        var rawToken = ExtractTokenFromLink(fakeEmail.LastHtmlBody!);
        var result   = await RegisterEndpoint.HandleVerifyEmail(rawToken, db, CancellationToken.None);

        Assert.Equal(StatusCodes.Status200OK, StatusCode(result));
        var account = await db.UserAccounts.IgnoreQueryFilters()
            .FirstAsync(u => u.Email == "henry@test.com");
        Assert.True(account.IsActive);
        Assert.Null(account.VerificationTokenHash);
        Assert.Null(account.VerificationTokenExpiry);
    }

    [Fact]
    public async Task VerifyEmail_WithExpiredToken_Returns400()
    {
        await using var db = CreateDb();
        var fakeEmail = new FakeEmailService();

        await RegisterEndpoint.HandleRegister(
            ValidRequest("iris@test.com"), db, CreateHasher(), fakeEmail,
            new DefaultHttpContext(), CreateConfig(), CancellationToken.None);

        // Manually expire the token
        var account = await db.UserAccounts.IgnoreQueryFilters()
            .FirstAsync(u => u.Email == "iris@test.com");
        account.VerificationTokenExpiry = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        var rawToken = ExtractTokenFromLink(fakeEmail.LastHtmlBody!);
        var result   = await RegisterEndpoint.HandleVerifyEmail(rawToken, db, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCode(result));
    }

    [Fact]
    public async Task VerifyEmail_WithInvalidToken_Returns400()
    {
        await using var db = CreateDb();

        var result = await RegisterEndpoint.HandleVerifyEmail("invalidtoken123", db, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusCode(result));
    }

    [Fact]
    public async Task VerifyEmail_TokenIsConsumed_AfterFirstUse()
    {
        await using var db = CreateDb();
        var fakeEmail = new FakeEmailService();

        await RegisterEndpoint.HandleRegister(
            ValidRequest("jack@test.com"), db, CreateHasher(), fakeEmail,
            new DefaultHttpContext(), CreateConfig(), CancellationToken.None);

        var rawToken = ExtractTokenFromLink(fakeEmail.LastHtmlBody!);

        // First use — success
        await RegisterEndpoint.HandleVerifyEmail(rawToken, db, CancellationToken.None);

        // Second use — token was consumed (hash is null)
        var result = await RegisterEndpoint.HandleVerifyEmail(rawToken, db, CancellationToken.None);
        Assert.Equal(StatusCodes.Status400BadRequest, StatusCode(result));
    }

    // ── Validation edge cases ─────────────────────────────────────────────────

    [Fact]
    public async Task Register_ShortPassword_Returns422()
    {
        await using var db = CreateDb();
        var request = new RegisterRequest
        {
            Email = "kate@test.com", Password = "short",
            FirstName = "Kate", LastName = "Test"
        };

        var result = await RegisterEndpoint.HandleRegister(
            request, db, CreateHasher(), CreateFakeEmail(),
            new DefaultHttpContext(), CreateConfig(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, StatusCode(result));
    }

    // ── SMTP failure handling ─────────────────────────────────────────────────

    [Fact]
    public async Task Register_SmtpFailure_Returns503_AndRollsBackAccount()
    {
        await using var db = CreateDb();

        var result = await RegisterEndpoint.HandleRegister(
            ValidRequest("smtp-fail@test.com"), db, CreateHasher(), new ThrowingEmailService(),
            new DefaultHttpContext(), CreateConfig(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, StatusCode(result));
        // Account must be rolled back so the user can retry without hitting 409.
        var count = await db.UserAccounts.IgnoreQueryFilters()
            .CountAsync(u => u.Email == "smtp-fail@test.com");
        Assert.Equal(0, count);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string ExtractTokenFromLink(string htmlBody)
    {
        // Find token= in the verification link
        const string marker = "token=";
        var idx = htmlBody.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) throw new InvalidOperationException("Verification link not found in email body.");
        var start = idx + marker.Length;
        var end   = htmlBody.IndexOf('"', start);
        var raw   = end > start ? htmlBody[start..end] : htmlBody[start..];
        return Uri.UnescapeDataString(raw);
    }
}

/// <summary>No-op email service for unit tests. Captures sent messages for assertion.</summary>
internal sealed class FakeEmailService : IEmailService
{
    public string? LastToEmail    { get; private set; }
    public string? LastSubject    { get; private set; }
    public string? LastHtmlBody   { get; private set; }

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        LastToEmail  = toEmail;
        LastSubject  = subject;
        LastHtmlBody = htmlBody;
        return Task.CompletedTask;
    }
}

/// <summary>Email service stub that always throws to simulate an SMTP connectivity failure.</summary>
internal sealed class ThrowingEmailService : IEmailService
{
    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
        => throw new System.Net.Sockets.SocketException(10061);
}
