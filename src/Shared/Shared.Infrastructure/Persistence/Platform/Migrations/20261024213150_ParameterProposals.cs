using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shared.Infrastructure.Persistence.Platform.Migrations
{
    /// <inheritdoc />
    public partial class ParameterProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parameter_proposals",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    param_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    proposed_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    proposed_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    approved_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parameter_proposals", x => x.id);
                    table.CheckConstraint("ck_parameter_proposal_four_eyes", "[approved_by] IS NULL OR [approved_by] <> [proposed_by]");
                    table.CheckConstraint("ck_parameter_proposal_value_json", "ISJSON([value], VALUE) = 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_parameter_proposals_status",
                schema: "platform",
                table: "parameter_proposals",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parameter_proposals",
                schema: "platform");
        }
    }
}
