using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicalHealthcare.Infrastructure.SqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddWaitlistPatientStatusIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_PatientId_Status",
                table: "WaitlistEntries",
                columns: new[] { "PatientId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WaitlistEntries_PatientId_Status",
                table: "WaitlistEntries");
        }
    }
}
