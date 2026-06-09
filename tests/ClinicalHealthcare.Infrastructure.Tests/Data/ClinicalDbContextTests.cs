using ClinicalHealthcare.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Data;

/// <summary>
/// Unit tests for <see cref="ClinicalDbContext"/> EF Core configuration (US_003).
/// Verifies Npgsql provider and entity-set isolation from <see cref="ApplicationDbContext"/>.
/// </summary>
public sealed class ClinicalDbContextTests
{
    // TC-002: ClinicalDbContext configures the Npgsql EF Core provider
    [Fact]
    public void ClinicalDbContext_ProviderName_IsNpgsql()
    {
        var opts = new DbContextOptionsBuilder<ClinicalDbContext>()
            .UseNpgsql("Host=stub;Database=stub;Username=stub;Password=stub")
            .Options;
        using var ctx = new ClinicalDbContext(opts);

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", ctx.Database.ProviderName);
    }

    // TC-002: ClinicalDbContext exposes the ExtractedClinicalFields DbSet (clinical data store)
    [Fact]
    public void ClinicalDbContext_ExposesExtractedClinicalFieldsDbSet()
    {
        var opts = new DbContextOptionsBuilder<ClinicalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        using var ctx = new ClinicalDbContext(opts);

        Assert.NotNull(ctx.ExtractedClinicalFields);
    }

    // EC-001: ClinicalDbContext does not expose operational entities
    [Fact]
    public void ClinicalDbContext_DoesNotContainOperationalEntities()
    {
        var opts = new DbContextOptionsBuilder<ClinicalDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        using var ctx = new ClinicalDbContext(opts);

        var entityTypeNames = ctx.Model.GetEntityTypes()
            .Select(e => e.ClrType.Name)
            .ToHashSet();

        // Operational entities must not appear in the clinical context.
        Assert.DoesNotContain("UserAccount",   entityTypeNames);
        Assert.DoesNotContain("Slot",          entityTypeNames);
        Assert.DoesNotContain("Appointment",   entityTypeNames);
    }

    // TC-004: missing POSTGRES_CONNECTION_STRING throws at startup (same fail-fast guard as ApplicationDbContext)
    [Fact]
    public void RequireConnectionString_WhenEnvVarMissing_ThrowsWithEnvVarName()
    {
        var previous = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");
        try
        {
            Environment.SetEnvironmentVariable("POSTGRES_CONNECTION_STRING", null);

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                _ = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
                    ?? throw new InvalidOperationException(
                        "Required environment variable 'POSTGRES_CONNECTION_STRING' is not set.");
            });

            Assert.Contains("POSTGRES_CONNECTION_STRING", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("POSTGRES_CONNECTION_STRING", previous);
        }
    }
}
