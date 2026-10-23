using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Marketplace.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Offers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "offers",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    listing_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    factor_org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    recourse_type = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    funding_deadline_bdays = table.Column<int>(type: "int", nullable: false),
                    valid_until = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    conditions_text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    fee_schedule_version = table.Column<int>(type: "int", nullable: false),
                    platform_fee = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    seller_fee = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    vat_pending = table.Column<bool>(type: "bit", nullable: false),
                    state = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    rejection_reason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    purchase_price = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offers", x => x.id);
                    table.CheckConstraint("ck_offer_currency", "[currency] IN ('RSD','EUR')");
                    table.CheckConstraint("ck_offer_fees", "[platform_fee] >= 0 AND [seller_fee] >= 0 AND [seller_fee] <= [platform_fee]");
                    table.CheckConstraint("ck_offer_funding_deadline", "[funding_deadline_bdays] BETWEEN 1 AND 30");
                    table.CheckConstraint("ck_offer_purchase_price", "[purchase_price] > 0");
                    table.ForeignKey(
                        name: "fk_offers_listings_listing_id",
                        column: x => x.listing_id,
                        principalSchema: "marketplace",
                        principalTable: "listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_offers_factor_org_id_state",
                schema: "marketplace",
                table: "offers",
                columns: new[] { "factor_org_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ux_offer_accepted_per_listing",
                schema: "marketplace",
                table: "offers",
                column: "listing_id",
                unique: true,
                filter: "[state] = 'Accepted'");

            migrationBuilder.CreateIndex(
                name: "ux_offer_active_per_factor",
                schema: "marketplace",
                table: "offers",
                columns: new[] { "listing_id", "factor_org_id" },
                unique: true,
                filter: "[state] = 'Active'");

            // Offers are immutable except for their state (ADR-013, database.md §1): an AFTER UPDATE trigger and a column DENY
            // for the application role on the financial columns.
            migrationBuilder.Sql($"""
                CREATE TRIGGER marketplace.{MarketplaceDbContext.OfferImmutableTrigger} ON marketplace.offers
                AFTER UPDATE AS
                IF UPDATE(id) OR UPDATE(listing_id) OR UPDATE(factor_org_id) OR UPDATE(purchase_price) OR UPDATE(currency)
                   OR UPDATE(recourse_type) OR UPDATE(funding_deadline_bdays) OR UPDATE(valid_until) OR UPDATE(conditions_text)
                   OR UPDATE(fee_schedule_version) OR UPDATE(platform_fee) OR UPDATE(seller_fee) OR UPDATE(vat_pending)
                   OR UPDATE(created_by) OR UPDATE(created_at)
                    THROW 51000, 'An offer is immutable except for its state.', 1;
                """);
            migrationBuilder.Sql("""
                IF DATABASE_PRINCIPAL_ID('app_rw') IS NULL CREATE ROLE app_rw;
                DENY UPDATE (listing_id, factor_org_id, purchase_price, currency, recourse_type, funding_deadline_bdays, valid_until,
                    conditions_text, fee_schedule_version, platform_fee, seller_fee, vat_pending, created_by, created_at)
                    ON marketplace.offers TO app_rw;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DROP TRIGGER marketplace.{MarketplaceDbContext.OfferImmutableTrigger};");

            migrationBuilder.DropTable(
                name: "offers",
                schema: "marketplace");
        }
    }
}
