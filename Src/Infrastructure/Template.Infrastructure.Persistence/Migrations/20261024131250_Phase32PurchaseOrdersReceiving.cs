using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Template.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase32PurchaseOrdersReceiving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseOrderId",
                table: "PurchaseRequisitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PurchasingOrderNumberCounters",
                columns: table => new
                {
                    Year = table.Column<int>(type: "integer", nullable: false),
                    LastValue = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchasingOrderNumberCounters", x => x.Year);
                    table.CheckConstraint("CK_PurchasingOrderNumberCounters_LastValue", "\"LastValue\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "PurchasingOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AwardId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovalInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    QuoteRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequisitionNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequesterId = table.Column<Guid>(type: "uuid", nullable: false),
                    CostCenterId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierLegalName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SupplierTradeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SupplierCountry = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    SupplierTaxId = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SupplierAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SupplierContactName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SupplierContactEmail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SupplierContactPhone = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BuyerId = table.Column<Guid>(type: "uuid", nullable: true),
                    BuyerName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Freight = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxesIncluded = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentTerms = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    QuoteValidUntil = table.Column<DateOnly>(type: "date", nullable: false),
                    DeliveryDays = table.Column<int>(type: "integer", nullable: false),
                    BaseCurrency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    QuoteToBaseRate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    RequisitionCurrency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    RequisitionToBaseRate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    RequisitionCurrencyTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DeliveryAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IssuedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletionSequence = table.Column<int>(type: "integer", nullable: false),
                    ShortClosed = table.Column<bool>(type: "boolean", nullable: false),
                    ShortClosedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ShortCloseReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ShortCloseReleasedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    DocumentStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DocumentJobId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentTemplateVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    DocumentAttempts = table.Column<int>(type: "integer", nullable: false),
                    DocumentFailureCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    DocumentReadyAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NotificationStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NotificationAttempts = table.Column<int>(type: "integer", nullable: false),
                    NotificationFailureCode = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    NotifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchasingOrders", x => x.Id);
                    table.CheckConstraint("CK_PurchasingOrders_Attempts", "\"DocumentAttempts\" >= 0 AND \"NotificationAttempts\" >= 0");
                    table.CheckConstraint("CK_PurchasingOrders_IssuedNumber", "\"Status\" = 'Draft' OR \"Status\" = 'Cancelled' OR \"Number\" IS NOT NULL");
                    table.CheckConstraint("CK_PurchasingOrders_Total", "\"Total\" >= 0 AND \"Subtotal\" >= 0 AND \"Freight\" >= 0 AND \"Discount\" >= 0");
                    table.ForeignKey(
                        name: "FK_PurchasingOrders_PurchaseRequisitions_RequisitionId",
                        column: x => x.RequisitionId,
                        principalTable: "PurchaseRequisitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchasingOrders_SourcingAwards_AwardId",
                        column: x => x.AwardId,
                        principalTable: "SourcingAwards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchasingOrders_SourcingRfqs_RfqId",
                        column: x => x.RfqId,
                        principalTable: "SourcingRfqs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchasingOrders_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceivingReceiptNumberCounters",
                columns: table => new
                {
                    Year = table.Column<int>(type: "integer", nullable: false),
                    LastValue = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingReceiptNumberCounters", x => x.Year);
                });

            migrationBuilder.CreateTable(
                name: "PurchasingOrderHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    From = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    To = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReasonCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchasingOrderHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchasingOrderHistory_PurchasingOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchasingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchasingOrderLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    RequisitionLineNumber = table.Column<int>(type: "integer", nullable: false),
                    CatalogItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitOfMeasureId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UnitAllowedDecimals = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PromisedDeliveryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    AcceptedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    RejectedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    IsShortClosed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchasingOrderLines", x => x.Id);
                    table.CheckConstraint("CK_PurchasingOrderLines_Quantity", "\"Quantity\" > 0 AND \"UnitPrice\" > 0");
                    table.CheckConstraint("CK_PurchasingOrderLines_Received", "\"ReceivedQuantity\" >= 0 AND \"RejectedQuantity\" >= 0 AND \"AcceptedQuantity\" >= 0");
                    table.CheckConstraint("CK_PurchasingOrderLines_UnitDecimals", "\"UnitAllowedDecimals\" BETWEEN 0 AND 3");
                    table.ForeignKey(
                        name: "FK_PurchasingOrderLines_PurchasingOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchasingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceivingReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseOrderNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReceiverId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DeliveryReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PolicyReference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TolerancePercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    HasDiscrepancy = table.Column<bool>(type: "boolean", nullable: false),
                    PostedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceivingReceipts_PurchasingOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchasingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceivingReceiptAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Length = table.Column<long>(type: "bigint", nullable: false),
                    AddedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingReceiptAttachments", x => x.Id);
                    table.CheckConstraint("CK_ReceivingReceiptAttachments_Length", "\"Length\" > 0 AND \"Length\" <= 10485760");
                    table.ForeignKey(
                        name: "FK_ReceivingReceiptAttachments_ReceivingReceipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "ReceivingReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceivingReceiptCorrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    PoLineNumber = table.Column<int>(type: "integer", nullable: false),
                    AcceptedDelta = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingReceiptCorrections", x => x.Id);
                    table.CheckConstraint("CK_ReceivingReceiptCorrections_Delta", "\"AcceptedDelta\" <> 0");
                    table.ForeignKey(
                        name: "FK_ReceivingReceiptCorrections_ReceivingReceipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "ReceivingReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceivingReceiptLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    PoLineNumber = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    UnitCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UnitAllowedDecimals = table.Column<int>(type: "integer", nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    AcceptedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    RejectedQuantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    DiscrepancyType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    DiscrepancyNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceivingReceiptLines", x => x.Id);
                    table.CheckConstraint("CK_ReceivingReceiptLines_Discrepancy", "\"RejectedQuantity\" = 0 OR \"DiscrepancyType\" IS NOT NULL");
                    table.CheckConstraint("CK_ReceivingReceiptLines_Quantities", "\"ReceivedQuantity\" > 0 AND \"AcceptedQuantity\" >= 0 AND \"RejectedQuantity\" >= 0 AND \"AcceptedQuantity\" + \"RejectedQuantity\" = \"ReceivedQuantity\"");
                    table.ForeignKey(
                        name: "FK_ReceivingReceiptLines_ReceivingReceipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "ReceivingReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchasingOrderHistory_PurchaseOrderId_Sequence",
                table: "PurchasingOrderHistory",
                columns: new[] { "PurchaseOrderId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchasingOrderLines_PurchaseOrderId_LineNumber",
                table: "PurchasingOrderLines",
                columns: new[] { "PurchaseOrderId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchasingOrders_AwardId",
                table: "PurchasingOrders",
                column: "AwardId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchasingOrders_DocumentJobId",
                table: "PurchasingOrders",
                column: "DocumentJobId",
                filter: "\"DocumentJobId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchasingOrders_Number",
                table: "PurchasingOrders",
                column: "Number",
                unique: true,
                filter: "\"Number\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchasingOrders_RequesterId",
                table: "PurchasingOrders",
                column: "RequesterId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchasingOrders_RequisitionId",
                table: "PurchasingOrders",
                column: "RequisitionId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchasingOrders_RfqId",
                table: "PurchasingOrders",
                column: "RfqId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchasingOrders_Status_UpdatedAtUtc",
                table: "PurchasingOrders",
                columns: new[] { "Status", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchasingOrders_SupplierId_Status",
                table: "PurchasingOrders",
                columns: new[] { "SupplierId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingReceiptAttachments_ReceiptId",
                table: "ReceivingReceiptAttachments",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingReceiptCorrections_ReceiptId_Sequence",
                table: "ReceivingReceiptCorrections",
                columns: new[] { "ReceiptId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingReceiptLines_ReceiptId_PoLineNumber",
                table: "ReceivingReceiptLines",
                columns: new[] { "ReceiptId", "PoLineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingReceipts_HasDiscrepancy_PostedAtUtc",
                table: "ReceivingReceipts",
                columns: new[] { "HasDiscrepancy", "PostedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingReceipts_Number",
                table: "ReceivingReceipts",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceivingReceipts_PurchaseOrderId_PostedAtUtc",
                table: "ReceivingReceipts",
                columns: new[] { "PurchaseOrderId", "PostedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchasingOrderHistory");

            migrationBuilder.DropTable(
                name: "PurchasingOrderLines");

            migrationBuilder.DropTable(
                name: "PurchasingOrderNumberCounters");

            migrationBuilder.DropTable(
                name: "ReceivingReceiptAttachments");

            migrationBuilder.DropTable(
                name: "ReceivingReceiptCorrections");

            migrationBuilder.DropTable(
                name: "ReceivingReceiptLines");

            migrationBuilder.DropTable(
                name: "ReceivingReceiptNumberCounters");

            migrationBuilder.DropTable(
                name: "ReceivingReceipts");

            migrationBuilder.DropTable(
                name: "PurchasingOrders");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderId",
                table: "PurchaseRequisitions");
        }
    }
}
