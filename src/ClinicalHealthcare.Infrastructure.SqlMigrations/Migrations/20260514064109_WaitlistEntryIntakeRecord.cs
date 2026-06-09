using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicalHealthcare.Infrastructure.SqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class WaitlistEntryIntakeRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IntakeRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IntakeGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    IsLatest = table.Column<bool>(type: "bit", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ChiefComplaint = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CurrentMeds = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Allergies = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MedicalHistory = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntakeRecords_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WaitlistEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PatientId = table.Column<int>(type: "int", nullable: false),
                    PreferredSlotId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    QueuedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaitlistEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WaitlistEntries_Slots_PreferredSlotId",
                        column: x => x.PreferredSlotId,
                        principalTable: "Slots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WaitlistEntries_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IntakeRecords_GroupId_IsLatest",
                table: "IntakeRecords",
                columns: new[] { "IntakeGroupId", "IsLatest" });

            migrationBuilder.CreateIndex(
                name: "IX_IntakeRecords_PatientId",
                table: "IntakeRecords",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "UIX_IntakeRecords_GroupId_Version",
                table: "IntakeRecords",
                columns: new[] { "IntakeGroupId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_PreferredSlotId",
                table: "WaitlistEntries",
                column: "PreferredSlotId");

            migrationBuilder.CreateIndex(
                name: "UIX_WaitlistEntries_PatientId_Active",
                table: "WaitlistEntries",
                column: "PatientId",
                unique: true,
                filter: "[Status] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntakeRecords");

            migrationBuilder.DropTable(
                name: "WaitlistEntries");
        }
    }
}
