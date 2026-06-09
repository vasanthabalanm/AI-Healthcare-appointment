using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClinicalHealthcare.Infrastructure.Services;

/// <summary>
/// EF Core implementation of <see cref="IConflictService"/> backed by PostgreSQL.
/// </summary>
public sealed class ConflictService : IConflictService
{
    private readonly ClinicalDbContext _db;

    public ConflictService(ClinicalDbContext db) => _db = db;

    /// <inheritdoc/>
    public Task<bool> HasUnresolvedConflictsAsync(int patientId, CancellationToken ct = default)
        => _db.ConflictFlags.AnyAsync(
            c => c.PatientId == patientId && c.Status == ConflictFlagStatus.Unresolved, ct);

    /// <inheritdoc/>
    public Task<int> GetUnresolvedCountAsync(int patientId, CancellationToken ct = default)
        => _db.ConflictFlags.CountAsync(
            c => c.PatientId == patientId && c.Status == ConflictFlagStatus.Unresolved, ct);
}
