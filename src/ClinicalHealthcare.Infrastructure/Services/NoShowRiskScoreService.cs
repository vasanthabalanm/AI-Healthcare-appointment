using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClinicalHealthcare.Infrastructure.Services;

/// <summary>
/// Default implementation of <see cref="INoShowRiskScoreService"/>.
/// Requires a scoped <see cref="ApplicationDbContext"/>.
/// </summary>
public sealed class NoShowRiskScoreService : INoShowRiskScoreService
{
    private readonly ApplicationDbContext _db;

    public NoShowRiskScoreService(ApplicationDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<int> CalculateAsync(int patientId, DateTime slotTime, CancellationToken cancellationToken = default)
    {
        // Component 1 — prior no-shows: +20 per occurrence, max 60.
        var noShowCount = await _db.Appointments
            .CountAsync(a => a.PatientId == patientId && a.Status == AppointmentStatus.NoShow, cancellationToken);
        var noShowScore = Math.Min(noShowCount * 20, 60);

        // Component 2 — lead time risk.
        var leadHours = (slotTime - DateTime.UtcNow).TotalHours;
        var leadScore = leadHours < 24 ? 30
                      : leadHours <= 72 ? 15
                      : 0;

        // Component 3 — intake completion: +10 if patient has no intake record at all.
        // IntakeRecords DbSet has a global query filter for IsLatest = true.
        var hasIntake = await _db.IntakeRecords
            .AnyAsync(i => i.PatientId == patientId, cancellationToken);
        var intakeScore = hasIntake ? 0 : 10;

        return Math.Min(noShowScore + leadScore + intakeScore, 100);
    }
}
