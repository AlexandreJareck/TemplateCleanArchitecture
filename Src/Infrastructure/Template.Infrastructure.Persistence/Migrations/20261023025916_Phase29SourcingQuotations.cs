using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Template.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase29SourcingQuotations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveRfqId",
                table: "PurchaseRequisitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AwardAmount",
                table: "PurchaseRequisitions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AwardId",
                table: "PurchaseRequisitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AwardedSupplierId",
                table: "PurchaseRequisitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AwardedSupplierName",
                table: "PurchaseRequisitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastAwardId",
                table: "BudgetReservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AwardId",
                table: "BudgetReservationChanges",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MessagingNotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsumerName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    RecipientType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RecipientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Template = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AggregateType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessagingNotificationDeliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SourcingRfqNumberCounters",
                columns: table => new
                {
                    Year = table.Column<int>(type: "integer", nullable: false),
                    LastValue = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourcingRfqNumberCounters", x => x.Year);
                });

            migrationBuilder.CreateTable(
                name: "SourcingRfqs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequisitionNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequisitionRevision = table.Column<int>(type: "integer", nullable: false),
                    RequesterId = table.Column<Guid>(type: "uuid", nullable: false),
                    CostCenterId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequisitionCurrency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    RequisitionEstimatedTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NeededBy = table.Column<DateOnly>(type: "date", nullable: false),
                    RoundNumber = table.Column<int>(type: "integer", nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PolicyVersion = table.Column<int>(type: "integer", nullable: false),
                    BaseCurrency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    EstimateInBaseCurrency = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    AboveThreshold = table.Column<bool>(type: "boolean", nullable: false),
                    DeadlineUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedEarly = table.Column<bool>(type: "boolean", nullable: false),
                    CloseReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CancellationReasonCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourcingRfqs", x => x.Id);
                    table.CheckConstraint("CK_SourcingRfqs_RequisitionEstimatedTotal", "\"RequisitionEstimatedTotal\" >= 0");
                    table.CheckConstraint("CK_SourcingRfqs_RoundNumber", "\"RoundNumber\" >= 1");
                    table.ForeignKey(
                        name: "FK_SourcingRfqs_PurchaseRequisitions_RequisitionId",
                        column: x => x.RequisitionId,
                        principalTable: "PurchaseRequisitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourcingComparisons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BaseCurrency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    CloseLocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ComputedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidQuoteCount = table.Column<int>(type: "integer", nullable: false),
                    RequiredValidQuotes = table.Column<int>(type: "integer", nullable: false),
                    Warnings = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RecommendedQuoteRevisionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourcingComparisons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourcingComparisons_SourcingRfqs_RfqId",
                        column: x => x.RfqId,
                        principalTable: "SourcingRfqs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourcingExchangeRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FromCurrency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ToCurrency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    QuoteDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RetrievedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EnteredBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    WaiverId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourcingExchangeRates", x => x.Id);
                    table.CheckConstraint("CK_SourcingExchangeRates_Rate", "\"Rate\" > 0");
                    table.ForeignKey(
                        name: "FK_SourcingExchangeRates_SourcingRfqs_RfqId",
                        column: x => x.RfqId,
                        principalTable: "SourcingRfqs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourcingRfqHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    From = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    To = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourcingRfqHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourcingRfqHistory_SourcingRfqs_RfqId",
                        column: x => x.RfqId,
                        principalTable: "SourcingRfqs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourcingRfqInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierLegalName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SupplierCountry = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    SupplierTaxId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SupplierStatusAtInvitation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    InvitedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    InvitedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RemovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RemovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourcingRfqInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourcingRfqInvitations_SourcingRfqs_RfqId",
                        column: x => x.RfqId,
                        principalTable: "SourcingRfqs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourcingRfqInvitations_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourcingRfqLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    EstimatedLineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourcingRfqLines", x => x.Id);
                    table.CheckConstraint("CK_SourcingRfqLines_Quantity", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_SourcingRfqLines_SourcingRfqs_RfqId",
                        column: x => x.RfqId,
                        principalTable: "SourcingRfqs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourcingWaivers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    RuleId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Permission = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourcingWaivers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourcingWaivers_SourcingRfqs_RfqId",
                        column: x => x.RfqId,
                        principalTable: "SourcingRfqs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourcingComparisonEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ComparisonId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierLegalName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    RawTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NormalizedTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ExchangeRateId = table.Column<Guid>(type: "uuid", nullable: true),
                    Rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    DeliveryDays = table.Column<int>(type: "integer", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: false),
                    CoveredLines = table.Column<int>(type: "integer", nullable: false),
                    TotalLines = table.Column<int>(type: "integer", nullable: false),
                    SupplierActiveAtClose = table.Column<bool>(type: "boolean", nullable: false),
                    IsValid = table.Column<bool>(type: "boolean", nullable: false),
                    InvalidReasons = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourcingComparisonEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourcingComparisonEntries_SourcingComparisons_ComparisonId",
                        column: x => x.ComparisonId,
                        principalTable: "SourcingComparisons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourcingQuotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvitationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourcingQuotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourcingQuotes_SourcingRfqInvitations_InvitationId",
                        column: x => x.InvitationId,
                        principalTable: "SourcingRfqInvitations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourcingQuotes_SourcingRfqs_RfqId",
                        column: x => x.RfqId,
                        principalTable: "SourcingRfqs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourcingQuoteRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Freight = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxesIncluded = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: false),
                    DeliveryDays = table.Column<int>(type: "integer", nullable: false),
                    PaymentTerms = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EnteredBy = table.Column<Guid>(type: "uuid", nullable: true),
                    EnteredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WithdrawnAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WithdrawnBy = table.Column<Guid>(type: "uuid", nullable: true),
                    WithdrawalReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourcingQuoteRevisions", x => x.Id);
                    table.CheckConstraint("CK_SourcingQuoteRevisions_Amounts", "\"Subtotal\" > 0 AND \"Freight\" >= 0 AND \"Discount\" >= 0 AND \"TaxesIncluded\" >= 0 AND \"Total\" >= 0");
                    table.CheckConstraint("CK_SourcingQuoteRevisions_DeliveryDays", "\"DeliveryDays\" >= 0 AND \"DeliveryDays\" <= 3650");
                    table.ForeignKey(
                        name: "FK_SourcingQuoteRevisions_SourcingQuotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "SourcingQuotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourcingAwards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComparisonId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierLegalName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    AwardedTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    NormalizedTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BaseCurrency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    RequisitionCurrencyTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RequisitionCurrency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Justification = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SubmittedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourcingAwards", x => x.Id);
                    table.CheckConstraint("CK_SourcingAwards_Amounts", "\"AwardedTotal\" > 0 AND \"NormalizedTotal\" > 0 AND \"RequisitionCurrencyTotal\" > 0");
                    table.ForeignKey(
                        name: "FK_SourcingAwards_SourcingComparisons_ComparisonId",
                        column: x => x.ComparisonId,
                        principalTable: "SourcingComparisons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourcingAwards_SourcingQuoteRevisions_QuoteRevisionId",
                        column: x => x.QuoteRevisionId,
                        principalTable: "SourcingQuoteRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourcingAwards_SourcingRfqs_RfqId",
                        column: x => x.RfqId,
                        principalTable: "SourcingRfqs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourcingQuoteAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Length = table.Column<long>(type: "bigint", nullable: false),
                    AddedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AddedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourcingQuoteAttachments", x => x.Id);
                    table.CheckConstraint("CK_SourcingQuoteAttachments_Length", "\"Length\" > 0 AND \"Length\" <= 10485760");
                    table.ForeignKey(
                        name: "FK_SourcingQuoteAttachments_SourcingQuoteRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "SourcingQuoteRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SourcingQuoteLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RfqLineNumber = table.Column<int>(type: "integer", nullable: false),
                    QuantityOffered = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourcingQuoteLines", x => x.Id);
                    table.CheckConstraint("CK_SourcingQuoteLines_Positive", "\"QuantityOffered\" > 0 AND \"UnitPrice\" > 0");
                    table.ForeignKey(
                        name: "FK_SourcingQuoteLines_SourcingQuoteRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "SourcingQuoteRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MessagingNotificationDeliveries_AggregateId_CreatedAtUtc",
                table: "MessagingNotificationDeliveries",
                columns: new[] { "AggregateId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MessagingNotificationDeliveries_EventId_RecipientId_Template",
                table: "MessagingNotificationDeliveries",
                columns: new[] { "EventId", "RecipientId", "Template" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourcingAwards_ComparisonId",
                table: "SourcingAwards",
                column: "ComparisonId");

            migrationBuilder.CreateIndex(
                name: "IX_SourcingAwards_QuoteRevisionId",
                table: "SourcingAwards",
                column: "QuoteRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_SourcingAwards_RfqId_Pending",
                table: "SourcingAwards",
                column: "RfqId",
                unique: true,
                filter: "\"Status\" = 'PendingApproval'");

            migrationBuilder.CreateIndex(
                name: "IX_SourcingAwards_SupplierId",
                table: "SourcingAwards",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_SourcingComparisonEntries_ComparisonId_QuoteRevisionId",
                table: "SourcingComparisonEntries",
                columns: new[] { "ComparisonId", "QuoteRevisionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourcingComparisons_RfqId_Sequence",
                table: "SourcingComparisons",
                columns: new[] { "RfqId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourcingExchangeRates_RfqId_Purpose_FromCurrency",
                table: "SourcingExchangeRates",
                columns: new[] { "RfqId", "Purpose", "FromCurrency" });

            migrationBuilder.CreateIndex(
                name: "IX_SourcingQuoteAttachments_RevisionId",
                table: "SourcingQuoteAttachments",
                column: "RevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_SourcingQuoteLines_RevisionId_RfqLineNumber",
                table: "SourcingQuoteLines",
                columns: new[] { "RevisionId", "RfqLineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourcingQuoteRevisions_QuoteId_RevisionNumber",
                table: "SourcingQuoteRevisions",
                columns: new[] { "QuoteId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourcingQuotes_InvitationId",
                table: "SourcingQuotes",
                column: "InvitationId");

            migrationBuilder.CreateIndex(
                name: "IX_SourcingQuotes_RfqId_SupplierId",
                table: "SourcingQuotes",
                columns: new[] { "RfqId", "SupplierId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourcingRfqHistory_RfqId_Sequence",
                table: "SourcingRfqHistory",
                columns: new[] { "RfqId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourcingRfqInvitations_RfqId_Sequence",
                table: "SourcingRfqInvitations",
                columns: new[] { "RfqId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourcingRfqInvitations_RfqId_SupplierId_Active",
                table: "SourcingRfqInvitations",
                columns: new[] { "RfqId", "SupplierId" },
                unique: true,
                filter: "\"RemovedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SourcingRfqInvitations_SupplierId",
                table: "SourcingRfqInvitations",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_SourcingRfqLines_RfqId_LineNumber",
                table: "SourcingRfqLines",
                columns: new[] { "RfqId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourcingRfqs_CreatedAtUtc",
                table: "SourcingRfqs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SourcingRfqs_Number",
                table: "SourcingRfqs",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourcingRfqs_RequisitionId_Active",
                table: "SourcingRfqs",
                column: "RequisitionId",
                unique: true,
                filter: "\"Status\" <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_SourcingRfqs_RequisitionId_RoundNumber",
                table: "SourcingRfqs",
                columns: new[] { "RequisitionId", "RoundNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourcingRfqs_Status_DeadlineUtc",
                table: "SourcingRfqs",
                columns: new[] { "Status", "DeadlineUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SourcingWaivers_RfqId_DecidedAtUtc",
                table: "SourcingWaivers",
                columns: new[] { "RfqId", "DecidedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessagingNotificationDeliveries");

            migrationBuilder.DropTable(
                name: "SourcingAwards");

            migrationBuilder.DropTable(
                name: "SourcingComparisonEntries");

            migrationBuilder.DropTable(
                name: "SourcingExchangeRates");

            migrationBuilder.DropTable(
                name: "SourcingQuoteAttachments");

            migrationBuilder.DropTable(
                name: "SourcingQuoteLines");

            migrationBuilder.DropTable(
                name: "SourcingRfqHistory");

            migrationBuilder.DropTable(
                name: "SourcingRfqLines");

            migrationBuilder.DropTable(
                name: "SourcingRfqNumberCounters");

            migrationBuilder.DropTable(
                name: "SourcingWaivers");

            migrationBuilder.DropTable(
                name: "SourcingComparisons");

            migrationBuilder.DropTable(
                name: "SourcingQuoteRevisions");

            migrationBuilder.DropTable(
                name: "SourcingQuotes");

            migrationBuilder.DropTable(
                name: "SourcingRfqInvitations");

            migrationBuilder.DropTable(
                name: "SourcingRfqs");

            migrationBuilder.DropColumn(
                name: "ActiveRfqId",
                table: "PurchaseRequisitions");

            migrationBuilder.DropColumn(
                name: "AwardAmount",
                table: "PurchaseRequisitions");

            migrationBuilder.DropColumn(
                name: "AwardId",
                table: "PurchaseRequisitions");

            migrationBuilder.DropColumn(
                name: "AwardedSupplierId",
                table: "PurchaseRequisitions");

            migrationBuilder.DropColumn(
                name: "AwardedSupplierName",
                table: "PurchaseRequisitions");

            migrationBuilder.DropColumn(
                name: "LastAwardId",
                table: "BudgetReservations");

            migrationBuilder.DropColumn(
                name: "AwardId",
                table: "BudgetReservationChanges");
        }
    }
}
