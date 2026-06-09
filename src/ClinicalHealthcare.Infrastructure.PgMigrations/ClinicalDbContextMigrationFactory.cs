using ClinicalHealthcare.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClinicalHealthcare.Infrastructure.PgMigrations;

/// <summary>
/// Design-time factory used by EF Core tooling to create <see cref="ClinicalDbContext"/>
/// when running <c>dotnet ef migrations add</c> from this project.
/// </summary>
public class ClinicalDbContextMigrationFactory : IDesignTimeDbContextFactory<ClinicalDbContext>
{
    public ClinicalDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
            ?? "Host=localhost;Database=clinical_dev;Username=postgres;Password=REPLACE_ME_DEV_ONLY";

        var options = new DbContextOptionsBuilder<ClinicalDbContext>()
            .UseNpgsql(connectionString,
                npgsql => npgsql.MigrationsAssembly("ClinicalHealthcare.Infrastructure.PgMigrations"))
            .Options;

        return new ClinicalDbContext(options);
    }
}
