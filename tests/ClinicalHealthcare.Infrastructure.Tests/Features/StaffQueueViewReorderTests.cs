using ClinicalHealthcare.Api.Features.Staff;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Features;

/// <summary>
/// Unit tests for TASK_034 — Staff queue view and manual reorder.
/// Covers AC-001 to AC-004 and edge cases.
/// </summary>
public sealed class StaffQueueViewReorderTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ApplicationDbContext BuildDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static int StatusCode(IResult result)
    {
        if (result is IStatusCodeHttpResult sc && sc.StatusCode is not null)
            return sc.StatusCode.Value;
        return (int)(result.GetType().GetProperty("StatusCode")?.GetValue(result) ?? 0);
    }

    /// <summary>Seeds today's Waiting QueueEntries with the given patient/staff IDs.</summary>
    private static async Task<(UserAccount staff, List<QueueEntry> entries)> SeedQueueAsync(
        ApplicationDbContext db, int entryCount = 3)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var staff = new UserAccount { Email = "staff@test.com", PasswordHash = "", Role = "staff", FirstName = "S", LastName = "T" };
        db.UserAccounts.Add(staff);

        var patients = Enumerable.Range(1, entryCount).Select(i =>
            new UserAccount { Email = $"p{i}@test.com", PasswordHash = "", Role = "patient", FirstName = $"P{i}", LastName = "Last" })
            .ToList();
        db.UserAccounts.AddRange(patients);
        await db.SaveChangesAsync();

        var entries = new List<QueueEntry>();
        for (var i = 0; i < entryCount; i++)
        {
            var e = new QueueEntry
            {
                PatientId      = patients[i].Id,
                QueueDate      = today,
                Position       = i + 1,
                Status         = QueueStatus.Waiting,
                IsWalkIn       = true,
                AddedByStaffId = staff.Id,
            };
            entries.Add(e);
            db.QueueEntries.Add(e);
        }
        await db.SaveChangesAsync();

        return (staff, entries);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // GetQueueEndpoint (AC-001)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetQueue_ReturnsWaitingEntriesOrderedByPosition()
    {
        await using var db = BuildDb();
        await SeedQueueAsync(db, entryCount: 3);

        var result = await GetQueueEndpoint.HandleGetQueue(db, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));
        var list = Assert.IsAssignableFrom<IReadOnlyList<QueueEntryDto>>(
            result.GetType().GetProperty("Value")?.GetValue(result));
        Assert.Equal(3, list.Count);
        Assert.Equal(1, list[0].Position);
        Assert.Equal(2, list[1].Position);
        Assert.Equal(3, list[2].Position);
    }

    [Fact]
    public async Task GetQueue_ExcludesRemovedEntries()
    {
        await using var db = BuildDb();
        var (_, entries) = await SeedQueueAsync(db, entryCount: 2);

        entries[0].Status = QueueStatus.Removed;
        await db.SaveChangesAsync();

        var result = await GetQueueEndpoint.HandleGetQueue(db, CancellationToken.None);

        var list = Assert.IsAssignableFrom<IReadOnlyList<QueueEntryDto>>(
            result.GetType().GetProperty("Value")?.GetValue(result));
        Assert.Single(list);
        Assert.Equal(2, list[0].Position);
    }

    [Fact]
    public async Task GetQueue_EmptyQueue_ReturnsEmptyList()
    {
        await using var db = BuildDb();

        var result = await GetQueueEndpoint.HandleGetQueue(db, CancellationToken.None);

        var list = Assert.IsAssignableFrom<IReadOnlyList<QueueEntryDto>>(
            result.GetType().GetProperty("Value")?.GetValue(result));
        Assert.Empty(list);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ReorderQueueEndpoint (AC-002 / AC-003)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Reorder_UpdatesPositionsInSuppliedOrder()
    {
        await using var db = BuildDb();
        var (_, entries) = await SeedQueueAsync(db, entryCount: 3);

        // Reverse the order: [3, 2, 1]
        var orderedIds = entries.Select(e => e.Id).Reverse().ToList();
        var rowVersions = entries.ToDictionary(e => e.Id, e => e.RowVersion);
        var req = new ReorderQueueRequest(orderedIds, rowVersions);

        var result = await ReorderQueueEndpoint.HandleReorder(req, db, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));

        // Re-query to confirm new positions.
        var reloaded = db.QueueEntries.OrderBy(q => q.Position).ToList();
        Assert.Equal(entries[2].Id, reloaded[0].Id);   // was 3rd, now 1st
        Assert.Equal(entries[1].Id, reloaded[1].Id);   // was 2nd, stays 2nd
        Assert.Equal(entries[0].Id, reloaded[2].Id);   // was 1st, now 3rd
    }

    [Fact]
    public async Task Reorder_IncompleteIdList_Returns400()
    {
        await using var db = BuildDb();
        var (_, entries) = await SeedQueueAsync(db, entryCount: 3);

        // Only supply 2 of the 3 entry IDs.
        var incompleteIds = entries.Take(2).Select(e => e.Id).ToList();
        var rowVersions   = entries.ToDictionary(e => e.Id, e => e.RowVersion);
        var req = new ReorderQueueRequest(incompleteIds, rowVersions);

        var result = await ReorderQueueEndpoint.HandleReorder(req, db, CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    [Fact]
    public async Task Reorder_ExtraIdNotInQueue_Returns400()
    {
        await using var db = BuildDb();
        var (_, entries) = await SeedQueueAsync(db, entryCount: 2);

        // Supply all real IDs + one that doesn't exist.
        var ids = entries.Select(e => e.Id).Append(9999).ToList();
        var rowVersions = entries.ToDictionary(e => e.Id, e => e.RowVersion);
        var req = new ReorderQueueRequest(ids, rowVersions);

        var result = await ReorderQueueEndpoint.HandleReorder(req, db, CancellationToken.None);

        Assert.Equal(400, StatusCode(result));
    }

    [Fact]
    public async Task Reorder_ConcurrentException_Returns409()
    {
        // Simulate DbUpdateConcurrencyException by using a throwing subclass.
        await using var db = new ThrowingConcurrencyDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        var staff = new UserAccount { Email = "s@test.com", PasswordHash = "", Role = "staff", FirstName = "S", LastName = "T" };
        var pat   = new UserAccount { Email = "p@test.com", PasswordHash = "", Role = "patient", FirstName = "P", LastName = "L" };
        db.UserAccounts.AddRange(staff, pat);
        await db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var entry = new QueueEntry
        {
            PatientId = pat.Id, QueueDate = today, Position = 1,
            Status = QueueStatus.Waiting, AddedByStaffId = staff.Id,
        };
        db.QueueEntries.Add(entry);
        await db.SaveChangesAsync();

        // Activate the throwing mode for the next SaveChangesAsync.
        db.ThrowOnNextSave = true;

        var req = new ReorderQueueRequest(
            [entry.Id],
            new Dictionary<int, byte[]> { [entry.Id] = entry.RowVersion });

        var result = await ReorderQueueEndpoint.HandleReorder(req, db, CancellationToken.None);

        Assert.Equal(409, StatusCode(result));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // RemoveQueueEntryEndpoint (AC-004)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Remove_SetsStatusRemoved()
    {
        await using var db = BuildDb();
        var (_, entries) = await SeedQueueAsync(db, entryCount: 2);

        var result = await RemoveQueueEntryEndpoint.HandleRemove(
            entries[0].Id, db, CancellationToken.None);

        Assert.Equal(200, StatusCode(result));
        var reloaded = db.QueueEntries.Find(entries[0].Id);
        Assert.Equal(QueueStatus.Removed, reloaded!.Status);
    }

    [Fact]
    public async Task Remove_RemovedEntryAbsentFromNextGet()
    {
        await using var db = BuildDb();
        var (_, entries) = await SeedQueueAsync(db, entryCount: 2);

        await RemoveQueueEntryEndpoint.HandleRemove(entries[0].Id, db, CancellationToken.None);

        var getResult = await GetQueueEndpoint.HandleGetQueue(db, CancellationToken.None);
        var list = Assert.IsAssignableFrom<IReadOnlyList<QueueEntryDto>>(
            getResult.GetType().GetProperty("Value")?.GetValue(getResult));
        Assert.Single(list);
        Assert.Equal(entries[1].Id, list[0].Id);
    }

    [Fact]
    public async Task Remove_NonExistentEntry_Returns404()
    {
        await using var db = BuildDb();

        var result = await RemoveQueueEntryEndpoint.HandleRemove(9999, db, CancellationToken.None);

        Assert.Equal(404, StatusCode(result));
    }

    // ── Helper: DbContext subclass that throws DbUpdateConcurrencyException ───

    private sealed class ThrowingConcurrencyDbContext : ApplicationDbContext
    {
        public bool ThrowOnNextSave { get; set; }

        public ThrowingConcurrencyDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (ThrowOnNextSave)
            {
                ThrowOnNextSave = false;
                throw new DbUpdateConcurrencyException(
                    "Simulated concurrent edit", []);
            }
            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
