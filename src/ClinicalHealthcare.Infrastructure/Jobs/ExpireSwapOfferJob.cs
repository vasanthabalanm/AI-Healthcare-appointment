using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicalHealthcare.Infrastructure.Jobs;

/// <summary>
/// Hangfire recurring job (runs every minute) that expires outstanding swap offers
/// whose acceptance window has elapsed (AC-005).
///
/// For each <see cref="WaitlistStatus.OfferSent"/> entry past its
/// <see cref="WaitlistEntry.OfferExpiresAt"/>:
/// <list type="bullet">
///   <item>Set entry <see cref="WaitlistStatus.Expired"/>.</item>
///   <item>Re-release the held slot (<c>IsAvailable = true</c>).</item>
///   <item>Invalidate the Redis slot-availability cache for that date.</item>
/// </list>
/// </summary>
public sealed class ExpireSwapOfferJob
{
    private readonly ApplicationDbContext      _db;
    private readonly ICacheService             _cache;
    private readonly ILogger<ExpireSwapOfferJob> _logger;

    public ExpireSwapOfferJob(
        ApplicationDbContext       db,
        ICacheService              cache,
        ILogger<ExpireSwapOfferJob> logger)
    {
        _db     = db;
        _cache  = cache;
        _logger = logger;
    }

    /// <summary>Runs expiry sweep. Called every minute by Hangfire recurring job.</summary>
    [AutomaticRetry(Attempts = 1)]
    public async Task ExecuteAsync(IJobCancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var expired = await _db.WaitlistEntries
            .Where(w => w.Status == WaitlistStatus.OfferSent && w.OfferExpiresAt <= now)
            .ToListAsync();

        if (expired.Count == 0)
            return;

        // Collect offered slot IDs so we can load and re-release them.
        var offeredSlotIds = expired
            .Where(w => w.OfferedSlotId.HasValue)
            .Select(w => w.OfferedSlotId!.Value)
            .Distinct()
            .ToList();

        var slots = await _db.Slots
            .Where(s => offeredSlotIds.Contains(s.Id))
            .ToListAsync();

        var slotMap = slots.ToDictionary(s => s.Id);

        foreach (var entry in expired)
        {
            entry.Status = WaitlistStatus.Expired;

            if (entry.OfferedSlotId.HasValue && slotMap.TryGetValue(entry.OfferedSlotId.Value, out var slot))
            {
                slot.IsAvailable = true;
            }
        }

        await _db.SaveChangesAsync();

        // Invalidate Redis cache for each re-released slot date (AC-005 + cache coherence).
        foreach (var slot in slotMap.Values)
        {
            var dateKey = $"slots:date:{DateOnly.FromDateTime(slot.SlotTime):yyyy-MM-dd}";
            await _cache.DeleteAsync(dateKey);
        }

        _logger.LogInformation(
            "ExpireSwapOfferJob: expired {Count} offer(s); re-released {SlotCount} slot(s).",
            expired.Count, slotMap.Count);
    }
}
