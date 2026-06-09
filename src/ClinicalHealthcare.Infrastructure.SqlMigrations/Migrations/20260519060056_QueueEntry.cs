using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicalHealthcare.Infrastructure.SqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class QueueEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DateOfBirth",
                table: "UserAccounts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WalkIn",
                table: "UserAccounts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<int>(
                name: "InsuranceStatus",
                table: "IntakeRecords",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 2);

            migrationBuilder.CreateTable(
                name: "QueueEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PatientId = table.Column<int>(type: "int", nullable: false),
                    QueueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsWalkIn = table.Column<bool>(type: "bit", nullable: false),
                    AddedByStaffId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueueEntries_UserAccounts_AddedByStaffId",
                        column: x => x.AddedByStaffId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QueueEntries_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_AddedByStaffId",
                table: "QueueEntries",
                column: "AddedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_PatientId",
                table: "QueueEntries",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_QueueDate_Status",
                table: "QueueEntries",
                columns: new[] { "QueueDate", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QueueEntries");

            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "WalkIn",
                table: "UserAccounts");

            migrationBuilder.AlterColumn<int>(
                name: "InsuranceStatus",
                table: "IntakeRecords",
                type: "int",
                nullable: false,
                defaultValue: 2,
                oldClrType: typeof(int),
                oldType: "int");
        }
    }
}
