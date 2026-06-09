using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicalHealthcare.Infrastructure.SqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class PasswordReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordResetTokenExpiry",
                table: "UserAccounts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordResetTokenHash",
                table: "UserAccounts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PasswordResetTokenUsed",
                table: "UserAccounts",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PasswordResetTokenExpiry",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "PasswordResetTokenHash",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "PasswordResetTokenUsed",
                table: "UserAccounts");
        }
    }
}
