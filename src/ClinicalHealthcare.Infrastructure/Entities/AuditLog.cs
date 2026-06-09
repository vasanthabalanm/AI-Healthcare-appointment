namespace ClinicalHealthcare.Infrastructure.Entities;

/// <summary>
/// Append-only audit record for every data mutation on PHI entities.
///
/// INSERT-only enforcement: UPDATE and DELETE permissions are revoked at the
/// SQL Server GRANT level in the <c>AuditLogPhiRetention</c> migration.
/// No application-layer delete or update paths exist.
///
/// Cross-entity references (EntityId, ActorId) are logical — no FK constraints
/// are created so that audit records outlive the entities they reference.
/// </summary>
public sealed class AuditLog
{
    public int Id { get; set; }

    /// <summary>Name of the entity type affected, e.g. "UserAccount", "ClinicalDocument".</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Primary key of the affected entity row.</summary>
    public int EntityId { get; set; }

    /// <summary>UserAccount.Id of the actor who triggered the change. Null for system actions.</summary>
    public int? ActorId { get; set; }

    /// <summary>Action performed: "INSERT", "UPDATE", "DELETE", etc.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>JSON snapshot of the entity state before the change. Null for INSERT actions.</summary>
    public string? BeforeValue { get; set; }

    /// <summary>JSON snapshot of the entity state after the change. Null for DELETE actions.</summary>
    public string? AfterValue { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>Optional trace/correlation ID for distributed tracing. Null when not available.</summary>
    public string? CorrelationId { get; set; }
}
