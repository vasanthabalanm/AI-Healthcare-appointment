using ClinicalHealthcare.Api.Abstractions;
using ClinicalHealthcare.Infrastructure.Cache;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using ClinicalHealthcare.Infrastructure.Jobs;
using Hangfire;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;

namespace ClinicalHealthcare.Api.Features.Appointments;

/// <summary>
/// Vertical-slice endpoint: GET /appointments/cancel?token=...
///
/// Allows a patient to cancel their appointment by following the single-use
/// cancellation link included in the T-48 h reminder email (TASK_028).
///
/// This endpoint is AllowAnonymous — it is accessed directly from the email link
/// without a JWT. The cancellation link token provides the required authentication.
///
/// Security:
///   - Token is cryptographically random (256-bit entropy) and short-lived (48 h).
///   - Only the SHA-256 hash of the token is stored in the DB.
///   - Token is single-use; <see cref="Appointment.CancellationLinkUsed"/> is set on first use.
///   - Constant-time hash comparison is achieved by DB lookup (no timing oracle).
///
/// Edge cases:
///   - Token already used → 400 {"error":"Cancellation link already used."}
///   - Token expired (>48h) → 400 {"error":"Cancellation link has expired."}
///   - Appointment not Scheduled → 400 {"error":"Appointment already cancelled or completed."}
///   - Token not found in DB → 400 {"error":"Invalid cancellation token."}
/// </summary>
public sealed class CancelByLinkEndpoint : IEndpointDefinition
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/appointments/cancel", HandleCancelByLink)
           .AllowAnonymous()
           .WithName("CancelAppointmentByLink")
           .WithTags("Appointments")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status400BadRequest);
    }

    // ── GET /appointments/cancel?token=... ────────────────────────────────────

    public static async Task<IResult> HandleCancelByLink(
        string               token,
        ApplicationDbContext db,
        ICacheService        cache,
        IBackgroundJobClient jobs,
        CancellationToken    ct)
    {
        // ── 1. Basic input guard ──────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(token))
            return Results.BadRequest(new { error = "Cancellation token is required." });

        // ── 2. Hash the incoming token and look up by hash ────────────────────
        // Never compare raw tokens — only the SHA-256 hash is stored in the DB.
        var tokenHash  = SendEmailReminderJob.ComputeSha256Hex(token);
        var appointment = await db.Appointments
            .Include(a => a.Slot)
            .FirstOrDefaultAsync(a => a.CancellationLinkTokenHash == tokenHash, ct);

        if (appointment is null)
            return Results.BadRequest(new { error = "Invalid cancellation token." });

        // ── 3. Validate token state ───────────────────────────────────────────
        if (appointment.CancellationLinkUsed)
            return Results.BadRequest(new { error = "Cancellation link already used." });

        if (appointment.CancellationLinkExpiry is null ||
            appointment.CancellationLinkExpiry < DateTime.UtcNow)
            return Results.BadRequest(new { error = "Cancellation link has expired." });

        if (appointment.Status != AppointmentStatus.Scheduled)
            return Results.BadRequest(new { error = "Appointment already cancelled or completed." });

        // ── 4. Consume token (AC-003) ─────────────────────────────────────────
        appointment.CancellationLinkUsed = true;

        // ── 5. Delete any pending Hangfire reminder/SMS jobs (AC-004) ─────────
        if (!string.IsNullOrWhiteSpace(appointment.ReminderJobId))
            jobs.ChangeState(appointment.ReminderJobId, new DeletedState(), null);
        if (!string.IsNullOrWhiteSpace(appointment.EmailReminderJobId))
            jobs.ChangeState(appointment.EmailReminderJobId, new DeletedState(), null);
        if (!string.IsNullOrWhiteSpace(appointment.SmsReminderJobId48h))
            jobs.ChangeState(appointment.SmsReminderJobId48h, new DeletedState(), null);
        if (!string.IsNullOrWhiteSpace(appointment.SmsReminderJobId2h))
            jobs.ChangeState(appointment.SmsReminderJobId2h, new DeletedState(), null);

        // ── 6. Cancel the appointment ─────────────────────────────────────────
        appointment.Status = AppointmentStatus.Cancelled;
        await db.SaveChangesAsync(ct);

        // ── 7. Offer freed slot to waitlist via SwapMonitorJob ────────────────
        jobs.Enqueue<SwapMonitorJob>(j => j.ExecuteAsync(appointment.SlotId, null!));

        // Invalidate slot cache so GET /slots reflects the newly available slot.
        if (appointment.Slot is not null)
        {
            var dateKey = $"{GetSlotsEndpoint.CacheKeyPrefix}{DateOnly.FromDateTime(appointment.Slot.SlotTime):yyyy-MM-dd}";
            await cache.DeleteAsync(dateKey, ct);
        }

        return Results.Ok(new { message = "Appointment successfully cancelled." });
    }
}
