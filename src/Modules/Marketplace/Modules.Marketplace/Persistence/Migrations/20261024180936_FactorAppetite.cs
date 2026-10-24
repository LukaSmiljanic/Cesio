using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Marketplace.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FactorAppetite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "factor_appetite",
                schema: "marketplace",
                columns: table => new
                {
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    min_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    max_amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    min_tenor_days = table.Column<int>(type: "int", nullable: true),
                    max_tenor_days = table.Column<int>(type: "int", nullable: true),
                    digest_enabled = table.Column<bool>(type: "bit", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_factor_appetite", x => x.org_id);
                    table.CheckConstraint("ck_factor_appetite_currency", "[currency] IN ('RSD','EUR')");
                    table.CheckConstraint("ck_factor_appetite_range", "([min_amount] IS NULL OR [min_amount] >= 0) AND ([max_amount] IS NULL OR [max_amount] >= 0) AND ([min_amount] IS NULL OR [max_amount] IS NULL OR [min_amount] <= [max_amount])");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "factor_appetite",
                schema: "marketplace");
        }
    }
}
