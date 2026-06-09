using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Configuration;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Email;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Jobs;

/// <summary>
/// Unit tests for TASK_021: SwapMonitorJob.
/// Verifies F1 fix (slot released in no-entry branch) and F2 fix (email before save).
/// </summary>
public sealed class SwapMonitorJobTests
{
    private static ApplicationDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(opts);
    }

    private static SwapMonitorJob CreateJob(
        ApplicationDbContext  db,
        Mock<IEmailService>?  emailMock = null,
        Mock<ICacheService>?  cacheMock = null)
    {
        return new SwapMonitorJob(
            db,
            (emailMock ?? new Mock<IEmailService>()).Object,
            (cacheMock ?? new Mock<ICacheService>()).Object,
            Options.Create(new AppSettings { SwapOfferWindowHours = 2 }),
            NullLogger<SwapMonitorJob>.Instance);
    }

    // ── AC-002/AC-003: active entry found — sets OfferSent, sends email, holds slot ─

    [Fact]
    public async Task SwapMonitorJob_WithActiveEntry_SetsOfferSent_SendsEmail_SlotKeptUnavailable()
    {
        var db = CreateDb();

        var patient = new UserAccount
        {
            Email        = "patient@test.com",
            Role         = "patient",
            FirstName    = "P",
            LastName     = "T",
            PasswordHash = "h",
            IsActive     = true,
        };
        db.UserAccounts.Add(patient);
        db.SaveChanges();

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
            PatientId      = patient.Id,
            Status         = WaitlistStatus.Active,
            QueuedAt       = DateTime.UtcNow,
            PreferredSlotId = slot.Id,
        };
        db.WaitlistEntries.Add(entry);
        db.SaveChanges();

        var emailMock = new Mock<IEmailService>();
        var job       = CreateJob(db, emailMock: emailMock);

        await job.ExecuteAsync(slot.Id, null!);

        var reloadedEntry = await db.WaitlistEntries.FindAsync(entry.Id);
        Assert.Equal(WaitlistStatus.OfferSent, reloadedEntry!.Status);
        Assert.NotNull(reloadedEntry.OfferExpiresAt);
        Assert.Equal(slot.Id, reloadedEntry.OfferedSlotId);

        var reloadedSlot = await db.Slots.FindAsync(slot.Id);
        Assert.False(reloadedSlot!.IsAvailable, "Slot must stay unavailable during offer window.");

        emailMock.Verify(
            e => e.SendAsync(patient.Email, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── F1 fix: no waitlist entry — job releases slot and invalidates cache ────

    [Fact]
    public async Task SwapMonitorJob_NoActiveEntry_ReleasesSlot_InvalidatesCache()
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

        var cacheMock = new Mock<ICacheService>();
        var job       = CreateJob(db, cacheMock: cacheMock);

        await job.ExecuteAsync(slot.Id, null!);

        var reloadedSlot = await db.Slots.FindAsync(slot.Id);
        Assert.True(reloadedSlot!.IsAvailable, "Slot must be released when no patient is waiting.");

        cacheMock.Verify(
            c => c.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── AC-001 fallback: no exact-match entry → fall back to open-waitlist entry ─

    [Fact]
    public async Task SwapMonitorJob_OpenWaitlistEntry_FallsBackToOpenEntry_SetsOfferSent()
    {
        var db = CreateDb();

        var patient = new UserAccount
        {
            Email        = "open@test.com",
            Role         = "patient",
            FirstName    = "O",
            LastName     = "W",
            PasswordHash = "h",
            IsActive     = true,
        };
        db.UserAccounts.Add(patient);
        db.SaveChanges();

        var slot = new Slot
        {
            SlotTime        = DateTime.UtcNow.AddDays(2),
            DurationMinutes = 30,
            IsAvailable     = false,
        };
        db.Slots.Add(slot);
        db.SaveChanges();

        // Entry has no preferred slot (open waitlist).
        var entry = new WaitlistEntry
        {
            PatientId       = patient.Id,
            Status          = WaitlistStatus.Active,
            QueuedAt        = DateTime.UtcNow,
            PreferredSlotId = null,
        };
        db.WaitlistEntries.Add(entry);
        db.SaveChanges();

        var emailMock = new Mock<IEmailService>();
        var job       = CreateJob(db, emailMock: emailMock);

        await job.ExecuteAsync(slot.Id, null!);

        var reloadedEntry = await db.WaitlistEntries.FindAsync(entry.Id);
        Assert.Equal(WaitlistStatus.OfferSent, reloadedEntry!.Status);
        Assert.Equal(slot.Id, reloadedEntry.OfferedSlotId);

        emailMock.Verify(
            e => e.SendAsync(patient.Email, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Guard: slot not found — no side-effects ───────────────────────────────

    [Fact]
    public async Task SwapMonitorJob_SlotNotFound_NoSideEffects()
    {
        var db        = CreateDb();
        var emailMock = new Mock<IEmailService>();
        var cacheMock = new Mock<ICacheService>();
        var job       = CreateJob(db, emailMock, cacheMock);

        await job.ExecuteAsync(releasedSlotId: 9999, null!);

        emailMock.Verify(
            e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        cacheMock.Verify(
            c => c.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
