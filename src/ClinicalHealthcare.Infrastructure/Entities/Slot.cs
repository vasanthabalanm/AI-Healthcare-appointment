using System.ComponentModel.DataAnnotations;

namespace ClinicalHealthcare.Infrastructure.Entities;

/// <summary>
/// Represents a bookable appointment slot.
/// <see cref="RowVersion"/> is a SQL Server <c>rowversion</c> column used as an
/// optimistic-concurrency token to prevent double-booking races.
/// </summary>
public sealed class Slot
{
    public int Id { get; set; }

    public DateTime SlotTime { get; set; }

    public int DurationMinutes { get; set; }

    public bool IsAvailable { get; set; } = true;

    /// <summary>
    /// Optimistic-concurrency token mapped to a SQL Server <c>rowversion</c> column.
    /// EF Core automatically checks this on every UPDATE/DELETE.
    /// </summary>
    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
