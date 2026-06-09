using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ClinicalHealthcare.Infrastructure.Interceptors;

/// <summary>
/// EF Core <see cref="SaveChangesInterceptor"/> that enforces the <see cref="Appointment"/>
/// finite-state machine (FSM) before any save operation reaches the database.
///
/// Valid transitions:
/// <list type="table">
///   <item><term>Scheduled → Arrived</term></item>
///   <item><term>Scheduled → Cancelled</term></item>
///   <item><term>Scheduled → NoShow</term></item>
///   <item><term>Arrived   → Completed</term></item>
/// </list>
///
/// Any other transition throws <see cref="InvalidOperationException"/> and the save is aborted.
/// </summary>
public sealed class AppointmentFsmInterceptor : SaveChangesInterceptor
{
    /// <summary>
    /// Allowed FSM transitions: key = current (original) status, value = set of permitted next states.
    /// </summary>
    private static readonly Dictionary<AppointmentStatus, HashSet<AppointmentStatus>> ValidTransitions =
        new()
        {
            [AppointmentStatus.Scheduled] = [
                AppointmentStatus.Arrived,
                AppointmentStatus.Cancelled,
                AppointmentStatus.NoShow
            ],
            [AppointmentStatus.Arrived] = [
                AppointmentStatus.Completed
            ]
        };

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ValidateAppointmentTransitions(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ValidateAppointmentTransitions(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private static void ValidateAppointmentTransitions(DbContext? context)
    {
        if (context is null)
            return;

        foreach (var entry in context.ChangeTracker.Entries<Appointment>())
        {
            if (entry.State != EntityState.Modified)
                continue;

            var originalStatus = (AppointmentStatus)entry.OriginalValues[nameof(Appointment.Status)]!;
            var newStatus      = entry.Entity.Status;

            if (originalStatus == newStatus)
                continue;

            if (!ValidTransitions.TryGetValue(originalStatus, out var allowed) ||
                !allowed.Contains(newStatus))
            {
                throw new InvalidOperationException(
                    $"Invalid Appointment status transition: {originalStatus} → {newStatus}. " +
                    $"Allowed from {originalStatus}: [{string.Join(", ", ValidTransitions.GetValueOrDefault(originalStatus) ?? [])}].");
            }
        }
    }
}
