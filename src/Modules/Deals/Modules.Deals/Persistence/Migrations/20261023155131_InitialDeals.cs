using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Deals.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialDeals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "deals");

            migrationBuilder.CreateSequence(
                name: "deal_reference_seq",
                schema: "deals");

            migrationBuilder.CreateTable(
                name: "deals",
                schema: "deals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reference = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    receivable_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    listing_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    offer_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    seller_org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    factor_org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    purchase_price = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    recourse_type = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    funding_deadline_bdays = table.Column<int>(type: "int", nullable: false),
                    conditions_text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    fee_schedule_version = table.Column<int>(type: "int", nullable: false),
                    platform_fee = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    seller_fee = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    vat_pending = table.Column<bool>(type: "bit", nullable: false),
                    state = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    signature_deadline = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    funding_deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    accepted_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    accepted_step_up_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    face_value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deals", x => x.id);
                    table.CheckConstraint("ck_deal_currency", "[currency] IN ('RSD','EUR')");
                    table.CheckConstraint("ck_deal_fees", "[platform_fee] >= 0 AND [seller_fee] >= 0 AND [seller_fee] <= [platform_fee]");
                    table.CheckConstraint("ck_deal_prices", "[purchase_price] > 0 AND [purchase_price] < [face_value]");
                });

            migrationBuilder.CreateTable(
                name: "deal_state_history",
                schema: "deals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    deal_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    from_state = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    to_state = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deal_state_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_deal_state_history_deals_deal_id",
                        column: x => x.deal_id,
                        principalSchema: "deals",
                        principalTable: "deals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_deal_state_history_deal_id_at",
                schema: "deals",
                table: "deal_state_history",
                columns: new[] { "deal_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_deals_factor_org_id_state",
                schema: "deals",
                table: "deals",
                columns: new[] { "factor_org_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_deals_offer_id",
                schema: "deals",
                table: "deals",
                column: "offer_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_deals_reference",
                schema: "deals",
                table: "deals",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_deals_seller_org_id_state",
                schema: "deals",
                table: "deals",
                columns: new[] { "seller_org_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ux_deal_live_per_receivable",
                schema: "deals",
                table: "deals",
                column: "receivable_id",
                unique: true,
                filter: "[state] <> 'Cancelled' AND [state] <> 'Terminated'");

            // Cross-schema references to stable rows (database.md §1).
            migrationBuilder.Sql("ALTER TABLE [deals].[deals] ADD CONSTRAINT [fk_deals_receivable] FOREIGN KEY ([receivable_id]) REFERENCES [receivables].[receivables] ([id]);");
            migrationBuilder.Sql("ALTER TABLE [deals].[deals] ADD CONSTRAINT [fk_deals_offer] FOREIGN KEY ([offer_id]) REFERENCES [marketplace].[offers] ([id]);");

            // The history is append-only and the deal's parties and terms never change (database.md §1).
            migrationBuilder.Sql($"""
                CREATE TRIGGER deals.{DealsDbContext.HistoryAppendOnlyTrigger} ON deals.deal_state_history
                INSTEAD OF UPDATE, DELETE AS
                THROW 51000, 'deals.deal_state_history is append-only.', 1;
                """);
            migrationBuilder.Sql($"""
                CREATE TRIGGER deals.{DealsDbContext.DealImmutableTrigger} ON deals.deals
                AFTER UPDATE AS
                IF UPDATE(id) OR UPDATE(reference) OR UPDATE(receivable_id) OR UPDATE(listing_id) OR UPDATE(offer_id)
                   OR UPDATE(seller_org_id) OR UPDATE(factor_org_id) OR UPDATE(face_value) OR UPDATE(purchase_price) OR UPDATE(currency)
                   OR UPDATE(recourse_type) OR UPDATE(funding_deadline_bdays) OR UPDATE(conditions_text) OR UPDATE(fee_schedule_version)
                   OR UPDATE(platform_fee) OR UPDATE(seller_fee) OR UPDATE(vat_pending) OR UPDATE(accepted_by) OR UPDATE(accepted_at)
                   OR UPDATE(accepted_step_up_at) OR UPDATE(signature_deadline)
                    THROW 51000, 'The parties and terms of a deal are immutable.', 1;
                """);
            migrationBuilder.Sql("""
                IF DATABASE_PRINCIPAL_ID('app_rw') IS NULL CREATE ROLE app_rw;
                DENY UPDATE (reference, receivable_id, listing_id, offer_id, seller_org_id, factor_org_id, face_value, purchase_price, currency,
                    recourse_type, funding_deadline_bdays, conditions_text, fee_schedule_version, platform_fee, seller_fee, vat_pending,
                    accepted_by, accepted_at, accepted_step_up_at, signature_deadline) ON deals.deals TO app_rw;
                DENY UPDATE, DELETE ON deals.deal_state_history TO app_rw;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DROP TRIGGER deals.{DealsDbContext.DealImmutableTrigger};");
            migrationBuilder.Sql($"DROP TRIGGER deals.{DealsDbContext.HistoryAppendOnlyTrigger};");

            migrationBuilder.DropTable(
                name: "deal_state_history",
                schema: "deals");

            migrationBuilder.DropTable(
                name: "deals",
                schema: "deals");

            migrationBuilder.DropSequence(
                name: "deal_reference_seq",
                schema: "deals");
        }
    }
}
