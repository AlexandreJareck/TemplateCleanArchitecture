using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Template.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase30FilesReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FilesStoredFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Length = table.Column<long>(type: "bigint", nullable: true),
                    Sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    Classification = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AvailableAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletionReason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilesStoredFiles", x => x.Id);
                    table.CheckConstraint("CK_FilesStoredFiles_Available", "\"Status\" <> 'Available' OR (\"Length\" > 0 AND \"Sha256\" IS NOT NULL AND char_length(\"Sha256\") = 64)");
                    table.CheckConstraint("CK_FilesStoredFiles_Length", "\"Length\" IS NULL OR \"Length\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "ReportingReportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    ParametersJson = table.Column<string>(type: "jsonb", nullable: false),
                    ParametersHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    AsOfUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PolicyVersion = table.Column<int>(type: "integer", nullable: false),
                    TemplateVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClaimOwner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ClaimExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProgressPercent = table.Column<int>(type: "integer", nullable: false),
                    ProgressStage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    OutputFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    OutputFileName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    OutputLength = table.Column<long>(type: "bigint", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportingReportJobs", x => x.Id);
                    table.CheckConstraint("CK_ReportingReportJobs_Attempts", "\"AttemptCount\" >= 0 AND \"AttemptCount\" <= 10");
                    table.CheckConstraint("CK_ReportingReportJobs_Progress", "\"ProgressPercent\" >= 0 AND \"ProgressPercent\" <= 100");
                    table.CheckConstraint("CK_ReportingReportJobs_ReadyOutput", "\"Status\" <> 'Ready' OR \"OutputFileId\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_ReportingReportJobs_FilesStoredFiles_OutputFileId",
                        column: x => x.OutputFileId,
                        principalTable: "FilesStoredFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FilesStoredFiles_OwnerType_OwnerId",
                table: "FilesStoredFiles",
                columns: new[] { "OwnerType", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_FilesStoredFiles_Status_CreatedAtUtc",
                table: "FilesStoredFiles",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FilesStoredFiles_Status_ExpiresAtUtc",
                table: "FilesStoredFiles",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportingReportJobs_Active",
                table: "ReportingReportJobs",
                columns: new[] { "RequestedBy", "ReportType", "ParametersHash" },
                unique: true,
                filter: "\"Status\" IN ('Pending', 'Running')");

            migrationBuilder.CreateIndex(
                name: "IX_ReportingReportJobs_OutputFileId",
                table: "ReportingReportJobs",
                column: "OutputFileId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportingReportJobs_RequestedBy_RequestedAtUtc",
                table: "ReportingReportJobs",
                columns: new[] { "RequestedBy", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportingReportJobs_Status_ClaimExpiresAtUtc",
                table: "ReportingReportJobs",
                columns: new[] { "Status", "ClaimExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportingReportJobs_Status_ExpiresAtUtc",
                table: "ReportingReportJobs",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportingReportJobs_Status_NextAttemptAtUtc",
                table: "ReportingReportJobs",
                columns: new[] { "Status", "NextAttemptAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportingReportJobs");

            migrationBuilder.DropTable(
                name: "FilesStoredFiles");
        }
    }
}
