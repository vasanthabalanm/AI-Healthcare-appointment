using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ClinicalHealthcare.Infrastructure.PgMigrations.Migrations.Application
{
    /// <inheritdoc />
    public partial class InitialCreate_App : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EntityType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EntityId = table.Column<int>(type: "integer", nullable: false),
                    ActorId = table.Column<int>(type: "integer", nullable: true),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BeforeValue = table.Column<string>(type: "text", nullable: true),
                    AfterValue = table.Column<string>(type: "text", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InsuranceReferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InsurerId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    InsurerName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PlanCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsuranceReferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Slots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SlotTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    IsAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Slots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    WalkIn = table.Column<bool>(type: "boolean", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    RetainUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailedLoginAttempts = table.Column<int>(type: "integer", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerificationTokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    VerificationTokenExpiry = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PasswordResetTokenHash = table.Column<string>(type: "text", nullable: true),
                    PasswordResetTokenExpiry = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PasswordResetTokenUsed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordResetTokenIssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerificationStatus = table.Column<int>(type: "integer", nullable: false),
                    VerifiedById = table.Column<int>(type: "integer", nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CodingStatus = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Appointments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    SlotId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    BookedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    NoShowRiskScore = table.Column<int>(type: "integer", nullable: false),
                    IsHighRisk = table.Column<bool>(type: "boolean", nullable: false),
                    ReminderJobId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SmsReminderJobId48h = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SmsReminderJobId2h = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EmailReminderJobId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CancellationLinkTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CancellationLinkExpiry = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationLinkUsed = table.Column<bool>(type: "boolean", nullable: false),
                    EmailReminderSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Appointments_Slots_SlotId",
                        column: x => x.SlotId,
                        principalTable: "Slots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClinicalDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    UploadedByStaffId = table.Column<int>(type: "integer", nullable: true),
                    OriginalFileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    EncryptedBlobPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    VirusScanResult = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    OcrStatus = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    RawOcrText = table.Column<string>(type: "text", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    RetainUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "IntakeRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IntakeGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsLatest = table.Column<bool>(type: "boolean", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    ChiefComplaint = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CurrentMeds = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Allergies = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    MedicalHistory = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    InsuranceStatus = table.Column<int>(type: "integer", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    RetainUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntakeRecords_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QueueEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    QueueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsWalkIn = table.Column<bool>(type: "boolean", nullable: false),
                    AddedByStaffId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    RowVersion = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueueEntries_UserAccounts_AddedByStaffId",
                        column: x => x.AddedByStaffId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QueueEntries_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WaitlistEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    PreferredSlotId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    QueuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    OfferExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OfferedSlotId = table.Column<int>(type: "integer", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    RetainUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaitlistEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WaitlistEntries_Slots_PreferredSlotId",
                        column: x => x.PreferredSlotId,
                        principalTable: "Slots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WaitlistEntries_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CalendarTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PatientId = table.Column<int>(type: "integer", nullable: false),
                    AppointmentId = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EncryptedAccessToken = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    EncryptedRefreshToken = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CalendarEventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
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

            migrationBuilder.CreateTable(
                name: "OutreachRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AppointmentId = table.Column<int>(type: "integer", nullable: false),
                    StaffId = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    AttemptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
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
                name: "IX_Appointments_CancellationLinkTokenHash",
                table: "Appointments",
                column: "CancellationLinkTokenHash",
                unique: true,
                filter: "\"CancellationLinkTokenHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_PatientId",
                table: "Appointments",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_SlotId",
                table: "Appointments",
                column: "SlotId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_OccurredAt",
                table: "AuditLogs",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarTokens_PatientId",
                table: "CalendarTokens",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "UIX_CalendarTokens_AppointmentId_Provider",
                table: "CalendarTokens",
                columns: new[] { "AppointmentId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalDocuments_PatientId",
                table: "ClinicalDocuments",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalDocuments_UploadedByStaffId",
                table: "ClinicalDocuments",
                column: "UploadedByStaffId");

            migrationBuilder.CreateIndex(
                name: "UIX_InsuranceReferences_InsurerId_PlanCode",
                table: "InsuranceReferences",
                columns: new[] { "InsurerId", "PlanCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntakeRecords_GroupId_IsLatest",
                table: "IntakeRecords",
                columns: new[] { "IntakeGroupId", "IsLatest" });

            migrationBuilder.CreateIndex(
                name: "IX_IntakeRecords_PatientId",
                table: "IntakeRecords",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "UIX_IntakeRecords_GroupId_Version",
                table: "IntakeRecords",
                columns: new[] { "IntakeGroupId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutreachRecords_AppointmentId",
                table: "OutreachRecords",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_OutreachRecords_StaffId",
                table: "OutreachRecords",
                column: "StaffId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_AddedByStaffId",
                table: "QueueEntries",
                column: "AddedByStaffId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_PatientId",
                table: "QueueEntries",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntries_QueueDate_Status",
                table: "QueueEntries",
                columns: new[] { "QueueDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_Email",
                table: "UserAccounts",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_PatientId_Status",
                table: "WaitlistEntries",
                columns: new[] { "PatientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_PreferredSlotId",
                table: "WaitlistEntries",
                column: "PreferredSlotId");

            migrationBuilder.CreateIndex(
                name: "UIX_WaitlistEntries_PatientId_Active",
                table: "WaitlistEntries",
                column: "PatientId",
                unique: true,
                filter: "\"Status\" = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "CalendarTokens");

            migrationBuilder.DropTable(
                name: "ClinicalDocuments");

            migrationBuilder.DropTable(
                name: "InsuranceReferences");

            migrationBuilder.DropTable(
                name: "IntakeRecords");

            migrationBuilder.DropTable(
                name: "OutreachRecords");

            migrationBuilder.DropTable(
                name: "QueueEntries");

            migrationBuilder.DropTable(
                name: "WaitlistEntries");

            migrationBuilder.DropTable(
                name: "Appointments");

            migrationBuilder.DropTable(
                name: "Slots");

            migrationBuilder.DropTable(
                name: "UserAccounts");
        }
    }
}
