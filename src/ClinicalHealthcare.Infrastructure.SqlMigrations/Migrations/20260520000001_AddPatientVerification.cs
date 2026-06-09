using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicalHealthcare.Infrastructure.SqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VerificationStatus",
                table: "UserAccounts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VerifiedById",
                table: "UserAccounts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAt",
                table: "UserAccounts",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VerificationStatus",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "VerifiedById",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "UserAccounts");
        }
    }
}
