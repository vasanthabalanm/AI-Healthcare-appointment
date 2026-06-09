using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ClinicalHealthcare.Api.Features.Auth;
using ClinicalHealthcare.Api.Middleware;
using ClinicalHealthcare.Infrastructure.Auth;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Tests for TASK_015: JWT session management and Redis allowlist.
/// Covers AC-001 to AC-006.
/// </summary>
public sealed class JwtSessionTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private const string TestSecret = "test-secret-key-min-32-bytes-1234!!";

    private static ApplicationDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(opts);
    }

    private static IJwtTokenService CreateTokenService()
    {
        Environment.SetEnvironmentVariable("JWT_SECRET", TestSecret);
        return new JwtTokenService();
    }

    private static IPasswordHasher<string> CreateHasher() => new PasswordHasher<string>();

    private static UserAccount CreateActiveUser(ApplicationDbContext db, string role = "patient")
    {
        var hasher  = CreateHasher();
        var account = new UserAccount
        {
            Email        = "test@example.com",
            Role         = role,
            IsActive     = true,
            PasswordHash = hasher.HashPassword("test@example.com", "ValidPass1!")
        };
        db.UserAccounts.Add(account);
        db.SaveChanges();
        return account;
    }

    private static ILoggerFactory CreateLoggerFactory() =>
        LoggerFactory.Create(_ => { });

    // ── AC-001: JwtTokenService ───────────────────────────────────────────────

    [Fact]
    public void JwtTokenService_Constructor_ThrowsWhenSecretMissing()
    {
        Environment.SetEnvironmentVariable("JWT_SECRET", null);
        Assert.Throws<InvalidOperationException>(() => new JwtTokenService());
    }

    [Fact]
    public void JwtTokenService_Constructor_ThrowsWhenSecretTooShort()
    {
        Environment.SetEnvironmentVariable("JWT_SECRET", "tooshort");
        Assert.Throws<InvalidOperationException>(() => new JwtTokenService());
    }

    [Fact]
    public void JwtTokenService_GenerateToken_ReturnsDifferentJtiEachCall()
    {
        var svc = CreateTokenService();
        var (_, jti1) = svc.GenerateToken(1, "patient");
        var (_, jti2) = svc.GenerateToken(1, "patient");
        Assert.NotEqual(jti1, jti2);
    }

    [Fact]
    public void JwtTokenService_GenerateToken_TokenExpiresIn15Minutes()
    {
        var svc         = CreateTokenService();
        var before      = DateTime.UtcNow;
        var (token, _)  = svc.GenerateToken(1, "patient");

        var handler    = new JwtSecurityTokenHandler();
        var parsed     = handler.ReadJwtToken(token);
        var expiry     = parsed.ValidTo;

        // Expiry should be ~15 minutes from now (within a few seconds of tolerance).
        Assert.InRange(expiry, before.AddSeconds(895), before.AddSeconds(905));
    }

    [Fact]
    public void JwtTokenService_GenerateToken_ContainsExpectedClaims()
    {
        var svc        = CreateTokenService();
        var (token, _) = svc.GenerateToken(42, "admin");

        var handler = new JwtSecurityTokenHandler();
        var parsed  = handler.ReadJwtToken(token);

        Assert.Equal("42",    parsed.Subject);
        Assert.Contains(parsed.Claims, c => c.Type == "role" && c.Value == "admin");
        Assert.NotNull(parsed.Id); // jti claim
    }

    // ── AC-002: Login + Redis allowlist ───────────────────────────────────────

    [Fact]
    public async Task Login_ValidCredentials_Returns200()
    {
        await using var db     = CreateDb();
        var account            = CreateActiveUser(db);
        var tokenService       = CreateTokenService();
        var redisMock          = new Mock<IConnectionMultiplexer>();
        var dbMock             = new Mock<IDatabase>();
        redisMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);
        dbMock.Setup(m => m.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var result = await InvokeHandleLogin(db, createHasher: CreateHasher,
            email: account.Email, password: "ValidPass1!",
            tokenService: tokenService, multiplexer: redisMock.Object);

        Assert.Equal(StatusCodes.Status200OK, GetStatusCode(result));
    }

    [Fact]
    public async Task Login_InvalidPassword_Returns401()
    {
        await using var db = CreateDb();
        CreateActiveUser(db);

        var result = await InvokeHandleLogin(db, CreateHasher,
            email: "test@example.com", password: "WrongPassword!",
            tokenService: CreateTokenService(), multiplexer: NullMultiplexer());

        Assert.Equal(StatusCodes.Status401Unauthorized, GetStatusCode(result));
    }

    [Fact]
    public async Task Login_InactiveAccount_Returns401()
    {
        await using var db = CreateDb();
        var account = CreateActiveUser(db);
        account.IsActive = false;
        db.SaveChanges();

        var result = await InvokeHandleLogin(db, CreateHasher,
            email: account.Email, password: "ValidPass1!",
            tokenService: CreateTokenService(), multiplexer: NullMultiplexer());

        Assert.Equal(StatusCodes.Status401Unauthorized, GetStatusCode(result));
    }

    [Fact]
    public async Task Login_MissingEmail_Returns422()
    {
        await using var db = CreateDb();

        var result = await InvokeHandleLogin(db, CreateHasher,
            email: "", password: "anything",
            tokenService: CreateTokenService(), multiplexer: NullMultiplexer());

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, GetStatusCode(result));
    }

    [Fact]
    public async Task Login_MissingPassword_Returns422()
    {
        await using var db = CreateDb();

        var result = await InvokeHandleLogin(db, CreateHasher,
            email: "test@example.com", password: "   ",
            tokenService: CreateTokenService(), multiplexer: NullMultiplexer());

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, GetStatusCode(result));
    }

    // ── AC-005: Account lockout ───────────────────────────────────────────────

    [Fact]
    public async Task Login_FifthFailedAttempt_SetsLockout()
    {
        await using var db = CreateDb();
        var account        = CreateActiveUser(db);

        for (var i = 0; i < LoginEndpoint.MaxFailedAttempts; i++)
        {
            await InvokeHandleLogin(db, CreateHasher,
                email: account.Email, password: "WrongPass!",
                tokenService: CreateTokenService(), multiplexer: NullMultiplexer());

            db.ChangeTracker.Clear(); // detach to avoid stale cached entity
            account = db.UserAccounts.Single(u => u.Email == account.Email);
        }

        // After MaxFailedAttempts failures the account should be locked.
        account = db.UserAccounts.Single(u => u.Email == account.Email);
        Assert.NotNull(account.LockoutEnd);
        Assert.True(account.LockoutEnd!.Value > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Login_LockedAccount_Returns423()
    {
        await using var db = CreateDb();
        var account        = CreateActiveUser(db);
        account.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15);
        db.SaveChanges();

        var result = await InvokeHandleLogin(db, CreateHasher,
            email: account.Email, password: "ValidPass1!",
            tokenService: CreateTokenService(), multiplexer: NullMultiplexer());

        Assert.Equal(StatusCodes.Status423Locked, GetStatusCode(result));
    }

    // ── AC-005: Lockout audit log ─────────────────────────────────────────────

    [Fact]
    public async Task Login_FifthFailedAttempt_WritesLockoutAuditLog()
    {
        await using var db = CreateDb();
        var account        = CreateActiveUser(db);

        for (var i = 0; i < LoginEndpoint.MaxFailedAttempts; i++)
        {
            db.ChangeTracker.Clear();
            account = db.UserAccounts.Single(u => u.Email == account.Email);
            await InvokeHandleLogin(db, CreateHasher,
                email: account.Email, password: "WrongPass!",
                tokenService: CreateTokenService(), multiplexer: NullMultiplexer());
        }

        var lockoutLog = db.AuditLogs.SingleOrDefault(a => a.Action == "LOCKOUT");
        Assert.NotNull(lockoutLog);
        Assert.Equal("UserAccount", lockoutLog!.EntityType);
    }

    // ── AC-006: Redis fallback at login ───────────────────────────────────────

    [Fact]
    public async Task Login_RedisDown_StillReturns200()
    {
        await using var db   = CreateDb();
        var account          = CreateActiveUser(db);

        var brokenMultiplexer = new Mock<IConnectionMultiplexer>();
        brokenMultiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Throws(new RedisException("connection refused"));

        var result = await InvokeHandleLogin(db, CreateHasher,
            email: account.Email, password: "ValidPass1!",
            tokenService: CreateTokenService(), multiplexer: brokenMultiplexer.Object);

        // AC-006: Redis down must not block login.
        Assert.Equal(StatusCodes.Status200OK, GetStatusCode(result));
    }

    // ── SessionTtlMiddleware ──────────────────────────────────────────────────

    [Fact]
    public async Task SessionTtlMiddleware_UnauthenticatedRequest_PassesThrough()
    {
        var nextCalled     = false;
        var middleware     = new SessionTtlMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            CreateLoggerFactory().CreateLogger<SessionTtlMiddleware>(),
            NullMultiplexer());

        var ctx = new DefaultHttpContext();
        // Identity is not authenticated — no claims principal set.
        await middleware.InvokeAsync(ctx);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task SessionTtlMiddleware_MissingJtiClaim_Returns401()
    {
        var middleware = new SessionTtlMiddleware(
            _ => Task.CompletedTask,
            CreateLoggerFactory().CreateLogger<SessionTtlMiddleware>(),
            NullMultiplexer());

        var ctx = new DefaultHttpContext();
        // Authenticated but no jti claim.
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "1")], "Test"));

        await middleware.InvokeAsync(ctx);

        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task SessionTtlMiddleware_KeyNotInRedis_Returns401()
    {
        var dbMock   = new Mock<IDatabase>();
        dbMock.Setup(m => m.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        var muxMock = new Mock<IConnectionMultiplexer>();
        muxMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);

        var middleware = new SessionTtlMiddleware(
            _ => Task.CompletedTask,
            CreateLoggerFactory().CreateLogger<SessionTtlMiddleware>(),
            muxMock.Object);

        var ctx = BuildAuthenticatedContext("some-jti");
        await middleware.InvokeAsync(ctx);

        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task SessionTtlMiddleware_ValidJti_ResetsTtlAndPassesThrough()
    {
        var dbMock  = new Mock<IDatabase>();
        dbMock.Setup(m => m.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        dbMock.Setup(m => m.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan?>(),
                It.IsAny<ExpireWhen>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var muxMock = new Mock<IConnectionMultiplexer>();
        muxMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);

        var nextCalled = false;
        var middleware = new SessionTtlMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            CreateLoggerFactory().CreateLogger<SessionTtlMiddleware>(),
            muxMock.Object);

        var ctx = BuildAuthenticatedContext("valid-jti");
        await middleware.InvokeAsync(ctx);

        Assert.True(nextCalled);
        dbMock.Verify(m => m.KeyExpireAsync(
            It.Is<RedisKey>(k => k.ToString().Contains("valid-jti")),
            It.Is<TimeSpan?>(t => t!.Value.TotalSeconds == JwtTokenService.TokenExpirySeconds),
            It.IsAny<ExpireWhen>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task SessionTtlMiddleware_RedisDown_PassesThroughWithWarning()
    {
        var logEntries = new List<string>();
        var loggerMock = new Mock<ILogger<SessionTtlMiddleware>>();
        loggerMock.Setup(l => l.IsEnabled(LogLevel.Warning)).Returns(true);
        loggerMock
            .Setup(l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(() => logEntries.Add("warning"));

        var muxMock = new Mock<IConnectionMultiplexer>();
        muxMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Throws(new RedisException("connection refused"));

        var nextCalled = false;
        var middleware = new SessionTtlMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            loggerMock.Object,
            muxMock.Object);

        var ctx = BuildAuthenticatedContext("any-jti");
        await middleware.InvokeAsync(ctx);

        // AC-006: must pass through AND log a warning.
        Assert.True(nextCalled);
        Assert.NotEmpty(logEntries);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static DefaultHttpContext BuildAuthenticatedContext(string jti)
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(JwtRegisteredClaimNames.Jti, jti),
            new Claim(ClaimTypes.NameIdentifier, "1"),
        }, "Test"));
        return ctx;
    }

    private static IConnectionMultiplexer NullMultiplexer()
    {
        var dbMock  = new Mock<IDatabase>();
        var muxMock = new Mock<IConnectionMultiplexer>();
        muxMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);
        dbMock.Setup(m => m.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        return muxMock.Object;
    }

    /// <summary>
    /// Calls <c>LoginEndpoint.HandleLogin</c> via the public static method
    /// by building a minimal handler invocation context.
    /// </summary>
    private static async Task<IResult> InvokeHandleLogin(
        ApplicationDbContext  db,
        Func<IPasswordHasher<string>> createHasher,
        string                email,
        string                password,
        IJwtTokenService      tokenService,
        IConnectionMultiplexer multiplexer)
    {
        // Use reflection to invoke the private static HandleLogin method.
        var method = typeof(LoginEndpoint)
            .GetMethod("HandleLogin",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        // Build the LoginRequest record (inner private type).
        var requestType    = typeof(LoginEndpoint).GetNestedType("LoginRequest",
            System.Reflection.BindingFlags.NonPublic)!;
        var requestObj     = Activator.CreateInstance(requestType, email, password)!;

        var loggerFactory = CreateLoggerFactory();

        var task = (Task<IResult>)method.Invoke(null, new object[]
        {
            requestObj,
            db,
            createHasher(),
            tokenService,
            multiplexer,
            loggerFactory,
            default(CancellationToken)
        })!;

        return await task;
    }

    private static int GetStatusCode(IResult result)
    {
        // Execute the result against a dummy HttpContext to extract the status code.
        var ctx = new DefaultHttpContext();
        ctx.RequestServices = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        result.ExecuteAsync(ctx).GetAwaiter().GetResult();
        return ctx.Response.StatusCode;
    }
}

