using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicalHealthcare.Infrastructure.Services;

/// <summary>
/// Default implementation of <see cref="IInsurancePreCheckService"/>.
///
/// Performs a soft lookup against the <c>InsuranceReference</c> reference table.
/// All exceptions are caught and logged at WARNING level; the caller always receives
/// a non-throwing result (AC-001 — intake is never blocked by pre-check failure).
/// </summary>
public sealed class InsurancePreCheckService : IInsurancePreCheckService
{
    private readonly ApplicationDbContext             _db;
    private readonly ILogger<InsurancePreCheckService> _logger;

    public InsurancePreCheckService(ApplicationDbContext db, ILogger<InsurancePreCheckService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<InsuranceStatus> CheckAsync(string? insurerId, string? planCode, CancellationToken ct = default)
    {
        // AC-004: empty / missing fields → Skipped; no DB round-trip needed.
        if (string.IsNullOrWhiteSpace(insurerId) || string.IsNullOrWhiteSpace(planCode))
            return InsuranceStatus.Skipped;

        // Normalise: trim whitespace and upper-case for case-insensitive comparison.
        var normInsurerId = insurerId.Trim().ToUpperInvariant();
        var normPlanCode  = planCode.Trim().ToUpperInvariant();

        try
        {
            // AC-003: look up by InsurerId + PlanCode; must be active.
            var found = await _db.InsuranceReferences
                .AnyAsync(r => r.InsurerId.ToUpper() == normInsurerId
                            && r.PlanCode.ToUpper()  == normPlanCode
                            && r.IsActive, ct);

            return found ? InsuranceStatus.Validated : InsuranceStatus.NotVerified;
        }
        catch (Exception ex)
        {
            // Edge case: unexpected DB error — log WARNING and fall back to NotVerified.
            // Intake must not be blocked (AC-001).
            _logger.LogWarning(ex,
                "Insurance pre-check failed for InsurerId={InsurerId} PlanCode={PlanCode}. " +
                "Defaulting to NotVerified.",
                insurerId, planCode);

            return InsuranceStatus.NotVerified;
        }
    }
}
