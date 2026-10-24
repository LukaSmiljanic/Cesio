using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Fees.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReversalWithdrawal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_fee_charges_reverses_id",
                schema: "fees",
                table: "fee_charges");

            migrationBuilder.CreateIndex(
                name: "ix_fee_charges_reverses_id",
                schema: "fees",
                table: "fee_charges",
                column: "reverses_id",
                unique: true,
                filter: "[reverses_id] IS NOT NULL AND [status] <> 'Withdrawn'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_fee_charges_reverses_id",
                schema: "fees",
                table: "fee_charges");

            migrationBuilder.CreateIndex(
                name: "ix_fee_charges_reverses_id",
                schema: "fees",
                table: "fee_charges",
                column: "reverses_id",
                unique: true,
                filter: "[reverses_id] IS NOT NULL");
        }
    }
}
