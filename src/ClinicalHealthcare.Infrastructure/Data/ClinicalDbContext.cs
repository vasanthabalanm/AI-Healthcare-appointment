using ClinicalHealthcare.Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClinicalHealthcare.Infrastructure.Data;

/// <summary>
/// PostgreSQL DbContext — clinical data (intake forms, clinical documents, coding records).
/// Connection string is sourced exclusively from the POSTGRES_CONNECTION_STRING environment variable.
/// </summary>
public class ClinicalDbContext : DbContext
{
    public DbSet<ExtractedClinicalField> ExtractedClinicalFields => Set<ExtractedClinicalField>();
    public DbSet<ConflictFlag>           ConflictFlags           => Set<ConflictFlag>();
    public DbSet<MedicalCodeSuggestion>  MedicalCodeSuggestions  => Set<MedicalCodeSuggestion>();

    public ClinicalDbContext(DbContextOptions<ClinicalDbContext> options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── ExtractedClinicalField ────────────────────────────────────────────
        modelBuilder.Entity<ExtractedClinicalField>(e =>
        {
            e.HasKey(f => f.Id);
            e.Property(f => f.FieldName).HasMaxLength(200).IsRequired();
            e.Property(f => f.FieldValue).IsRequired();
            e.Property(f => f.ExtractionJobId).HasMaxLength(100).IsRequired();
            e.Property(f => f.FieldType).HasConversion<int>().IsRequired();
            e.Property(f => f.ExtractedAt).HasDefaultValueSql("NOW()");

            // Soft-delete: default false; no hard deletes
            e.Property(f => f.IsDeleted).HasDefaultValue(false);

            // Soft-delete query filter — excludes IsDeleted records from all default queries.
            // Use .IgnoreQueryFilters() to access soft-deleted records (e.g. audit/admin views).
            e.HasQueryFilter(f => !f.IsDeleted);

            // AC-005 — confidence_score must be within [0.0, 1.0]
            // Column name is PascalCase ("ConfidenceScore") because UseSnakeCaseNamingConvention is not configured.
            e.ToTable("ExtractedClinicalFields", t => t.HasCheckConstraint(
                "CK_ExtractedClinicalFields_ConfidenceScore",
                "\"ConfidenceScore\" >= 0.0 AND \"ConfidenceScore\" <= 1.0"));

            e.HasIndex(f => f.PatientId).HasDatabaseName("IX_ExtractedClinicalFields_PatientId");
            e.HasIndex(f => f.DocumentId).HasDatabaseName("IX_ExtractedClinicalFields_DocumentId");
        });

        // ── ConflictFlag ──────────────────────────────────────────────────────
        modelBuilder.Entity<ConflictFlag>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.FieldName).HasMaxLength(200).IsRequired();
            e.Property(c => c.Value1).IsRequired();
            e.Property(c => c.Value2).IsRequired();
            e.Property(c => c.Status).HasConversion<int>().IsRequired();
            e.Property(c => c.CreatedAt).HasDefaultValueSql("NOW()");

            e.HasIndex(c => c.PatientId).HasDatabaseName("IX_ConflictFlags_PatientId");

            // Optimistic concurrency: map PostgreSQL system column xmin as a shadow
            // concurrency token. xmin is always present on every PG table — no migration
            // required. Prevents TOCTOU race on concurrent resolve/dismiss (TASK_043).
            e.Property<uint>("xmin")
             .HasColumnName("xmin")
             .ValueGeneratedOnAddOrUpdate()
             .IsConcurrencyToken();
        });

        // ── MedicalCodeSuggestion ─────────────────────────────────────────────
        modelBuilder.Entity<MedicalCodeSuggestion>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.SuggestedCode).HasMaxLength(20).IsRequired();
            e.Property(m => m.CommittedCode).HasMaxLength(20);
            e.Property(m => m.CodeDescription).HasMaxLength(500).IsRequired();
            e.Property(m => m.CodeType).HasConversion<int>().IsRequired();
            e.Property(m => m.Status).HasConversion<int>().IsRequired();

            // AC-005 — confidence_score must be within [0.0, 1.0]
            // AC-004 — Trust-First: when status = Accepted (1), verified_by must be set
            // Column names are PascalCase ("ConfidenceScore", "Status", "VerifiedById") per Npgsql default convention.
            e.ToTable("MedicalCodeSuggestions", t =>
            {
                t.HasCheckConstraint(
                    "CK_MedicalCodeSuggestions_ConfidenceScore",
                    "\"ConfidenceScore\" >= 0.0 AND \"ConfidenceScore\" <= 1.0");
                t.HasCheckConstraint(
                    "CK_MedicalCodeSuggestions_TrustFirst",
                    "\"Status\" != 1 OR \"VerifiedById\" IS NOT NULL");
            });

            e.HasIndex(m => m.PatientId).HasDatabaseName("IX_MedicalCodeSuggestions_PatientId");
        });
    }
}
