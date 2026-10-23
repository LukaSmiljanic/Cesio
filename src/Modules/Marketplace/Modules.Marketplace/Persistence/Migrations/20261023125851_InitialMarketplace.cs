using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Marketplace.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMarketplace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "marketplace");

            migrationBuilder.CreateTable(
                name: "listings",
                schema: "marketplace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    receivable_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    seller_org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    state = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    opens_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    closes_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    audience_type = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    reserve_price = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    maturity_date = table.Column<DateOnly>(type: "date", nullable: false),
                    seller_pib = table.Column<string>(type: "char(9)", unicode: false, fixedLength: true, maxLength: 9, nullable: false),
                    debtor_pib = table.Column<string>(type: "char(9)", unicode: false, fixedLength: true, maxLength: 9, nullable: false),
                    suspended_reason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    version = table.Column<int>(type: "int", nullable: false),
                    face_value = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listings", x => x.id);
                    table.CheckConstraint("ck_listing_currency", "[currency] IN ('RSD','EUR')");
                    table.CheckConstraint("ck_listing_dates", "[closes_at] > [opens_at]");
                    table.CheckConstraint("ck_listing_reserve_price", "[reserve_price] IS NULL OR ([reserve_price] > 0 AND [reserve_price] < [face_value])");
                });

            migrationBuilder.CreateTable(
                name: "listing_audience",
                schema: "marketplace",
                columns: table => new
                {
                    listing_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    factor_org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    mode = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listing_audience", x => new { x.listing_id, x.factor_org_id });
                    table.CheckConstraint("ck_listing_audience_mode", "[mode] IN ('Include','Exclude')");
                    table.ForeignKey(
                        name: "fk_listing_audience_listings_listing_id",
                        column: x => x.listing_id,
                        principalSchema: "marketplace",
                        principalTable: "listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_listing_audience_factor_org_id_mode",
                schema: "marketplace",
                table: "listing_audience",
                columns: new[] { "factor_org_id", "mode" });

            migrationBuilder.CreateIndex(
                name: "ix_listings_seller_org_id_state",
                schema: "marketplace",
                table: "listings",
                columns: new[] { "seller_org_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_listings_state_closes_at",
                schema: "marketplace",
                table: "listings",
                columns: new[] { "state", "closes_at" });

            migrationBuilder.CreateIndex(
                name: "ux_listing_open_per_receivable",
                schema: "marketplace",
                table: "listings",
                column: "receivable_id",
                unique: true,
                filter: "[state] = 'Open'");

            // Cross-schema reference to a stable row (database.md §1).
            migrationBuilder.Sql("ALTER TABLE [marketplace].[listings] ADD CONSTRAINT [fk_listings_receivable] FOREIGN KEY ([receivable_id]) REFERENCES [receivables].[receivables] ([id]);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "listing_audience",
                schema: "marketplace");

            migrationBuilder.DropTable(
                name: "listings",
                schema: "marketplace");
        }
    }
}
