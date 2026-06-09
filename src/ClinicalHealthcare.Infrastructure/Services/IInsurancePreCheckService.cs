using ClinicalHealthcare.Infrastructure.Entities;

namespace ClinicalHealthcare.Infrastructure.Services;

/// <summary>
/// Soft insurance pre-check service (AC-003 / TASK_032).
/// Non-blocking — always returns a status; never throws to the caller.
/// </summary>
public interface IInsurancePreCheckService
{
    /// <summary>
    /// Checks <paramref name="insurerId"/> + <paramref name="planCode"/> against the
    /// <c>InsuranceReference</c> reference table.
    /// </summary>
    /// <param name="insurerId">Payer/insurer identifier from the intake request.</param>
    /// <param name="planCode">Insurance plan code from the intake request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="InsuranceStatus.Skipped"/> if either input is null or whitespace;
    /// <see cref="InsuranceStatus.Validated"/> if an active matching row is found;
    /// <see cref="InsuranceStatus.NotVerified"/> otherwise.
    /// </returns>
    Task<InsuranceStatus> CheckAsync(string? insurerId, string? planCode, CancellationToken ct = default);
}
