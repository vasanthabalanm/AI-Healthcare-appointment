using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace ClinicalHealthcare.Infrastructure.Data;

/// <summary>
/// Client-side value generator for the <c>RowVersion</c> (bytea) concurrency token.
/// EF Core's <c>IsRowVersion()</c> marks the property as <c>ValueGeneratedOnAddOrUpdate</c>
/// and expects the database to produce the value. PostgreSQL has no built-in auto-generating
/// bytea trigger, so this generator produces a fresh random 8-byte token client-side before
/// every INSERT and UPDATE, keeping the optimistic-concurrency contract intact.
/// </summary>
internal sealed class RowVersionGenerator : ValueGenerator<byte[]>
{
    public override bool GeneratesTemporaryValues => false;

    public override byte[] Next(EntityEntry entry)
        => Guid.NewGuid().ToByteArray()[..8];
}
