using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicalHealthcare.Infrastructure.SqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class CalendarToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CalendarTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PatientId = table.Column<int>(type: "int", nullable: false),
                    AppointmentId = table.Column<int>(type: "int", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EncryptedAccessToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EncryptedRefreshToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CalendarEventId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalendarTokens_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CalendarTokens_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarTokens_PatientId",
                table: "CalendarTokens",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "UIX_CalendarTokens_AppointmentId_Provider",
                table: "CalendarTokens",
                columns: new[] { "AppointmentId", "Provider" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalendarTokens");
        }
    }
}
