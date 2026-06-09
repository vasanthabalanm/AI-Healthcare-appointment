using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicalHealthcare.Infrastructure.SqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class OutreachRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutreachRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AppointmentId = table.Column<int>(type: "int", nullable: false),
                    StaffId = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AttemptedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutreachRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutreachRecords_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutreachRecords_UserAccounts_StaffId",
                        column: x => x.StaffId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutreachRecords_AppointmentId",
                table: "OutreachRecords",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_OutreachRecords_StaffId",
                table: "OutreachRecords",
                column: "StaffId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutreachRecords");
        }
    }
}
