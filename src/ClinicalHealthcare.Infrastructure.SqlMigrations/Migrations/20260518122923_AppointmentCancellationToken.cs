using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicalHealthcare.Infrastructure.SqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AppointmentCancellationToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CancellationLinkExpiry",
                table: "Appointments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationLinkTokenHash",
                table: "Appointments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CancellationLinkUsed",
                table: "Appointments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EmailReminderJobId",
                table: "Appointments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancellationLinkExpiry",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "CancellationLinkTokenHash",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "CancellationLinkUsed",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "EmailReminderJobId",
                table: "Appointments");
        }
    }
}
