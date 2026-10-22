using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Template.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase28ReliableMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MessagingInbox",
                columns: table => new
                {
                    ConsumerName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessagingInbox", x => new { x.ConsumerName, x.EventId });
                });

            migrationBuilder.CreateTable(
                name: "MessagingOutbox",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    EventVersion = table.Column<int>(type: "integer", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CausationId = table.Column<Guid>(type: "uuid", nullable: true),
                    AggregateType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregateVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClaimOwner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ClaimExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DispatchedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PoisonedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastFailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessagingOutbox", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "MessagingProcurementProjections",
                columns: table => new
                {
                    ConsumerName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AggregateType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    LastEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastAggregateVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProcessedCount = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessagingProcurementProjections", x => new { x.ConsumerName, x.AggregateType, x.AggregateId, x.EventType });
                });

            migrationBuilder.CreateIndex(
                name: "IX_MessagingInbox_CompletedAtUtc",
                table: "MessagingInbox",
                column: "CompletedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MessagingInbox_ReceivedAtUtc",
                table: "MessagingInbox",
                column: "ReceivedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MessagingOutbox_AggregateId_CreatedAtUtc_EventId",
                table: "MessagingOutbox",
                columns: new[] { "AggregateId", "CreatedAtUtc", "EventId" });

            migrationBuilder.CreateIndex(
                name: "IX_MessagingOutbox_ClaimExpiresAtUtc",
                table: "MessagingOutbox",
                column: "ClaimExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MessagingOutbox_DispatchedAtUtc_PoisonedAtUtc_NextAttemptAt~",
                table: "MessagingOutbox",
                columns: new[] { "DispatchedAtUtc", "PoisonedAtUtc", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MessagingProcurementProjections_LastEventId",
                table: "MessagingProcurementProjections",
                column: "LastEventId");

            migrationBuilder.CreateIndex(
                name: "IX_MessagingProcurementProjections_UpdatedAtUtc",
                table: "MessagingProcurementProjections",
                column: "UpdatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MessagingInbox");

            migrationBuilder.DropTable(
                name: "MessagingOutbox");

            migrationBuilder.DropTable(
                name: "MessagingProcurementProjections");
        }
    }
}
