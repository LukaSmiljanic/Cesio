using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Fees.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReversalApprovedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approved_at",
                schema: "fees",
                table: "fee_charges",
                type: "datetimeoffset(3)",
                precision: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "approved_at",
                schema: "fees",
                table: "fee_charges");
        }
    }
}
