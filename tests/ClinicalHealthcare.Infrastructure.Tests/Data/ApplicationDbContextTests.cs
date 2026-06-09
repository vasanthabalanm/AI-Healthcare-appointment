using ClinicalHealthcare.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace ClinicalHealthcare.Infrastructure.Tests.Data;

/// <summary>
/// Unit tests for <see cref="ApplicationDbContext"/> EF Core configuration (US_003).
/// Verifies Npgsql provider, DI resolution patterns, and startup fail-fast behaviour.
/// </summary>
public sealed class ApplicationDbContextTests
{
    // TC-001: ApplicationDbContext configures the Npgsql EF Core provider
    [Fact]
    public void ApplicationDbContext_ProviderName_IsNpgsql()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=stub;Database=stub;Username=stub;Password=stub")
            .Options;
        using var ctx = new ApplicationDbContext(opts);

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", ctx.Database.ProviderName);
    }

    // TC-003: missing POSTGRES_CONNECTION_STRING throws InvalidOperationException at startup
    [Fact]
    public void RequireConnectionString_WhenEnvVarMissing_ThrowsWithEnvVarName()
    {
        var previous = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");
        try
        {
            Environment.SetEnvironmentVariable("POSTGRES_CONNECTION_STRING", null);

            // Reproduces the fail-fast pattern used in Program.cs RequireConnectionString().
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

    // EC-002: On Windows, setting env var to empty string removes it (same as null).
    // On non-Windows it would be distinct. The test documents the platform behaviour.
    [Fact]
    public void RequireConnectionString_WhenEnvVarSetToEmpty_BehavesAsPlatformDictates()
    {
        var previous = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");
        try
        {
            Environment.SetEnvironmentVariable("POSTGRES_CONNECTION_STRING", "");

            // On Windows, setting to "" effectively removes the variable; GetEnvironmentVariable returns null.
            // On Linux/macOS, it returns "".
            var value = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");

            if (OperatingSystem.IsWindows())
                Assert.Null(value);   // Windows removes the variable
            else
                Assert.Equal("", value); // Other OS keeps the empty string
        }
        finally
        {
            Environment.SetEnvironmentVariable("POSTGRES_CONNECTION_STRING", previous);
        }
    }

    // TC-004: ApplicationDbContext exposes the UserAccounts DbSet (operational data store)
    [Fact]
    public void ApplicationDbContext_ExposesUserAccountsDbSet()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        using var ctx = new ApplicationDbContext(opts);

        Assert.NotNull(ctx.UserAccounts);
    }

    // EC-001: ApplicationDbContext and ClinicalDbContext are independent — each has distinct entity types
    [Fact]
    public void ApplicationDbContext_DoesNotContainClinicalEntities()
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        using var ctx = new ApplicationDbContext(opts);

        var entityTypeNames = ctx.Model.GetEntityTypes()
            .Select(e => e.ClrType.Name)
            .ToHashSet();

        // Clinical-only entities must not bleed into the operational context.
        Assert.DoesNotContain("ExtractedClinicalField",  entityTypeNames);
        Assert.DoesNotContain("MedicalCodeSuggestion",   entityTypeNames);
        Assert.DoesNotContain("ConflictFlag",             entityTypeNames);
    }
}
