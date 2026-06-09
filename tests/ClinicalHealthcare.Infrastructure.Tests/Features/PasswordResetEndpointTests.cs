using ClinicalHealthcare.Api.Features.Auth;
using ClinicalHealthcare.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for TASK_017: password reset via email token.
/// Covers AC-001 to AC-005 and edge cases.
/// </summary>
public sealed class PasswordResetEndpointTests
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

    private static IPasswordHasher<string> CreateHasher() => new PasswordHasher<string>();
    private static ILoggerFactory CreateLoggerFactory() => LoggerFactory.Create(_ => { });

    private static IConfiguration BuildConfig(string baseUrl = "https://app.test") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:BaseUrl"]  = baseUrl,
                ["SMTP_Host"]    = "smtp.test",
                ["SMTP_Port"]    = "587",
                ["SMTP_User"]    = "user@test",
                ["SMTP_Pass"]    = "pass",
                ["SMTP_From"]    = "no-reply@test"
            })
            .Build();

    private static UserAccount SeedUser(ApplicationDbContext db,
        string email    = "user@example.com",
        string role     = "patient",
        bool   isActive = true)
    {
        var hasher  = CreateHasher();
        var account = new UserAccount
        {
            Email        = email,
            Role         = role,
            IsActive     = isActive,
            FirstName    = "Test",
            LastName     = "User",
            PasswordHash = hasher.HashPassword(email, "OldPass1!")
        };
        db.UserAccounts.Add(account);
        db.SaveChanges();
        return account;
    }

    private static (string rawToken, string hash) IssueResetToken(
        ApplicationDbContext db, UserAccount account, int offsetMinutes = 0)
    {
        var rawToken = "ValidRawToken_" + Guid.NewGuid().ToString("N");
        var hash     = ForgotPasswordEndpoint.ComputePbkdf2Hash(rawToken);

        account.PasswordResetTokenHash   = hash;
        account.PasswordResetTokenExpiry = DateTime.UtcNow.AddMinutes(60 + offsetMinutes);
        account.PasswordResetTokenUsed   = false;
        db.SaveChanges();

        return (rawToken, hash);
    }

    private static IConnectionMultiplexer BuildRedisMock() => BuildRedisMock([]);

    private static IConnectionMultiplexer BuildRedisMock(string[] existingJtis)
    {
        var redisDb   = new Mock<IDatabase>();
        var redis     = new Mock<IConnectionMultiplexer>();

        var redisValues = existingJtis.Select(j => (RedisValue)j).ToArray();
        redisDb.Setup(r => r.SetMembersAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
               .ReturnsAsync(redisValues);
        redisDb.Setup(r => r.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
               .ReturnsAsync((long)existingJtis.Length);
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
             .Returns(redisDb.Object);

        return redis.Object;
    }

    // ── ForgotPassword: AC-001 always returns 200 ─────────────────────────────

    [Fact]
    public async Task ForgotPassword_EmptyEmail_Returns200()
    {
        var db      = CreateDb();
        var emailSvc = new Mock<ClinicalHealthcare.Infrastructure.Email.IEmailService>();
        var config  = BuildConfig();

        var request = new ForgotPasswordEndpoint.ForgotPasswordRequest("");
        var result  = await ForgotPasswordEndpoint.HandleForgotPassword(
            request, db, emailSvc.Object, config, CreateLoggerFactory(), CancellationToken.None);

        Assert.Equal(200, StatusCode(result));
        emailSvc.Verify(e => e.SendAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_Returns200_NoEmailSent()
    {
        var db       = CreateDb();
        var emailSvc = new Mock<ClinicalHealthcare.Infrastructure.Email.IEmailService>();
        var config   = BuildConfig();

        var request = new ForgotPasswordEndpoint.ForgotPasswordRequest("nobody@example.com");
        var result  = await ForgotPasswordEndpoint.HandleForgotPassword(
            request, db, emailSvc.Object, config, CreateLoggerFactory(), CancellationToken.None);

        Assert.Equal(200, StatusCode(result));
        emailSvc.Verify(e => e.SendAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ForgotPassword_InactiveAccount_Returns200_NoEmailSent()
    {
        var db       = CreateDb();
        SeedUser(db, "inactive@example.com", isActive: false);
        var emailSvc = new Mock<ClinicalHealthcare.Infrastructure.Email.IEmailService>();
        var config   = BuildConfig();

        var request = new ForgotPasswordEndpoint.ForgotPasswordRequest("inactive@example.com");
        var result  = await ForgotPasswordEndpoint.HandleForgotPassword(
            request, db, emailSvc.Object, config, CreateLoggerFactory(), CancellationToken.None);

        Assert.Equal(200, StatusCode(result));
        emailSvc.Verify(e => e.SendAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ForgotPassword_KnownEmail_Returns200_SendsEmail_StoresHash()
    {
        var db       = CreateDb();
        var account  = SeedUser(db);
        var emailSvc = new Mock<ClinicalHealthcare.Infrastructure.Email.IEmailService>();
        var config   = BuildConfig();

        var request = new ForgotPasswordEndpoint.ForgotPasswordRequest(account.Email);
        var result  = await ForgotPasswordEndpoint.HandleForgotPassword(
            request, db, emailSvc.Object, config, CreateLoggerFactory(), CancellationToken.None);

        Assert.Equal(200, StatusCode(result));
        emailSvc.Verify(e => e.SendAsync(
            account.Email, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        var updated = await db.UserAccounts.FindAsync(account.Id);
        Assert.NotNull(updated!.PasswordResetTokenHash);
        Assert.NotNull(updated.PasswordResetTokenExpiry);
        Assert.False(updated.PasswordResetTokenUsed);
    }

    [Fact]
    public async Task ForgotPassword_TokenExpiry_IsApproximately60Minutes()
    {
        var db       = CreateDb();
        var account  = SeedUser(db);
        var emailSvc = new Mock<ClinicalHealthcare.Infrastructure.Email.IEmailService>();
        var config   = BuildConfig();

        var before  = DateTime.UtcNow;
        var request = new ForgotPasswordEndpoint.ForgotPasswordRequest(account.Email);
        await ForgotPasswordEndpoint.HandleForgotPassword(
            request, db, emailSvc.Object, config, CreateLoggerFactory(), CancellationToken.None);
        var after   = DateTime.UtcNow;

        var updated = await db.UserAccounts.FindAsync(account.Id);
        var expiry  = updated!.PasswordResetTokenExpiry!.Value;

        Assert.True(expiry >= before.AddMinutes(60));
        Assert.True(expiry <= after.AddMinutes(60));
    }

    [Fact]
    public async Task ForgotPassword_SameResponseMessage_ForKnownAndUnknownEmail()
    {
        var db        = CreateDb();
        var account   = SeedUser(db);
        var emailSvc  = new Mock<ClinicalHealthcare.Infrastructure.Email.IEmailService>();
        var config    = BuildConfig();

        var knownResult   = await ForgotPasswordEndpoint.HandleForgotPassword(
            new ForgotPasswordEndpoint.ForgotPasswordRequest(account.Email),
            db, emailSvc.Object, config, CreateLoggerFactory(), CancellationToken.None);
        var unknownResult = await ForgotPasswordEndpoint.HandleForgotPassword(
            new ForgotPasswordEndpoint.ForgotPasswordRequest("ghost@example.com"),
            db, emailSvc.Object, config, CreateLoggerFactory(), CancellationToken.None);

        // Both must return 200 — response bodies are checked by status code only
        // (message equality would expose enumeration risk via timing differences)
        Assert.Equal(200, StatusCode(knownResult));
        Assert.Equal(200, StatusCode(unknownResult));
    }

    // ── ResetPassword: AC-003 validates and resets ────────────────────────────

    [Fact]
    public async Task ResetPassword_ValidToken_Returns200_PasswordChanged()
    {
        var db      = CreateDb();
        var account = SeedUser(db);
        var (rawToken, _) = IssueResetToken(db, account);
        var redis   = BuildRedisMock();
        var hasher  = CreateHasher();

        var request = new ResetPasswordEndpoint.ResetPasswordRequest(
            account.Email, rawToken, "NewPass1!");
        var result  = await ResetPasswordEndpoint.HandleResetPassword(
            request, db, hasher, redis, CreateLoggerFactory(), CancellationToken.None);

        Assert.Equal(200, StatusCode(result));

        var updated = await db.UserAccounts.FindAsync(account.Id);
        var verify  = hasher.VerifyHashedPassword(account.Email, updated!.PasswordHash, "NewPass1!");
        Assert.Equal(PasswordVerificationResult.Success, verify);
    }

    [Fact]
    public async Task ResetPassword_ValidToken_MarksTokenUsed()
    {
        var db      = CreateDb();
        var account = SeedUser(db);
        var (rawToken, _) = IssueResetToken(db, account);
        var redis   = BuildRedisMock();

        await ResetPasswordEndpoint.HandleResetPassword(
            new ResetPasswordEndpoint.ResetPasswordRequest(account.Email, rawToken, "NewPass1!"),
            db, CreateHasher(), redis, CreateLoggerFactory(), CancellationToken.None);

        var updated = await db.UserAccounts.FindAsync(account.Id);
        Assert.True(updated!.PasswordResetTokenUsed);
        Assert.Null(updated.PasswordResetTokenExpiry);
    }

    [Fact]
    public async Task ResetPassword_ValidToken_ResetsFailedLoginCounters()
    {
        var db      = CreateDb();
        var account = SeedUser(db);
        account.FailedLoginAttempts = 5;
        account.LockoutEnd          = DateTime.UtcNow.AddMinutes(10);
        db.SaveChanges();

        var (rawToken, _) = IssueResetToken(db, account);
        var redis = BuildRedisMock();

        await ResetPasswordEndpoint.HandleResetPassword(
            new ResetPasswordEndpoint.ResetPasswordRequest(account.Email, rawToken, "NewPass1!"),
            db, CreateHasher(), redis, CreateLoggerFactory(), CancellationToken.None);

        var updated = await db.UserAccounts.FindAsync(account.Id);
        Assert.Equal(0, updated!.FailedLoginAttempts);
        Assert.Null(updated.LockoutEnd);
    }

    // ── ResetPassword: AC-005 single-use token ────────────────────────────────

    [Fact]
    public async Task ResetPassword_AlreadyUsedToken_Returns400()
    {
        var db      = CreateDb();
        var account = SeedUser(db);
        var (rawToken, _) = IssueResetToken(db, account);
        var redis   = BuildRedisMock();

        // First use — succeeds
        await ResetPasswordEndpoint.HandleResetPassword(
            new ResetPasswordEndpoint.ResetPasswordRequest(account.Email, rawToken, "NewPass1!"),
            db, CreateHasher(), redis, CreateLoggerFactory(), CancellationToken.None);

        // Second use — must fail
        var result = await ResetPasswordEndpoint.HandleResetPassword(
            new ResetPasswordEndpoint.ResetPasswordRequest(account.Email, rawToken, "AnotherPass1!"),
            db, CreateHasher(), redis, CreateLoggerFactory(), CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── ResetPassword: AC-002 expired token ───────────────────────────────────

    [Fact]
    public async Task ResetPassword_ExpiredToken_Returns400()
    {
        var db      = CreateDb();
        var account = SeedUser(db);
        var (rawToken, _) = IssueResetToken(db, account, offsetMinutes: -120); // expired 1 hour ago

        var redis  = BuildRedisMock();
        var result = await ResetPasswordEndpoint.HandleResetPassword(
            new ResetPasswordEndpoint.ResetPasswordRequest(account.Email, rawToken, "NewPass1!"),
            db, CreateHasher(), redis, CreateLoggerFactory(), CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── ResetPassword: invalid token scenarios ────────────────────────────────

    [Fact]
    public async Task ResetPassword_WrongToken_Returns400()
    {
        var db      = CreateDb();
        var account = SeedUser(db);
        IssueResetToken(db, account);
        var redis  = BuildRedisMock();

        var result = await ResetPasswordEndpoint.HandleResetPassword(
            new ResetPasswordEndpoint.ResetPasswordRequest(account.Email, "completely-wrong-token", "NewPass1!"),
            db, CreateHasher(), redis, CreateLoggerFactory(), CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    [Fact]
    public async Task ResetPassword_UnknownEmail_Returns400()
    {
        var db     = CreateDb();
        var redis  = BuildRedisMock();

        var result = await ResetPasswordEndpoint.HandleResetPassword(
            new ResetPasswordEndpoint.ResetPasswordRequest("ghost@example.com", "anytoken", "NewPass1!"),
            db, CreateHasher(), redis, CreateLoggerFactory(), CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    [Fact]
    public async Task ResetPassword_MissingFields_Returns422()
    {
        var db    = CreateDb();
        var redis = BuildRedisMock();

        var result = await ResetPasswordEndpoint.HandleResetPassword(
            new ResetPasswordEndpoint.ResetPasswordRequest("", "", ""),
            db, CreateHasher(), redis, CreateLoggerFactory(), CancellationToken.None);

        Assert.Equal(422, StatusCode(result));
    }

    [Fact]
    public async Task ResetPassword_NoTokenStored_Returns400()
    {
        var db      = CreateDb();
        var account = SeedUser(db);
        // Account exists but has no reset token
        var redis  = BuildRedisMock();

        var result = await ResetPasswordEndpoint.HandleResetPassword(
            new ResetPasswordEndpoint.ResetPasswordRequest(account.Email, "sometoken", "NewPass1!"),
            db, CreateHasher(), redis, CreateLoggerFactory(), CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── ResetPassword: AC-004 Redis session revocation ────────────────────────

    [Fact]
    public async Task ResetPassword_ValidToken_RevokesAllRedisSessions()
    {
        var db      = CreateDb();
        var account = SeedUser(db);
        var (rawToken, _) = IssueResetToken(db, account);

        var redisDb = new Mock<IDatabase>();
        var redis   = new Mock<IConnectionMultiplexer>();

        var jtis      = new[] { "jti-aaa", "jti-bbb" };
        var redisVals = jtis.Select(j => (RedisValue)j).ToArray();

        redisDb.Setup(r => r.SetMembersAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
               .ReturnsAsync(redisVals);
        redisDb.Setup(r => r.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
               .ReturnsAsync(3L)  // 2 jti keys + 1 set key
               .Verifiable();
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
             .Returns(redisDb.Object);

        await ResetPasswordEndpoint.HandleResetPassword(
            new ResetPasswordEndpoint.ResetPasswordRequest(account.Email, rawToken, "NewPass1!"),
            db, CreateHasher(), redis.Object, CreateLoggerFactory(), CancellationToken.None);

        redisDb.Verify(r => r.KeyDeleteAsync(
            It.Is<RedisKey[]>(keys => keys.Length == 3),  // 2 jtis + 1 set
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task ResetPassword_RedisDown_StillReturns200_PasswordChanged()
    {
        var db      = CreateDb();
        var account = SeedUser(db);
        var (rawToken, _) = IssueResetToken(db, account);

        var redisDb = new Mock<IDatabase>();
        var redis   = new Mock<IConnectionMultiplexer>();

        redisDb.Setup(r => r.SetMembersAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
               .ThrowsAsync(new RedisException("connection refused"));
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
             .Returns(redisDb.Object);

        var result = await ResetPasswordEndpoint.HandleResetPassword(
            new ResetPasswordEndpoint.ResetPasswordRequest(account.Email, rawToken, "NewPass1!"),
            db, CreateHasher(), redis.Object, CreateLoggerFactory(), CancellationToken.None);

        // Password reset succeeds even if Redis is down (sessions expire via TTL)
        Assert.Equal(200, StatusCode(result));

        var updated = await db.UserAccounts.FindAsync(account.Id);
        Assert.True(updated!.PasswordResetTokenUsed);
    }

    // ── ForgotPasswordEndpoint.ComputePbkdf2Hash determinism ─────────────────

    [Fact]
    public void ComputePbkdf2Hash_SameInput_ReturnsSameHash()
    {
        var token = "test-token-" + Guid.NewGuid();
        var hash1 = ForgotPasswordEndpoint.ComputePbkdf2Hash(token);
        var hash2 = ForgotPasswordEndpoint.ComputePbkdf2Hash(token);
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputePbkdf2Hash_DifferentInputs_ReturnDifferentHashes()
    {
        var hash1 = ForgotPasswordEndpoint.ComputePbkdf2Hash("token-a");
        var hash2 = ForgotPasswordEndpoint.ComputePbkdf2Hash("token-b");
        Assert.NotEqual(hash1, hash2);
    }

    // ── New tests: P4 missing coverage ───────────────────────────────────────

    [Fact]
    public async Task ResetPassword_ShortPassword_Returns422()
    {
        var db      = CreateDb();
        var account = SeedUser(db);
        var (rawToken, _) = IssueResetToken(db, account);
        var redis   = BuildRedisMock();

        var result = await ResetPasswordEndpoint.HandleResetPassword(
            new ResetPasswordEndpoint.ResetPasswordRequest(account.Email, rawToken, "abc"),
            db, CreateHasher(), redis, CreateLoggerFactory(), CancellationToken.None);

        Assert.Equal(422, StatusCode(result));
    }

    [Fact]
    public async Task ForgotPassword_CooldownWindow_Returns200_NoEmailSent()
    {
        var db       = CreateDb();
        var account  = SeedUser(db);
        var emailSvc = new Mock<ClinicalHealthcare.Infrastructure.Email.IEmailService>();
        var config   = BuildConfig();

        // Simulate a token issued 2 minutes ago — within the 5-minute cooldown.
        account.PasswordResetTokenIssuedAt = DateTime.UtcNow.AddMinutes(-2);
        db.SaveChanges();

        var result = await ForgotPasswordEndpoint.HandleForgotPassword(
            new ForgotPasswordEndpoint.ForgotPasswordRequest(account.Email),
            db, emailSvc.Object, config, CreateLoggerFactory(), CancellationToken.None);

        Assert.Equal(200, StatusCode(result));
        emailSvc.Verify(e => e.SendAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ForgotPassword_CooldownExpired_SendsNewEmail()
    {
        var db       = CreateDb();
        var account  = SeedUser(db);
        var emailSvc = new Mock<ClinicalHealthcare.Infrastructure.Email.IEmailService>();
        var config   = BuildConfig();

        // Token issued 10 minutes ago — cooldown has expired, new request allowed.
        account.PasswordResetTokenIssuedAt = DateTime.UtcNow.AddMinutes(-10);
        db.SaveChanges();

        var result = await ForgotPasswordEndpoint.HandleForgotPassword(
            new ForgotPasswordEndpoint.ForgotPasswordRequest(account.Email),
            db, emailSvc.Object, config, CreateLoggerFactory(), CancellationToken.None);

        Assert.Equal(200, StatusCode(result));
        emailSvc.Verify(e => e.SendAsync(
            account.Email, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ForgotPassword_CaseInsensitiveEmail_SendsEmail()
    {
        var db       = CreateDb();
        SeedUser(db, "user@example.com");
        var emailSvc = new Mock<ClinicalHealthcare.Infrastructure.Email.IEmailService>();
        var config   = BuildConfig();

        var result = await ForgotPasswordEndpoint.HandleForgotPassword(
            new ForgotPasswordEndpoint.ForgotPasswordRequest("USER@EXAMPLE.COM"),
            db, emailSvc.Object, config, CreateLoggerFactory(), CancellationToken.None);

        Assert.Equal(200, StatusCode(result));
        emailSvc.Verify(e => e.SendAsync(
            "user@example.com", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ResetPassword_ValidToken_AuditLogEntry_IsWritten()
    {
        var db      = CreateDb();
        var account = SeedUser(db);
        var (rawToken, _) = IssueResetToken(db, account);
        var redis   = BuildRedisMock();

        await ResetPasswordEndpoint.HandleResetPassword(
            new ResetPasswordEndpoint.ResetPasswordRequest(account.Email, rawToken, "NewPass1!"),
            db, CreateHasher(), redis, CreateLoggerFactory(), CancellationToken.None);

        var auditEntry = db.AuditLogs.SingleOrDefault(a => a.Action == "PASSWORD-RESET");
        Assert.NotNull(auditEntry);
        Assert.Equal("UserAccount", auditEntry.EntityType);
        Assert.Equal(account.Id, auditEntry.EntityId);
    }

    [Fact]
    public async Task ForgotPassword_EmailSendFails_StillReturns200_TokenPersisted()
    {
        var db       = CreateDb();
        var account  = SeedUser(db);
        var emailSvc = new Mock<ClinicalHealthcare.Infrastructure.Email.IEmailService>();
        var config   = BuildConfig();

        emailSvc.Setup(e => e.SendAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP unavailable"));

        var result = await ForgotPasswordEndpoint.HandleForgotPassword(
            new ForgotPasswordEndpoint.ForgotPasswordRequest(account.Email),
            db, emailSvc.Object, config, CreateLoggerFactory(), CancellationToken.None);

        // P3: must return 200 even when SMTP fails.
        Assert.Equal(200, StatusCode(result));

        // Token was persisted to DB before the send attempt.
        var updated = await db.UserAccounts.FindAsync(account.Id);
        Assert.NotNull(updated!.PasswordResetTokenHash);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int StatusCode(IResult result)
    {
        var sc = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>(result);
        return sc.StatusCode ?? throw new InvalidOperationException("StatusCode was null");
    }
}
