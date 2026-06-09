using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Jobs;

/// <summary>
/// Unit tests for TASK_021: ExpireSwapOfferJob (AC-005).
/// Verifies expired offers are set to Expired, slot released, and cache invalidated.
/// </summary>
public sealed class ExpireSwapOfferJobTests
{
    private static ApplicationDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(opts);
    }

    // ── AC-005: expired offer → entry Expired, slot released, cache cleared ──

    [Fact]
    public async Task ExpireSwapOfferJob_ExpiredOffer_SetsExpired_ReleasesSlot_InvalidatesCache()
    {
        var db = CreateDb();

        var slot = new Slot
        {
            SlotTime        = DateTime.UtcNow.AddDays(1),
            DurationMinutes = 30,
            IsAvailable     = false,
        };
        db.Slots.Add(slot);
        db.SaveChanges();

        var entry = new WaitlistEntry
        {
            PatientId      = 1,
            Status         = WaitlistStatus.OfferSent,
            QueuedAt       = DateTime.UtcNow.AddHours(-3),
            OfferExpiresAt = DateTime.UtcNow.AddHours(-1), // already expired
            OfferedSlotId  = slot.Id,
        };
        db.WaitlistEntries.Add(entry);
        db.SaveChanges();

        var cacheMock = new Mock<ICacheService>();
        var job       = new ExpireSwapOfferJob(db, cacheMock.Object, NullLogger<ExpireSwapOfferJob>.Instance);

        await job.ExecuteAsync(null!);

        var reloadedEntry = await db.WaitlistEntries.FindAsync(entry.Id);
        Assert.Equal(WaitlistStatus.Expired, reloadedEntry!.Status);

        var reloadedSlot = await db.Slots.FindAsync(slot.Id);
        Assert.True(reloadedSlot!.IsAvailable, "Expired offer must re-release the slot.");

        cacheMock.Verify(
            c => c.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Guard: active (non-expired) offer is left unchanged ───────────────────

    [Fact]
    public async Task ExpireSwapOfferJob_ActiveOffer_NoChanges()
    {
        var db = CreateDb();

        var slot = new Slot
        {
            SlotTime        = DateTime.UtcNow.AddDays(1),
            DurationMinutes = 30,
            IsAvailable     = false,
        };
        db.Slots.Add(slot);
        db.SaveChanges();

        var entry = new WaitlistEntry
        {
            PatientId      = 1,
            Status         = WaitlistStatus.OfferSent,
            QueuedAt       = DateTime.UtcNow.AddHours(-1),
            OfferExpiresAt = DateTime.UtcNow.AddHours(2), // not yet expired
            OfferedSlotId  = slot.Id,
        };
        db.WaitlistEntries.Add(entry);
        db.SaveChanges();

        var cacheMock = new Mock<ICacheService>();
        var job       = new ExpireSwapOfferJob(db, cacheMock.Object, NullLogger<ExpireSwapOfferJob>.Instance);

        await job.ExecuteAsync(null!);

        var reloadedEntry = await db.WaitlistEntries.FindAsync(entry.Id);
        Assert.Equal(WaitlistStatus.OfferSent, reloadedEntry!.Status);

        cacheMock.Verify(
            c => c.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
