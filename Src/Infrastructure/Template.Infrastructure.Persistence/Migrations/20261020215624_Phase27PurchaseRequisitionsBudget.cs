using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Template.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase27PurchaseRequisitionsBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BudgetFiscalPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetFiscalPeriods", x => x.Id);
                    table.CheckConstraint("CK_BudgetFiscalPeriods_DateRange", "\"EndDate\" >= \"StartDate\"");
                });

            migrationBuilder.CreateTable(
                name: "PurchaseRequisitionNumberCounters",
                columns: table => new
                {
                    Year = table.Column<int>(type: "integer", nullable: false),
                    LastValue = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseRequisitionNumberCounters", x => x.Year);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseRequisitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequesterId = table.Column<Guid>(type: "uuid", nullable: false),
                    CostCenterId = table.Column<Guid>(type: "uuid", nullable: false),
                    CostCenterCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CostCenterName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NeededBy = table.Column<DateOnly>(type: "date", nullable: true),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    EstimatedTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ActiveReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActiveAllocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceRequisitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseRequisitions", x => x.Id);
                    table.CheckConstraint("CK_PurchaseRequisitions_EstimatedTotal", "\"EstimatedTotal\" >= 0");
                    table.CheckConstraint("CK_PurchaseRequisitions_Revision", "\"Revision\" >= 1");
                    table.ForeignKey(
                        name: "FK_PurchaseRequisitions_OrganizationCostCenters_CostCenterId",
                        column: x => x.CostCenterId,
                        principalTable: "OrganizationCostCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseRequisitions_PurchaseRequisitions_SourceRequisition~",
                        column: x => x.SourceRequisitionId,
                        principalTable: "PurchaseRequisitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BudgetAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FiscalPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    CostCenterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReservedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CommittedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetAllocations", x => x.Id);
                    table.CheckConstraint("CK_BudgetAllocations_Amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_BudgetAllocations_Totals", "\"ReservedAmount\" >= 0 AND \"CommittedAmount\" >= 0 AND \"ReservedAmount\" + \"CommittedAmount\" <= \"Amount\"");
                    table.ForeignKey(
                        name: "FK_BudgetAllocations_BudgetFiscalPeriods_FiscalPeriodId",
                        column: x => x.FiscalPeriodId,
                        principalTable: "BudgetFiscalPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetAllocations_OrganizationCostCenters_CostCenterId",
                        column: x => x.CostCenterId,
                        principalTable: "OrganizationCostCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseRequisitionAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Length = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AddedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AddedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LockedAtRevision = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseRequisitionAttachments", x => x.Id);
                    table.CheckConstraint("CK_PurchaseRequisitionAttachments_Length", "\"Length\" > 0 AND \"Length\" <= 10485760");
                    table.ForeignKey(
                        name: "FK_PurchaseRequisitionAttachments_PurchaseRequisitions_Requisi~",
                        column: x => x.RequisitionId,
                        principalTable: "PurchaseRequisitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseRequisitionLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    CatalogItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CategoryName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UnitOfMeasureId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UnitName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    UnitAllowedDecimals = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    EstimatedUnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseRequisitionLines", x => x.Id);
                    table.CheckConstraint("CK_PurchaseRequisitionLines_EstimatedUnitPrice", "\"EstimatedUnitPrice\" > 0");
                    table.CheckConstraint("CK_PurchaseRequisitionLines_Quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_PurchaseRequisitionLines_CatalogCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "CatalogCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseRequisitionLines_CatalogItems_CatalogItemId",
                        column: x => x.CatalogItemId,
                        principalTable: "CatalogItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseRequisitionLines_CatalogUnitsOfMeasure_UnitOfMeasur~",
                        column: x => x.UnitOfMeasureId,
                        principalTable: "CatalogUnitsOfMeasure",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseRequisitionLines_PurchaseRequisitions_RequisitionId",
                        column: x => x.RequisitionId,
                        principalTable: "PurchaseRequisitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseRequisitionStateChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    From = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    To = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReasonCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseRequisitionStateChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseRequisitionStateChanges_PurchaseRequisitions_Requis~",
                        column: x => x.RequisitionId,
                        principalTable: "PurchaseRequisitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseRequisitionSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubmittedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Purpose = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    NeededBy = table.Column<DateOnly>(type: "date", nullable: false),
                    CostCenterId = table.Column<Guid>(type: "uuid", nullable: false),
                    CostCenterCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CostCenterName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    EstimatedTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseRequisitionSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseRequisitionSubmissions_PurchaseRequisitions_Requisi~",
                        column: x => x.RequisitionId,
                        principalTable: "PurchaseRequisitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BudgetAllocationAmountChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    PreviousAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NewAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetAllocationAmountChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetAllocationAmountChanges_BudgetAllocations_AllocationId",
                        column: x => x.AllocationId,
                        principalTable: "BudgetAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BudgetReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequisitionRevision = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReleaseReason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReleasedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetReservations", x => x.Id);
                    table.CheckConstraint("CK_BudgetReservations_Amount", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_BudgetReservations_BudgetAllocations_AllocationId",
                        column: x => x.AllocationId,
                        principalTable: "BudgetAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BudgetReservations_PurchaseRequisitions_RequisitionId",
                        column: x => x.RequisitionId,
                        principalTable: "PurchaseRequisitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseRequisitionSubmissionLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    CatalogItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UnitOfMeasureId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    EstimatedUnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseRequisitionSubmissionLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseRequisitionSubmissionLines_PurchaseRequisitionSubmi~",
                        column: x => x.SubmissionId,
                        principalTable: "PurchaseRequisitionSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BudgetReservationChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Operation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    From = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    To = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetReservationChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BudgetReservationChanges_BudgetReservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "BudgetReservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetAllocationAmountChanges_AllocationId_Sequence",
                table: "BudgetAllocationAmountChanges",
                columns: new[] { "AllocationId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BudgetAllocations_CostCenterId",
                table: "BudgetAllocations",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetAllocations_FiscalPeriodId_CostCenterId_Currency",
                table: "BudgetAllocations",
                columns: new[] { "FiscalPeriodId", "CostCenterId", "Currency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BudgetFiscalPeriods_Code",
                table: "BudgetFiscalPeriods",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BudgetFiscalPeriods_Status_StartDate_EndDate",
                table: "BudgetFiscalPeriods",
                columns: new[] { "Status", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetReservationChanges_ReservationId_Sequence",
                table: "BudgetReservationChanges",
                columns: new[] { "ReservationId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BudgetReservations_AllocationId_Status",
                table: "BudgetReservations",
                columns: new[] { "AllocationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BudgetReservations_RequisitionId_Active",
                table: "BudgetReservations",
                column: "RequisitionId",
                unique: true,
                filter: "\"Status\" IN ('Reserved', 'Committed')");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetReservations_RequisitionId_CreatedAtUtc",
                table: "BudgetReservations",
                columns: new[] { "RequisitionId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitionAttachments_RequisitionId",
                table: "PurchaseRequisitionAttachments",
                column: "RequisitionId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitionLines_CatalogItemId",
                table: "PurchaseRequisitionLines",
                column: "CatalogItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitionLines_CategoryId",
                table: "PurchaseRequisitionLines",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitionLines_RequisitionId_LineNumber",
                table: "PurchaseRequisitionLines",
                columns: new[] { "RequisitionId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitionLines_UnitOfMeasureId",
                table: "PurchaseRequisitionLines",
                column: "UnitOfMeasureId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitions_CostCenterId_Status",
                table: "PurchaseRequisitions",
                columns: new[] { "CostCenterId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitions_DepartmentId_Status",
                table: "PurchaseRequisitions",
                columns: new[] { "DepartmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitions_Number",
                table: "PurchaseRequisitions",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitions_RequesterId_CreatedAtUtc",
                table: "PurchaseRequisitions",
                columns: new[] { "RequesterId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitions_SourceRequisitionId",
                table: "PurchaseRequisitions",
                column: "SourceRequisitionId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitions_Status_NeededBy",
                table: "PurchaseRequisitions",
                columns: new[] { "Status", "NeededBy" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitionStateChanges_RequisitionId_Sequence",
                table: "PurchaseRequisitionStateChanges",
                columns: new[] { "RequisitionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitionSubmissionLines_SubmissionId_LineNumber",
                table: "PurchaseRequisitionSubmissionLines",
                columns: new[] { "SubmissionId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequisitionSubmissions_RequisitionId_Revision",
                table: "PurchaseRequisitionSubmissions",
                columns: new[] { "RequisitionId", "Revision" },
                unique: true);

            // BUD-001: fiscal periods never overlap (inclusive local-date ranges). EF Core has no model API for
            // exclusion constraints; the built-in GiST range operator class needs no extension.
            migrationBuilder.Sql("""
                ALTER TABLE "BudgetFiscalPeriods"
                ADD CONSTRAINT "EX_BudgetFiscalPeriods_NoOverlap"
                EXCLUDE USING gist (daterange("StartDate", "EndDate", '[]') WITH &&);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""ALTER TABLE "BudgetFiscalPeriods" DROP CONSTRAINT IF EXISTS "EX_BudgetFiscalPeriods_NoOverlap";""");

            migrationBuilder.DropTable(
                name: "BudgetAllocationAmountChanges");

            migrationBuilder.DropTable(
                name: "BudgetReservationChanges");

            migrationBuilder.DropTable(
                name: "PurchaseRequisitionAttachments");

            migrationBuilder.DropTable(
                name: "PurchaseRequisitionLines");

            migrationBuilder.DropTable(
                name: "PurchaseRequisitionNumberCounters");

            migrationBuilder.DropTable(
                name: "PurchaseRequisitionStateChanges");

            migrationBuilder.DropTable(
                name: "PurchaseRequisitionSubmissionLines");

            migrationBuilder.DropTable(
                name: "BudgetReservations");

            migrationBuilder.DropTable(
                name: "PurchaseRequisitionSubmissions");

            migrationBuilder.DropTable(
                name: "BudgetAllocations");

            migrationBuilder.DropTable(
                name: "PurchaseRequisitions");

            migrationBuilder.DropTable(
                name: "BudgetFiscalPeriods");
        }
    }
}
