using ClinicalHealthcare.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClinicalHealthcare.Infrastructure.PgMigrations;

/// <summary>
/// Design-time factory used by EF Core tooling to create <see cref="ApplicationDbContext"/>
/// when running <c>dotnet ef migrations add</c> from this project.
///
/// Migrations are stored in <c>Migrations/Application/</c> to keep them separate from
/// the <see cref="ClinicalDbContext"/> migrations in <c>Migrations/</c>.
/// A distinct history table (<c>__EFMigrationsHistory_App</c>) prevents collision.
/// </summary>
public class ApplicationDbContextMigrationFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
            ?? "Host=localhost;Database=clinical_dev;Username=postgres;Password=admin";

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString,
                npgsql => npgsql
                    .MigrationsAssembly("ClinicalHealthcare.Infrastructure.PgMigrations")
                    .MigrationsHistoryTable("__EFMigrationsHistory_App"))
            .Options;

        return new ApplicationDbContext(options);
    }
}
