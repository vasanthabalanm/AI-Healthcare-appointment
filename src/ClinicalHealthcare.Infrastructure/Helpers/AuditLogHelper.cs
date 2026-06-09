using System.Text.Json;
using ClinicalHealthcare.Infrastructure.Data;
using ClinicalHealthcare.Infrastructure.Entities;

namespace ClinicalHealthcare.Infrastructure.Helpers;

/// <summary>
/// Append-only audit log helper. Stages a new <see cref="AuditLog"/> entry in
/// the DbContext change-tracker without calling <c>SaveChanges</c> — callers
/// commit the entry atomically alongside the entity change in a single save.
/// </summary>
public static class AuditLogHelper
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Stages an audit log entry for <paramref name="action"/> on <paramref name="entityType"/>.
    /// </summary>
    /// <param name="db">The active <see cref="ApplicationDbContext"/>.</param>
    /// <param name="entityType">Entity type name, e.g. "UserAccount".</param>
    /// <param name="entityId">Primary key of the affected row.</param>
    /// <param name="actorId">UserAccount.Id of the acting user; null for system actions.</param>
    /// <param name="action">"INSERT", "UPDATE", or "DELETE".</param>
    /// <param name="before">Entity state before the change; null for inserts.</param>
    /// <param name="after">Entity state after the change; null for deletes.</param>
    /// <param name="correlationId">Optional request correlation ID.</param>
    public static void Stage(
        ApplicationDbContext db,
        string entityType,
        int entityId,
        int? actorId,
        string action,
        object? before,
        object? after,
        string? correlationId = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            EntityType    = entityType,
            EntityId      = entityId,
            ActorId       = actorId,
            Action        = action,
            BeforeValue   = before is null ? null : JsonSerializer.Serialize(before, _jsonOptions),
            AfterValue    = after  is null ? null : JsonSerializer.Serialize(after,  _jsonOptions),
            OccurredAt    = DateTime.UtcNow,
            CorrelationId = correlationId
        });
    }
}
