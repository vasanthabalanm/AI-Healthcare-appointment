using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicalHealthcare.Infrastructure.SqlMigrations.Migrations
{
    // NOTE: This migration was generated after both TASK_021 and TASK_022 entity changes
    // had accumulated. It therefore contains two separate concerns:
    //   • TASK_021 (WaitlistEntry): OfferExpiresAt, OfferedSlotId
    //   • TASK_022 (Appointment):   NoShowRiskScore, IsHighRisk
    // A targeted rollback of AppointmentRiskScore also removes the TASK_021 WaitlistEntry
    // columns. Plan accordingly when rolling back only one of the two feature sets.
    /// <inheritdoc />
    public partial class AppointmentRiskScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "OfferExpiresAt",
                table: "WaitlistEntries",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OfferedSlotId",
                table: "WaitlistEntries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHighRisk",
                table: "Appointments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "NoShowRiskScore",
                table: "Appointments",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OfferExpiresAt",
                table: "WaitlistEntries");

            migrationBuilder.DropColumn(
                name: "OfferedSlotId",
                table: "WaitlistEntries");

            migrationBuilder.DropColumn(
                name: "IsHighRisk",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "NoShowRiskScore",
                table: "Appointments");
        }
    }
}
