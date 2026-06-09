using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Configuration;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Email;
using ClinicalHealthcare.Infrastructure.Entities;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClinicalHealthcare.Infrastructure.Jobs;

/// <summary>
/// Hangfire fire-and-forget job triggered when an appointment slot is released
/// (e.g. appointment cancelled — enqueued by <c>CancelAppointmentEndpoint</c>).
///
/// AC-002 — Finds the oldest <c>Active</c> <see cref="WaitlistEntry"/> whose
/// preferred date/slot matches the released slot, sends an offer notification,
/// holds the slot for the configured offer window, and transitions the entry
/// to <see cref="WaitlistStatus.OfferSent"/>.
///
/// AC-003 — Offer window is driven by <see cref="AppSettings.SwapOfferWindowHours"/>.
///
/// If no waitlisted patient exists the slot is simply left available.
/// </summary>
public sealed class SwapMonitorJob
{
    private readonly ApplicationDbContext          _db;
    private readonly IEmailService                 _emailService;
    private readonly ICacheService                 _cache;
    private readonly IOptions<AppSettings>         _appSettings;
    private readonly ILogger<SwapMonitorJob>       _logger;

    public SwapMonitorJob(
        ApplicationDbContext    db,
        IEmailService           emailService,
        ICacheService           cache,
        IOptions<AppSettings>   appSettings,
        ILogger<SwapMonitorJob> logger)
    {
        _db           = db;
        _emailService = emailService;
        _cache        = cache;
        _appSettings  = appSettings;
        _logger       = logger;
    }

    /// <summary>
    /// Processes a released slot: finds the next patient in the waitlist and
    /// issues a time-limited swap offer.
    /// </summary>
    /// <param name="releasedSlotId">ID of the slot that was freed by a cancellation.</param>
    /// <param name="cancellationToken">Hangfire-supplied shutdown token.</param>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 60, 120])]
    public async Task ExecuteAsync(int releasedSlotId, IJobCancellationToken cancellationToken)
    {
        var slot = await _db.Slots.FirstOrDefaultAsync(s => s.Id == releasedSlotId);
        if (slot is null)
        {
            _logger.LogWarning("SwapMonitorJob: slot {SlotId} not found — nothing to do.", releasedSlotId);
            return;
        }

        // AC-002 — find oldest Active entry: prefer entries wanting this specific slot,
        // then fall back to open entries (PreferredSlotId == null).
        // Two queries avoids a client-eval warning for the date-based subquery.
        var entry = await _db.WaitlistEntries
            .Where(w => w.Status == WaitlistStatus.Active && w.PreferredSlotId == releasedSlotId)
            .OrderBy(w => w.QueuedAt)
            .FirstOrDefaultAsync();

        entry ??= await _db.WaitlistEntries
            .Where(w => w.Status == WaitlistStatus.Active && w.PreferredSlotId == null)
            .OrderBy(w => w.QueuedAt)
            .FirstOrDefaultAsync();

        if (entry is null)
        {
            // F1 fix: no waitlist entry — release the slot now (cancel held it unavailable
            // until the job confirms no patient is waiting).
            slot.IsAvailable = true;
            await _db.SaveChangesAsync();
            var noEntryKey = $"slots:date:{DateOnly.FromDateTime(slot.SlotTime):yyyy-MM-dd}";
            await _cache.DeleteAsync(noEntryKey);
            _logger.LogInformation(
                "SwapMonitorJob: slot {SlotId} released — no Active waitlist entries; slot made available.",
                releasedSlotId);
            return;
        }

        // AC-003 — set offer expiry based on configured window.
        var windowHours = _appSettings.Value.SwapOfferWindowHours;
        entry.Status         = WaitlistStatus.OfferSent;
        entry.OfferExpiresAt = DateTime.UtcNow.AddHours(windowHours);
        entry.OfferedSlotId  = releasedSlotId;

        // Slot stays unavailable while offer is pending (defensive assignment — already false).
        slot.IsAvailable = false;

        // F2 fix: send email BEFORE committing DB state. If email throws, SaveChangesAsync is
        // never called, entry remains Active, and Hangfire's retry re-processes cleanly.
        var patient = await _db.UserAccounts.FindAsync(entry.PatientId);
        if (patient is not null)
        {
            await _emailService.SendAsync(
                toEmail:  patient.Email,
                subject:  "Appointment Slot Available — Action Required",
                htmlBody: $"<p>A slot is now available on {slot.SlotTime:f}. "
                        + $"You have {windowHours} hour(s) to accept. "
                        + $"Use <code>POST /waitlist/{entry.Id}/accept</code> to confirm.</p>");
        }
        else
        {
            _logger.LogWarning("SwapMonitorJob: patient {PatientId} not found — skipping email.", entry.PatientId);
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "SwapMonitorJob: swap offer sent to patient {PatientId} for slot {SlotId}; expires at {ExpiresAt:O}.",
            entry.PatientId, releasedSlotId, entry.OfferExpiresAt);
    }
}
