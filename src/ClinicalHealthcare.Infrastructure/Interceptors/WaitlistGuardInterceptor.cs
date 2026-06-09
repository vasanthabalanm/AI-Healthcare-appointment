using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ClinicalHealthcare.Infrastructure.Interceptors;

/// <summary>
/// EF Core <see cref="SaveChangesInterceptor"/> that prevents a patient from
/// having more than one <see cref="WaitlistStatus.Active"/> <see cref="WaitlistEntry"/>
/// at any time.
///
/// The DB-level enforcement is a filtered unique index
/// <c>UIX_WaitlistEntries_PatientId_Active (WHERE [Status] = 0)</c>.
/// This interceptor provides a clean <see cref="InvalidOperationException"/> before
/// the database roundtrip — consistent with the <see cref="AppointmentFsmInterceptor"/>
/// pattern and testable with the EF Core InMemory provider.
/// </summary>
public sealed class WaitlistGuardInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ValidateSingleActivePerPatient(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ValidateSingleActivePerPatient(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private static void ValidateSingleActivePerPatient(DbContext? context)
    {
        if (context is null)
            return;

        // Collect PatientIds of new Active entries being inserted
        var incomingActivePatientIds = context.ChangeTracker
            .Entries<WaitlistEntry>()
            .Where(e => e.State == EntityState.Added &&
                        e.Entity.Status == WaitlistStatus.Active)
            .Select(e => e.Entity.PatientId)
            .ToHashSet();

        if (incomingActivePatientIds.Count == 0)
            return;

        // Check whether any of these patients already have an Active entry
        // in the change tracker (either unchanged tracked or another added entry)
        foreach (var patientId in incomingActivePatientIds)
        {
            var existingActive = context.ChangeTracker
                .Entries<WaitlistEntry>()
                .Where(e => e.Entity.PatientId == patientId &&
                            e.Entity.Status    == WaitlistStatus.Active &&
                            e.State            != EntityState.Deleted)
                .Skip(1) // one Active entry is allowed — it's the one being added
                .Any();

            if (existingActive)
            {
                throw new InvalidOperationException(
                    $"Patient {patientId} already has an Active waitlist entry. " +
                    "A patient may only have one Active waitlist entry at a time.");
            }
        }
    }
}
