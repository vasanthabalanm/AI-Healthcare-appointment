using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicalHealthcare.Infrastructure.SqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AppointmentEmailReminderSentAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EmailReminderSentAt",
                table: "Appointments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_CancellationLinkTokenHash",
                table: "Appointments",
                column: "CancellationLinkTokenHash",
                unique: true,
                filter: "[CancellationLinkTokenHash] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_CancellationLinkTokenHash",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "EmailReminderSentAt",
                table: "Appointments");
        }
    }
}
