using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicalHealthcare.Infrastructure.SqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AuditLogPhiRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "WaitlistEntries",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetainUntil",
                table: "WaitlistEntries",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "UserAccounts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetainUntil",
                table: "UserAccounts",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "IntakeRecords",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetainUntil",
                table: "IntakeRecords",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "ClinicalDocuments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetainUntil",
                table: "ClinicalDocuments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BeforeValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_OccurredAt",
                table: "AuditLogs",
                column: "OccurredAt");

            // AC-001 — AuditLog is INSERT-only. Revoke UPDATE and DELETE from the
            // public database role so no user can mutate or remove audit records.
            // In production, replace [public] with the specific application DB login/role.
            migrationBuilder.Sql("REVOKE UPDATE, DELETE ON [dbo].[AuditLogs] FROM [public];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore UPDATE/DELETE access before dropping the table
            migrationBuilder.Sql("GRANT UPDATE, DELETE ON [dbo].[AuditLogs] TO [public];");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "WaitlistEntries");

            migrationBuilder.DropColumn(
                name: "RetainUntil",
                table: "WaitlistEntries");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "RetainUntil",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "IntakeRecords");

            migrationBuilder.DropColumn(
                name: "RetainUntil",
                table: "IntakeRecords");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "ClinicalDocuments");

            migrationBuilder.DropColumn(
                name: "RetainUntil",
                table: "ClinicalDocuments");
        }
    }
}
