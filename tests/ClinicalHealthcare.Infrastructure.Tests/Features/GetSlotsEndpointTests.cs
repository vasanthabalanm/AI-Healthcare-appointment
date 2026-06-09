using System.Security.Claims;
using ClinicalHealthcare.Api.Features.Appointments;
using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for TASK_019: GET /slots — Redis cache-aside slot browsing.
/// Covers AC-001, AC-002, and input validation edge cases.
/// </summary>
public sealed class GetSlotsEndpointTests
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

    private static string TomorrowDate => DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd");

    // ── AC-001: cache hit returns cached list, no DB query ────────────────────

    [Fact]
    public async Task GetSlots_CacheHit_ReturnsCachedSlots()
    {
        var db        = CreateDb();
        var cacheMock = new Mock<ICacheService>();
        var expected  = new List<GetSlotsEndpoint.SlotDto>
        {
            new(1, DateTime.UtcNow.AddDays(1).AddHours(9), 30)
        };

        cacheMock.Setup(c => c.GetAsync<List<GetSlotsEndpoint.SlotDto>>(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await GetSlotsEndpoint.HandleGetSlots(TomorrowDate, null, cacheMock.Object, db, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));
        // SetAsync must NOT be called — cache was warm.
        cacheMock.Verify(c => c.SetAsync(
            It.IsAny<string>(), It.IsAny<List<GetSlotsEndpoint.SlotDto>>(),
            It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── AC-002: cache miss queries DB and populates cache ─────────────────────

    [Fact]
    public async Task GetSlots_CacheMiss_QueriesDb_PopulatesCache()
    {
        var db = CreateDb();
        var tomorrow = DateTime.UtcNow.AddDays(1).Date;

        db.Slots.Add(new ClinicalHealthcare.Infrastructure.Entities.Slot
        {
            SlotTime        = tomorrow.AddHours(9),
            DurationMinutes = 30,
            IsAvailable     = true
        });
        db.SaveChanges();

        var cacheMock = new Mock<ICacheService>();
        cacheMock.Setup(c => c.GetAsync<List<GetSlotsEndpoint.SlotDto>>(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<GetSlotsEndpoint.SlotDto>?)null);

        var result = await GetSlotsEndpoint.HandleGetSlots(
            tomorrow.ToString("yyyy-MM-dd"), null, cacheMock.Object, db, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));
        // Cache must be populated after DB query.
        cacheMock.Verify(c => c.SetAsync(
            It.Is<string>(k => k.Contains(tomorrow.ToString("yyyy-MM-dd"))),
            It.IsAny<List<GetSlotsEndpoint.SlotDto>>(),
            GetSlotsEndpoint.CacheTtl,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── AC-002: unavailable slots are excluded ────────────────────────────────

    [Fact]
    public async Task GetSlots_CacheMiss_ExcludesUnavailableSlots()
    {
        var db = CreateDb();
        var tomorrow = DateTime.UtcNow.AddDays(1).Date;

        db.Slots.AddRange(
            new ClinicalHealthcare.Infrastructure.Entities.Slot { SlotTime = tomorrow.AddHours(9),  DurationMinutes = 30, IsAvailable = true },
            new ClinicalHealthcare.Infrastructure.Entities.Slot { SlotTime = tomorrow.AddHours(14), DurationMinutes = 30, IsAvailable = false }
        );
        db.SaveChanges();

        var cacheMock = new Mock<ICacheService>();
        cacheMock.Setup(c => c.GetAsync<List<GetSlotsEndpoint.SlotDto>>(
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<GetSlotsEndpoint.SlotDto>?)null);

        List<GetSlotsEndpoint.SlotDto>? capturedList = null;
        cacheMock.Setup(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<List<GetSlotsEndpoint.SlotDto>>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, List<GetSlotsEndpoint.SlotDto>, TimeSpan, CancellationToken>(
                (_, list, _, _) => capturedList = list)
            .Returns(Task.CompletedTask);

        await GetSlotsEndpoint.HandleGetSlots(tomorrow.ToString("yyyy-MM-dd"), null, cacheMock.Object, db, CancellationToken.None);

        Assert.NotNull(capturedList);
        Assert.Single(capturedList!);
        Assert.True(capturedList[0].SlotTime.Hour == 9);
    }

    // ── Input validation: missing date returns 400 ────────────────────────────

    [Fact]
    public async Task GetSlots_NullDate_Returns400()
    {
        var db        = CreateDb();
        var cacheMock = new Mock<ICacheService>();

        var result = await GetSlotsEndpoint.HandleGetSlots(null, null, cacheMock.Object, db, CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    [Fact]
    public async Task GetSlots_InvalidDate_Returns400()
    {
        var db        = CreateDb();
        var cacheMock = new Mock<ICacheService>();

        var result = await GetSlotsEndpoint.HandleGetSlots("not-a-date", null, cacheMock.Object, db, CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }
}
