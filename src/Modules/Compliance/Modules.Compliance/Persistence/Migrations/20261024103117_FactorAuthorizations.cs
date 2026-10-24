using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Modules.Compliance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FactorAuthorizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "factor_authorizations",
                schema: "compliance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    org_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    issuing_authority = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    decision_number = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    decision_date = table.Column<DateOnly>(type: "date", nullable: true),
                    evidence_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    official_source_url = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    source_snapshot_document_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    verification_method = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true),
                    status = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    first_approver = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    verified_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    reverify_by = table.Column<DateOnly>(type: "date", nullable: true),
                    decision_reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    decided_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    submitted_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_factor_authorizations", x => x.id);
                    table.CheckConstraint("ck_factor_auth_decision", "[kind] = 'Bank' OR ([decision_number] IS NOT NULL AND [decision_date] IS NOT NULL AND [evidence_document_id] IS NOT NULL)");
                    table.CheckConstraint("ck_factor_auth_four_eyes", "[verified_by] IS NULL OR [first_approver] IS NULL OR [verified_by] <> [first_approver]");
                });

            migrationBuilder.CreateIndex(
                name: "ix_factor_authorizations_status_reverify_by",
                schema: "compliance",
                table: "factor_authorizations",
                columns: new[] { "status", "reverify_by" });

            migrationBuilder.CreateIndex(
                name: "ux_factor_auth_active",
                schema: "compliance",
                table: "factor_authorizations",
                column: "org_id",
                unique: true,
                filter: "[status] = 'Verified'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "factor_authorizations",
                schema: "compliance");
        }
    }
}
