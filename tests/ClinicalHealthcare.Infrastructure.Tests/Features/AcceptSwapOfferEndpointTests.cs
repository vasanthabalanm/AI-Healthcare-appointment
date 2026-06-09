using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ClinicalHealthcare.Api.Features.Appointments;
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
/// Unit tests for TASK_021: POST /waitlist/{id}/accept.
/// Covers AC-004 (transaction: appointment created, entry fulfilled) and all guard cases.
/// </summary>
public sealed class AcceptSwapOfferEndpointTests
{
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

    private static (UserAccount patient, Slot slot, WaitlistEntry entry) SeedOffer(
        ApplicationDbContext db, bool expired = false)
    {
        var patient = new UserAccount
        {
            Email        = "p@test.com",
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
            Status         = WaitlistStatus.OfferSent,
            QueuedAt       = DateTime.UtcNow.AddHours(-2),
            OfferExpiresAt = expired ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddHours(2),
            OfferedSlotId  = slot.Id,
        };
        db.WaitlistEntries.Add(entry);
        db.SaveChanges();

        return (patient, slot, entry);
    }

    // ── AC-004: valid accept creates appointment and fulfils entry ────────────

    [Fact]
    public async Task AcceptSwapOffer_ValidOffer_Returns200_AppointmentCreated_EntryFulfilled()
    {
        var db = CreateDb();
        var (patient, slot, entry) = SeedOffer(db);
        var cache = new Mock<ICacheService>();
        var ctx   = BuildPatientContext(patient.Id);

        var result = await AcceptSwapOfferEndpoint.HandleAcceptSwapOffer(
            entry.Id, ctx, db, cache.Object, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));

        var reloadedEntry = await db.WaitlistEntries.FindAsync(entry.Id);
        Assert.Equal(WaitlistStatus.Fulfilled, reloadedEntry!.Status);

        var appt = await db.Appointments.FirstOrDefaultAsync(a => a.PatientId == patient.Id);
        Assert.NotNull(appt);
        Assert.Equal(slot.Id, appt.SlotId);
        Assert.Equal(AppointmentStatus.Scheduled, appt.Status);
    }

    // ── Guard: expired offer window returns 400 ───────────────────────────────

    [Fact]
    public async Task AcceptSwapOffer_ExpiredOffer_Returns400()
    {
        var db = CreateDb();
        var (patient, _, entry) = SeedOffer(db, expired: true);
        var cache = new Mock<ICacheService>();
        var ctx   = BuildPatientContext(patient.Id);

        var result = await AcceptSwapOfferEndpoint.HandleAcceptSwapOffer(
            entry.Id, ctx, db, cache.Object, CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── Guard: wrong patient returns 403 ─────────────────────────────────────

    [Fact]
    public async Task AcceptSwapOffer_WrongPatient_Returns403()
    {
        var db = CreateDb();
        var (_, _, entry) = SeedOffer(db);
        var cache = new Mock<ICacheService>();
        var ctx   = BuildPatientContext(userId: 9999);

        var result = await AcceptSwapOfferEndpoint.HandleAcceptSwapOffer(
            entry.Id, ctx, db, cache.Object, CancellationToken.None);

        Assert.Equal(403, StatusCode(result));
    }

    // ── Guard: entry not in OfferSent state returns 400 ──────────────────────

    [Fact]
    public async Task AcceptSwapOffer_EntryNotOfferSent_Returns400()
    {
        var db = CreateDb();
        var (patient, _, _) = SeedOffer(db);
        var activeEntry = new WaitlistEntry
        {
            PatientId = patient.Id,
            Status    = WaitlistStatus.Active,
            QueuedAt  = DateTime.UtcNow,
        };
        db.WaitlistEntries.Add(activeEntry);
        db.SaveChanges();

        var cache = new Mock<ICacheService>();
        var ctx   = BuildPatientContext(patient.Id);

        var result = await AcceptSwapOfferEndpoint.HandleAcceptSwapOffer(
            activeEntry.Id, ctx, db, cache.Object, CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    // ── Guard: unauthenticated request returns 401 ────────────────────────────

    [Fact]
    public async Task AcceptSwapOffer_MissingSubClaim_Returns401()
    {
        var db    = CreateDb();
        var cache = new Mock<ICacheService>();
        var ctx   = new DefaultHttpContext(); // no claims

        var result = await AcceptSwapOfferEndpoint.HandleAcceptSwapOffer(
            id: 1, ctx, db, cache.Object, CancellationToken.None);

        Assert.Equal(401, StatusCode(result));
    }
}
