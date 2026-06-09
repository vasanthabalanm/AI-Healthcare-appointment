using ClinicalHealthcare.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClinicalHealthcare.Infrastructure.SqlMigrations;

/// <summary>
/// Design-time factory used by EF Core tooling to create <see cref="ApplicationDbContext"/>
/// when running <c>dotnet ef migrations add</c> from this project.
/// </summary>
public class ApplicationDbContextMigrationFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("SQLSERVER_CONNECTION_STRING")
            ?? "Server=(localdb)\\mssqllocaldb;Database=ClinicalHealthcare_Dev;Trusted_Connection=True;";

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString,
                sql => sql.MigrationsAssembly("ClinicalHealthcare.Infrastructure.SqlMigrations"))
            .Options;

        return new ApplicationDbContext(options);
    }
}
