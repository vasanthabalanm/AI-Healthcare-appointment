using System.Text;
using ClinicalHealthcare.Infrastructure.Cache;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Cache;

/// <summary>
/// Unit tests for <see cref="CacheService"/> cache-aside behaviour (US_004).
/// All Redis interactions are mocked — no live Redis connection required.
/// </summary>
public sealed class CacheServiceTests
{
    private sealed record TestModel(int Id, string Name);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (CacheService svc,
                    Mock<IDatabase> dbMock,
                    Mock<IDatabaseAsync> dbAsyncMock,
                    Mock<ILogger<CacheService>> logMock)
        BuildService()
    {
        var dbMock      = new Mock<IDatabase>();
        var dbAsyncMock = dbMock.As<IDatabaseAsync>(); // StringSetAsync lives on IDatabaseAsync
        var muxMock     = new Mock<IConnectionMultiplexer>();
        muxMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
               .Returns(dbMock.Object);

        var logMock  = new Mock<ILogger<CacheService>>();
        var settings = Options.Create(new CacheSettings());
        var svc      = new CacheService(muxMock.Object, settings, logMock.Object);
        return (svc, dbMock, dbAsyncMock, logMock);
    }

    private static void SetupGet(Mock<IDatabase> db, string json)
        => db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
             .ReturnsAsync(new RedisValue(json));

    private static void SetupGetMiss(Mock<IDatabase> db)
        => db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
             .ReturnsAsync(RedisValue.Null);

    private static void SetupGetThrows(Mock<IDatabase> db)
        => db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
             .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "stub"));

    // StringSetAsync is declared on IDatabaseAsync; set up through that interface.
    private static void SetupSet(Mock<IDatabaseAsync> dbAsync)
        => dbAsync.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
             .ReturnsAsync(true);

    private static void SetupSetThrows(Mock<IDatabaseAsync> dbAsync)
        => dbAsync.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
             .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "stub"));

    // ── TC-001: cache hit returns deserialized value ───────────────────────────

    [Fact]
    public async Task GetAsync_CacheHit_ReturnsDeserializedValue()
    {
        var (svc, db, _, _) = BuildService();
        SetupGet(db, """{"id":1,"name":"Alice"}""");

        var result = await svc.GetAsync<TestModel>("key:1");

        Assert.NotNull(result);
        Assert.Equal(1,       result.Id);
        Assert.Equal("Alice", result.Name);
    }

    // ── TC-002: cache miss returns null ───────────────────────────────────────

    [Fact]
    public async Task GetAsync_CacheMiss_ReturnsNull()
    {
        var (svc, db, _, _) = BuildService();
        SetupGetMiss(db);

        var result = await svc.GetAsync<TestModel>("key:1");

        Assert.Null(result);
    }

    // ── TC-003: SetAsync stores the serialized value with the specified TTL ───

    [Fact]
    public async Task SetAsync_StoresSerialisedValueWithTtl()
    {
        var (svc, db, _, _) = BuildService();
        var ttl = TimeSpan.FromSeconds(60);

        await svc.SetAsync("key:1", new TestModel(1, "Alice"), ttl);

        // Inspect Moq's invocation log directly — bypasses the interface-inheritance
        // overload-matching limitation between IDatabase and IDatabaseAsync.
        var inv = db.Invocations.FirstOrDefault(i => i.Method.Name == "StringSetAsync");
        Assert.NotNull(inv);
        Assert.Equal((RedisKey)"key:1", (RedisKey)inv.Arguments[0]);
        Assert.Contains("\"name\":\"Alice\"", ((RedisValue)inv.Arguments[1]).ToString());
        // In StackExchange.Redis 2.7+, the expiry arg is typed as `Expiration` (wraps TimeSpan?).
        // Expiration has a public ctor from TimeSpan and value-equality via Equals.
        Assert.Equal(new Expiration(ttl), (Expiration)inv.Arguments[2]);
    }

    // ── EC-001: RedisConnectionException on GET → null returned, warning logged ─

    [Fact]
    public async Task GetAsync_RedisConnectionException_ReturnsNullAndLogsWarning()
    {
        var (svc, db, _, log) = BuildService();
        SetupGetThrows(db);

        var result = await svc.GetAsync<TestModel>("key:1");

        Assert.Null(result);
        log.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ── EC-002: RedisConnectionException on SET → no exception, warning logged ─
    // GetDatabase() itself throws to avoid the IDatabase/IDatabaseAsync interface
    // inheritance issue with mocking StringSetAsync directly.

    [Fact]
    public async Task SetAsync_RedisConnectionException_DoesNotThrowAndLogsWarning()
    {
        var logMock = new Mock<ILogger<CacheService>>();
        var muxMock = new Mock<IConnectionMultiplexer>();
        muxMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
               .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "stub"));

        var svc = new CacheService(muxMock.Object, Options.Create(new CacheSettings()), logMock.Object);

        // Must not throw — CacheService wraps RedisException in SetAsync
        await svc.SetAsync("key:1", new TestModel(1, "Bob"), TimeSpan.FromSeconds(30));

        logMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ── ES-001: DeleteAsync on a non-existent key does not throw ──────────────

    [Fact]
    public async Task DeleteAsync_NonExistentKey_DoesNotThrow()
    {
        var (svc, db, _, _) = BuildService();
        db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
          .ReturnsAsync(false); // false = key did not exist

        var exception = await Record.ExceptionAsync(() => svc.DeleteAsync("key:missing"));

        Assert.Null(exception);
    }
}
