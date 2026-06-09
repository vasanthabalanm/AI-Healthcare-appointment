using ClinicalHealthcare.Infrastructure.Entities;

namespace ClinicalHealthcare.Infrastructure.Services;

/// <summary>
/// Provides conflict-related query helpers for use by other features.
/// </summary>
public interface IConflictService
{
    /// <summary>
    /// Returns <see langword="true"/> when the patient has at least one
    /// <see cref="ConflictFlag"/> with status <see cref="ConflictFlagStatus.Unresolved"/>.
    /// </summary>
    Task<bool> HasUnresolvedConflictsAsync(int patientId, CancellationToken ct = default);

    /// <summary>
    /// Returns the number of <see cref="ConflictFlag"/> rows with status
    /// <see cref="ConflictFlagStatus.Unresolved"/> for the given patient.
    /// Returns 0 when there are none.
    /// </summary>
    Task<int> GetUnresolvedCountAsync(int patientId, CancellationToken ct = default);
}
