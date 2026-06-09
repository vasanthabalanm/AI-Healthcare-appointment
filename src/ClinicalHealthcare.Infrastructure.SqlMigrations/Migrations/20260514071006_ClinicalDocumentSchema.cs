using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicalHealthcare.Infrastructure.SqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class ClinicalDocumentSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClinicalDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PatientId = table.Column<int>(type: "int", nullable: false),
                    UploadedByStaffId = table.Column<int>(type: "int", nullable: true),
                    OriginalFileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    EncryptedBlobPath = table.Column<string>(type: "nvarchar(500)", nullable: false),
                    VirusScanResult = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    OcrStatus = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicalDocuments_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalDocuments_UserAccounts_UploadedByStaffId",
                        column: x => x.UploadedByStaffId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalDocuments_PatientId",
                table: "ClinicalDocuments",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalDocuments_UploadedByStaffId",
                table: "ClinicalDocuments",
                column: "UploadedByStaffId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicalDocuments");
        }
    }
}
