using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ClinicalHealthcare.Infrastructure.PgMigrations.Migrations
{
    /// <inheritdoc />
    public partial class ClinicalEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConflictFlags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    FieldName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Value1 = table.Column<string>(type: "text", nullable: false),
                    Value2 = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ResolvedByStaffId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConflictFlags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExtractedClinicalFields",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    DocumentId = table.Column<int>(type: "integer", nullable: false),
                    FieldType = table.Column<int>(type: "integer", nullable: false),
                    FieldName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FieldValue = table.Column<string>(type: "text", nullable: false),
                    ConfidenceScore = table.Column<double>(type: "double precision", nullable: false),
                    ExtractionJobId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExtractedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtractedClinicalFields", x => x.Id);
                    table.CheckConstraint("CK_ExtractedClinicalFields_ConfidenceScore", "\"ConfidenceScore\" >= 0.0 AND \"ConfidenceScore\" <= 1.0");
                });

            migrationBuilder.CreateTable(
                name: "MedicalCodeSuggestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    CodeType = table.Column<int>(type: "integer", nullable: false),
                    SuggestedCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CommittedCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CodeDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ConfidenceScore = table.Column<double>(type: "double precision", nullable: false),
                    LowConfidenceFlag = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    VerifiedById = table.Column<int>(type: "integer", nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicalCodeSuggestions", x => x.Id);
                    table.CheckConstraint("CK_MedicalCodeSuggestions_ConfidenceScore", "\"ConfidenceScore\" >= 0.0 AND \"ConfidenceScore\" <= 1.0");
                    table.CheckConstraint("CK_MedicalCodeSuggestions_TrustFirst", "\"Status\" != 1 OR \"VerifiedById\" IS NOT NULL");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConflictFlags_PatientId",
                table: "ConflictFlags",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_ExtractedClinicalFields_DocumentId",
                table: "ExtractedClinicalFields",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ExtractedClinicalFields_PatientId",
                table: "ExtractedClinicalFields",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicalCodeSuggestions_PatientId",
                table: "MedicalCodeSuggestions",
                column: "PatientId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConflictFlags");

            migrationBuilder.DropTable(
                name: "ExtractedClinicalFields");

            migrationBuilder.DropTable(
                name: "MedicalCodeSuggestions");
        }
    }
}
