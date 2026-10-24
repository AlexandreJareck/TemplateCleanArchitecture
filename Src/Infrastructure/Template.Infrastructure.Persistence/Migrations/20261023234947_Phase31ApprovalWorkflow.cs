using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Template.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase31ApprovalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BaseCurrency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    StepDuration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    ReminderAfter = table.Column<TimeSpan>(type: "interval", nullable: false),
                    EscalationRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MaxEscalations = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FirstUsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalPolicies", x => x.Id);
                    table.CheckConstraint("CK_ApprovalPolicies_MaxEscalations", "\"MaxEscalations\" >= 0");
                    table.CheckConstraint("CK_ApprovalPolicies_Version", "\"Version\" >= 1");
                });

            migrationBuilder.CreateTable(
                name: "ApprovalInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequisitionNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequesterId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CostCenterId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    AmountEvaluated = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BaseCurrency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    RequisitionAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RequisitionCurrency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyVersion = table.Column<int>(type: "integer", nullable: false),
                    StepDuration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    ReminderAfter = table.Column<TimeSpan>(type: "interval", nullable: false),
                    EscalationRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MaxEscalations = table.Column<int>(type: "integer", nullable: false),
                    WorkflowInstanceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WorkflowVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PendingOutcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CurrentLevel = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndCause = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    EndReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalInstances", x => x.Id);
                    table.CheckConstraint("CK_ApprovalInstances_Amounts", "\"AmountEvaluated\" > 0 AND \"RequisitionAmount\" > 0");
                    table.CheckConstraint("CK_ApprovalInstances_CurrentLevel", "\"CurrentLevel\" >= 1");
                    table.ForeignKey(
                        name: "FK_ApprovalInstances_ApprovalPolicies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "ApprovalPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalInstances_PurchaseRequisitions_RequisitionId",
                        column: x => x.RequisitionId,
                        principalTable: "PurchaseRequisitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalPolicyLevels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    MinimumAmountExclusive = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Resolver = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalPolicyLevels", x => x.Id);
                    table.CheckConstraint("CK_ApprovalPolicyLevels_Level", "\"Level\" >= 1");
                    table.CheckConstraint("CK_ApprovalPolicyLevels_Minimum", "\"MinimumAmountExclusive\" >= 0");
                    table.ForeignKey(
                        name: "FK_ApprovalPolicyLevels_ApprovalPolicies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "ApprovalPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssigneeUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssigneeRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DecisionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalHistory_ApprovalInstances_InstanceId",
                        column: x => x.InstanceId,
                        principalTable: "ApprovalInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Resolver = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ResolverRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AssigneeUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssigneeRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AssignmentSequence = table.Column<int>(type: "integer", nullable: false),
                    AssignedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReminderDueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RemindedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReminderCount = table.Column<int>(type: "integer", nullable: false),
                    EscalationCount = table.Column<int>(type: "integer", nullable: false),
                    Decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    DecidedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalSteps", x => x.Id);
                    table.CheckConstraint("CK_ApprovalSteps_Assignee", "NOT (\"AssigneeUserId\" IS NOT NULL AND \"AssigneeRole\" IS NOT NULL)");
                    table.CheckConstraint("CK_ApprovalSteps_Level", "\"Level\" >= 1");
                    table.ForeignKey(
                        name: "FK_ApprovalSteps_ApprovalInstances_InstanceId",
                        column: x => x.InstanceId,
                        principalTable: "ApprovalInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ApprovalPolicies",
                columns: new[] { "Id", "BaseCurrency", "CreatedAtUtc", "CreatedBy", "EffectiveFromUtc", "EscalationRole", "FirstUsedAtUtc", "MaxEscalations", "Reason", "ReminderAfter", "StepDuration", "Version" },
                values: new object[] { new Guid("6a1f3c52-7d0e-4b8a-9b51-31a0c0de0001"), "BRL", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "ProcurementManager", null, 1, "Policy version 1 seed (B23-11).", new TimeSpan(3, 0, 0, 0, 0), new TimeSpan(5, 0, 0, 0, 0), 1 });

            migrationBuilder.InsertData(
                table: "ApprovalPolicyLevels",
                columns: new[] { "Id", "Level", "MinimumAmountExclusive", "PolicyId", "Resolver", "Role" },
                values: new object[,]
                {
                    { new Guid("6a1f3c52-7d0e-4b8a-9b51-31a0c0de0001"), 1, 0m, new Guid("6a1f3c52-7d0e-4b8a-9b51-31a0c0de0001"), "CostCenterManager", null },
                    { new Guid("6a1f3c52-7d0e-4b8a-9b51-31a0c0de0002"), 2, 10000m, new Guid("6a1f3c52-7d0e-4b8a-9b51-31a0c0de0001"), "DepartmentHead", null },
                    { new Guid("6a1f3c52-7d0e-4b8a-9b51-31a0c0de0003"), 3, 100000m, new Guid("6a1f3c52-7d0e-4b8a-9b51-31a0c0de0001"), "RoleGroup", "FinanceApprover" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalHistory_ActorId",
                table: "ApprovalHistory",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalHistory_InstanceId_Sequence",
                table: "ApprovalHistory",
                columns: new[] { "InstanceId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalInstances_AwardId",
                table: "ApprovalInstances",
                column: "AwardId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalInstances_PolicyId",
                table: "ApprovalInstances",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalInstances_RequesterId",
                table: "ApprovalInstances",
                column: "RequesterId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalInstances_RequisitionId",
                table: "ApprovalInstances",
                column: "RequisitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalInstances_Status_UpdatedAtUtc",
                table: "ApprovalInstances",
                columns: new[] { "Status", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalInstances_SupplierId_Status",
                table: "ApprovalInstances",
                columns: new[] { "SupplierId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalInstances_WorkflowInstanceId",
                table: "ApprovalInstances",
                column: "WorkflowInstanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalPolicies_EffectiveFromUtc",
                table: "ApprovalPolicies",
                column: "EffectiveFromUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalPolicies_Version",
                table: "ApprovalPolicies",
                column: "Version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalPolicyLevels_PolicyId_Level",
                table: "ApprovalPolicyLevels",
                columns: new[] { "PolicyId", "Level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalSteps_AssigneeRole_Status",
                table: "ApprovalSteps",
                columns: new[] { "AssigneeRole", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalSteps_AssigneeUserId_Status",
                table: "ApprovalSteps",
                columns: new[] { "AssigneeUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalSteps_InstanceId_Level",
                table: "ApprovalSteps",
                columns: new[] { "InstanceId", "Level" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalHistory");

            migrationBuilder.DropTable(
                name: "ApprovalPolicyLevels");

            migrationBuilder.DropTable(
                name: "ApprovalSteps");

            migrationBuilder.DropTable(
                name: "ApprovalInstances");

            migrationBuilder.DropTable(
                name: "ApprovalPolicies");
        }
    }
}
