using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicalHealthcare.Infrastructure.SqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class InsurancePreCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InsuranceStatus",
                table: "IntakeRecords",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.CreateTable(
                name: "InsuranceReferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InsurerId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    InsurerName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PlanCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsuranceReferences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UIX_InsuranceReferences_InsurerId_PlanCode",
                table: "InsuranceReferences",
                columns: new[] { "InsurerId", "PlanCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InsuranceReferences");

            migrationBuilder.DropColumn(
                name: "InsuranceStatus",
                table: "IntakeRecords");
        }
    }
}
